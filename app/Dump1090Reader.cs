namespace adsb2mqtt
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Net;
    using System.Net.Sockets;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using System.Timers;
    using System.IO;
    using System.Text.Json;
    using Microsoft.Extensions.Configuration;

    using MQTTnet;
    using MQTTnet.Client;
    using MQTTnet.Formatter;
    using MQTTnet.Protocol;
    using MQTTnet.Server;

    public class Dump1090Reader
    {
        private readonly static int FlightRecordTTLSecs = 300;

        private static double _radiusNauticalMiles;
        private static string? _aircraftDbPath;
        private static double _baseLatitudeRad;
        private static double _baseLongitudeRad;
        private static string? _topicBase;
        private static ConcurrentDictionary<string, Flight> _icaoFlight = new ();
        private static ConcurrentDictionary<string, Flight> _trackedIcaoFlight = new ();
        private static IMqttClient? _mqttClient;
        private static IConfigurationRoot? _configuration;
        private static Tracing _tracing = new();

        private static string? FindAircraftType(string icao)
        {
            // Scan json files for aircraft type.
            for (int i = icao.Length; i >= 1; --i)
            {
                string jsonText;
                JsonDocument jsonDoc;
                try
                {
                    var filename = $"{_aircraftDbPath}/{icao.Substring(0, i)}.json";
                    if (!File.Exists(filename))
                    {
                        continue;
                    }
                    _tracing.Debug($"Reading {filename}.");
                    jsonText = File.ReadAllText(filename);
                    jsonDoc = JsonDocument.Parse(jsonText);
                    JsonElement icaoElement;
                    if (!jsonDoc.RootElement.TryGetProperty(icao.Substring(i), out icaoElement))
                    {
                        _tracing.Debug($"No property {icao.Substring(i)}.");
                        continue;
                    }
                    _tracing.Debug($"Found icao element {icao.Substring(i)} in {filename}.");
                    JsonElement aircraftTypeElement;
                    if (!icaoElement.TryGetProperty("t", out aircraftTypeElement))
                    {
                        _tracing.Debug("No property type property.");
                        continue;
                    }
                    var aircraftType = aircraftTypeElement.GetString();
                    _tracing.Debug($"Aircraft type: {aircraftType}");
                    return aircraftType;
                }
                catch(Exception ex)
                {
                    _tracing.Error(ex.ToString());
                    continue;
                }
            }
            _tracing.Debug($"No aircraft info for {icao}.");

            return string.Empty;
        }

        private static double GetNauticalMiles(double latitude, double longitude)
        {
            // Calculate the great circle distance between receiver and aircraftr points 
            // on the earth (specified in decimal degrees)

            // convert decimal degrees to radians 
            var latRad = Math.PI * latitude / 180.0;
            var lonRad = Math.PI * longitude / 180.0;
            // haversine formula 
            var dlat = _baseLatitudeRad - latRad;
            var dlon = _baseLongitudeRad - lonRad;
            var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(latRad) * Math.Cos(_baseLatitudeRad) * Math.Pow(Math.Sin(dlon / 2), 2);
            var c = 2 * Math.Asin(Math.Sqrt(a));
            // Radius of earth in nautical miles
            var nm = 3440.07 * c;

            return Math.Round(nm, 4);
        }

        private static void GroomFlightsCallback(object? source, ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;
            foreach (var icao in _icaoFlight.Keys)
            {
                if (_trackedIcaoFlight.ContainsKey(icao))
                {
                    continue;
                }
                var flight = _icaoFlight[icao];
                if ((now - _icaoFlight[flight.Icao].LogDateTime).TotalSeconds > FlightRecordTTLSecs)
                {
                    Flight staleFlight;
                    if (!_icaoFlight.TryRemove(flight.Icao, out staleFlight))
                    {
                        _tracing.Info($"Unable to drop stale flight {staleFlight.Icao} last seen {staleFlight.LogDateTime} UTC");
                    }
                    continue;
                }
            }
        }

        private static void UpdateFlight(string icao, string[] recordSplit, Flight lastFlight, out Flight newflight)
        {
            newflight = lastFlight;
            newflight.Icao = icao;
            var logTime = recordSplit[7];
            var logDate = recordSplit[6];
            newflight.LogDateTime = DateTime.Parse($"{logDate} {logTime}").ToUniversalTime();
            if (!string.IsNullOrEmpty(recordSplit[10]))
                newflight.Callsign = recordSplit[10].Trim();
            if (!string.IsNullOrEmpty(recordSplit[11]))
                newflight.Altitude = recordSplit[11].Trim();
            if (!string.IsNullOrEmpty(recordSplit[13]))
                newflight.Direction = recordSplit[13].Trim();
            if (!string.IsNullOrEmpty(recordSplit[14]))
                newflight.Latitude = recordSplit[14].Trim();
            if (!string.IsNullOrEmpty(recordSplit[15]))
                newflight.Longitude = recordSplit[15].Trim();
            if (string.IsNullOrEmpty(newflight.AircraftType))
            {
                newflight.AircraftType = FindAircraftType(icao);
            }
        }

        private static void HandleRecord(String record)
        {
            string[] recordSplit = record.Split(',');
            if (recordSplit.Length < 5)
            {
                _tracing.Warning($"Invalid record: {record}");
                return;
            }
            var msgType = recordSplit[0];
            var transType = recordSplit[1];
            var icao = recordSplit[4];
            Flight lastFlight;
            _icaoFlight.TryGetValue(icao, out lastFlight);
            Flight flight;
            UpdateFlight(icao, recordSplit, lastFlight, out flight);
            _icaoFlight.AddOrUpdate(icao, flight, (key, val) => flight);
            if (flight.Complete)
            {
                _trackedIcaoFlight.TryAdd(icao, flight);
            }
        }

        private static bool MqttConnected()
        {
            if (_mqttClient == null)
            {
                throw new InvalidOperationException("_mqttClient");
            }

            return _mqttClient.IsConnected;
        }

        private static void ReceiveDump1090(string server, int port)
        {
            using (var socket = ConnectSocket(server, port))
            {
                if (socket == null)
                {
                    throw new ApplicationException("Connection failed");
                }
                var readBuffer = new Byte[1024];
                int bytesRead = 0;
                var recordBytes = new List<Byte>();
                while (socket.Connected && MqttConnected())
                {
                    bytesRead = socket.Receive(readBuffer);
                    if (0 == bytesRead)
                    {
                        _tracing.Info("Socket reconnecting.");
                        return;
                    }
                    for (int i = 0; i < bytesRead; ++i)
                    {
                        if ('\n' == readBuffer[i])
                        {
                            var recordArray = recordBytes.ToArray();
                            var record = Encoding.ASCII.GetString(recordArray, 0, recordArray.Length);
                            if (!String.IsNullOrEmpty(record))
                            {
                                HandleRecord(record);
                            }
                            recordBytes.Clear();
                        }
                        else
                        {
                            recordBytes.Add(readBuffer[i]);
                        }
                    }
                }
            }
            _tracing.Info("Socket disconnected");
        }

        private static Socket? ConnectSocket(string server, int port)
        {
            Socket? socket = null;
            var hostEntry = Dns.GetHostEntry(server);

            // Loop through the AddressList to obtain the supported AddressFamily.
            foreach(IPAddress address in hostEntry.AddressList)
            {
                IPEndPoint ipe = new IPEndPoint(address, port);
                var tempSocket =
                    new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                tempSocket.Connect(ipe);

                if(tempSocket.Connected)
                {
                    socket = tempSocket;
                    break;
                }
                else
                {
                    continue;
                }
            }
            _tracing.Info("Socket connected");
            return socket;
        }

        private static async void PublishRecordsCallback(object? source, ElapsedEventArgs e)
        {
            try
            {
                await PublishRecordsAsync();
            }
            catch (Exception ex)
            {
                _tracing.Error(ex.ToString());
                throw;
            }
        }

        private static async Task PublishRecordsAsync()
        {
            if (_mqttClient == null)
            {
                throw new InvalidOperationException("_mqttClient");
            }
            Flight flight;
            foreach (var icao in _trackedIcaoFlight.Keys)
            {
                if (!_trackedIcaoFlight.TryRemove(icao, out flight))
                {
                    continue;
                }
                var nm = GetNauticalMiles(double.Parse(flight.Latitude), double.Parse(flight.Longitude));
                if (nm < _radiusNauticalMiles)
                {
                    var payloadText = $"{{ \"icao\":\"{flight.Icao}\",\"flt\":\"{flight.Callsign}\"," +
                        $"\"alt\":{flight.Altitude},\"dir\":{flight.Direction},\"lat\":{flight.Latitude}," +
                        $"\"lng\":{flight.Longitude},\"t\":\"{flight.AircraftType}\",\"nm\":{nm} }}";
                    var payload = Encoding.UTF8.GetBytes(payloadText);
                    var message = new MqttApplicationMessageBuilder()
                                        .WithTopic($"{_topicBase}/{icao}")
                                        .WithPayload(payload)
                                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel
                                        .AtLeastOnce).Build();
                    var pubResult = await _mqttClient.PublishAsync(message);
                }
            }
        }

        private static async Task<bool> ConnectMqtt(string username, string password, string host, int port, bool useTls)
        {
            var mqttFactory = new MqttFactory();
            var tlsOptions = new MqttClientTlsOptions
            {
                UseTls = useTls
            };
            var options = new MqttClientOptionsBuilder()
                            .WithCredentials(username, password)
                            .WithProtocolVersion(MqttProtocolVersion.V311)
                            .WithTcpServer(host, port)
                            .WithTlsOptions(tlsOptions)
                            .WithCleanSession(true)
                            .WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
                            .Build();
            _mqttClient = mqttFactory.CreateMqttClient();
            if (_mqttClient == null)
            {
                throw new InvalidOperationException("_mqttClient");
            }
            _mqttClient.ConnectedAsync += (MqttClientConnectedEventArgs args) =>
            {
                _tracing.Info("MQTT connected");
                return Task.CompletedTask;
            };
            _mqttClient.DisconnectedAsync += (MqttClientDisconnectedEventArgs args) =>
            {
                _tracing.Info("MQTT disconnected");
                return Task.CompletedTask;
            };
            var connectResult = await _mqttClient.ConnectAsync(options);
            return connectResult.ResultCode == MqttClientConnectResultCode.Success;
        }

        private static async Task DisconnectMqtt()
        {
            await _mqttClient.DisconnectAsync();
        }

        /// <summary>
        /// Process ADS-B messages and build up full flight details
        /// from multiple messages; track flights within threshold
        /// in nautical miles; publish tracked flights to MQTT.
        /// </summary>
        public static async Task Process()
        {
            _configuration = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json", optional:true, reloadOnChange:true)
                            .AddJsonFile("appsettings.Development.json", optional:true)
                            .AddEnvironmentVariables()
                            .Build();

            // Tracing config
            _tracing.TraceLevel = _configuration.GetValue<TraceLevel>("TRACE_LEVEL");
            // Base station coordinates
            _radiusNauticalMiles = _configuration.GetValue<double>("RADIUS_NM");
            _baseLatitudeRad = _configuration.GetValue<double>("LATITUDE") * Math.PI / 180.0;
            _baseLongitudeRad = _configuration.GetValue<double>("LONGITUDE") * Math.PI / 180.0;
            _aircraftDbPath = _configuration["AIRCRAFT_DB_PATH"];
            // dump1090 aircraft database path.
            if (_aircraftDbPath is null)
            {
                throw new ArgumentNullException("_aircraftDbPath");
            }
            // TCP BaseStation output host and port.
            var beastHost = _configuration["BEAST_HOST"];
            if (beastHost is null)
            {
                throw new ArgumentNullException("BEAST_HOST");
            }
            int beastPort = _configuration.GetValue<int>("BEAST_PORT");
            // MQTT
            _topicBase = _configuration["TOPIC_BASE"];
            if (_topicBase is null)
            {
                throw new ArgumentNullException("_topicBase");
            }
            var mqttUsername = _configuration["MQTT_USERNAME"];
            if (mqttUsername is null)
            {
                throw new ArgumentNullException("MQTT_USERNAME");
            }
            var mqttPassword = _configuration["MQTT_PASSWORD"];
            if (mqttPassword is null)
            {
                throw new ArgumentNullException("MQTT_USERNAME");
            }
            var mqttServer = _configuration["MQTT_SERVER"];
            if (mqttServer is null)
            {
                throw new ArgumentNullException("MQTT_SERVER");
            }
            int mqttPort = _configuration.GetValue<int>("MQTT_PORT");
            bool mqttUseTls = _configuration.GetValue<bool>("MQTT_USE_TLS");

            if (!await ConnectMqtt(mqttUsername, mqttPassword, mqttServer, mqttPort, mqttUseTls))
            {
                _tracing.Info("Could not connect to MQTT");
                return;
            }

            var publishTimer = new Timer();
            publishTimer.Elapsed += new ElapsedEventHandler(PublishRecordsCallback);
            publishTimer.Interval = 1 * 1000;
            publishTimer.AutoReset = true;
            publishTimer.Enabled = true;

            var groomTimer = new Timer();
            groomTimer.Elapsed += new ElapsedEventHandler(GroomFlightsCallback);
            groomTimer.Interval = 10 * 60 * 1000;
            groomTimer.AutoReset = true;
            groomTimer.Enabled = true;

            while (MqttConnected())
            {
                try
                {
                    ReceiveDump1090(beastHost, beastPort);
                }
                catch (SocketException ex)
                {
                    _tracing.Warning(ex.Message);
                    System.Threading.Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    _tracing.Error(ex.ToString());
                    await DisconnectMqtt();
                    throw;
                }
            }
       }
    }
}
