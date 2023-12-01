namespace adsb2mqtt;
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
using MQTTnet.Exceptions;

public class Dump1090Reader
{
    private readonly static int FlightRecordTTLSecs = 300;

    private double _radiusNauticalMiles;
    private double _baseLatitudeRad;
    private double _baseLongitudeRad;
    private string? _topicBase;
    private ConcurrentDictionary<string, Flight> _icaoFlight = new();
    private ConcurrentDictionary<string, Flight> _trackedIcaoFlight = new();
    private IMqttClient? _mqttClient;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<Worker> _logger;
    private readonly IFindAircraftType _findAircraftType;

    public Dump1090Reader(ILogger<Worker> logger,
                          IConfiguration configuration,
                          IFindAircraftType findAircraftType)
    {
        if (logger is null)
        {
            throw new ArgumentNullException("logger");
        }
        if (configuration is null)
        {
            throw new ArgumentNullException("configuration");
        }
        if (findAircraftType is null)
        {
            throw new ArgumentNullException("findAircraftType");
        }
        _logger = logger;
        _configuration = configuration;
        _findAircraftType = findAircraftType;
    }

    private double GetNauticalMiles(double latitude, double longitude)
    {
        // Calculate the great circle distance between receiver and aircraftr points 
        // on the earth (specified in decimal degrees)
        const double EarthRadiusInNauticalMiles = 3440.07;

        var latRad = Math.PI * latitude / 180.0;
        var lonRad = Math.PI * longitude / 180.0;
        var dlat = _baseLatitudeRad - latRad;
        var dlon = _baseLongitudeRad - lonRad;

        var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(latRad) * Math.Cos(_baseLatitudeRad) * Math.Pow(Math.Sin(dlon / 2), 2);
        var c = 2 * Math.Asin(Math.Sqrt(a));

        return Math.Round(EarthRadiusInNauticalMiles * c, 4);
    }

    private void GroomFlightsCallback(object? source, ElapsedEventArgs e)
    {
        var now = DateTime.UtcNow;
        var staleFlights = _icaoFlight.Where(pair => (now - pair.Value.LogDateTime).TotalSeconds > FlightRecordTTLSecs)
                                    .Select(pair => pair.Key)
                                    .ToList();

        foreach (var icao in staleFlights)
        {
            Flight staleFlight;
            if (!_icaoFlight.TryRemove(icao, out staleFlight))
            {
                _logger.LogInformation($"Unable to drop stale flight {staleFlight.Icao} last seen {staleFlight.LogDateTime} UTC");
            }
        }
    }

    private bool MqttConnected()
    {
        if (_mqttClient == null)
        {
            throw new InvalidOperationException("_mqttClient");
        }

        return _mqttClient.IsConnected;
    }

    private Socket? ConnectSocket(string server, int port)
    {
        Socket? socket = null;
        var hostEntry = Dns.GetHostEntry(server);

        // Loop through the AddressList to obtain the supported AddressFamily.
        foreach (IPAddress address in hostEntry.AddressList)
        {
            IPEndPoint ipe = new IPEndPoint(address, port);
            var tempSocket =
                new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            tempSocket.Connect(ipe);

            if (tempSocket.Connected)
            {
                socket = tempSocket;
                break;
            }
            else
            {
                continue;
            }
        }
        _logger.LogInformation("Socket connected");
        return socket;
    }

    private async void PublishRecordsCallback(object? source, ElapsedEventArgs e)
    {
        try
        {
            await PublishRecordsAsync();
        }
        catch (MqttClientDisconnectedException ex)
        {
            _logger.LogError(ex.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            throw;
        }
    }

    private async Task PublishRecordsAsync()
    {
        if (_mqttClient == null)
        {
            throw new InvalidOperationException("_mqttClient");
        }
        if (!MqttConnected())
        {
            _logger.LogDebug("MQTT not connected; skipping publish.");
            return;
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

    private async Task<bool> ConnectMqtt(string username, string password, string host, int port, bool useTls)
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
            _logger.LogInformation("MQTT connected");
            return Task.CompletedTask;
        };
        _mqttClient.DisconnectedAsync += (MqttClientDisconnectedEventArgs args) =>
        {
            _logger.LogInformation("MQTT disconnected");
            return Task.CompletedTask;
        };
        var connectResult = await _mqttClient.ConnectAsync(options);
        return connectResult.ResultCode == MqttClientConnectResultCode.Success;
    }

    private async Task DisconnectMqtt()
    {
        await _mqttClient.DisconnectAsync();
    }

    private void UpdateFlight(string icao, string[] recordSplit, Flight lastFlight, out Flight newflight)
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
            newflight.AircraftType = _findAircraftType.Find(icao);
        }
    }

    private void HandleRecord(String record)
    {
        string[] recordSplit = record.Split(',');
        if (recordSplit.Length < 5)
        {
            _logger.LogInformation($"Invalid record: {record}");
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

    private void ReceiveDump1090(string server, int port, CancellationToken stoppingToken)
    {
        using var socket = ConnectSocket(server, port);
        if (socket == null)
        {
            throw new ApplicationException("Socket connection failed");
        }
        var readBuffer = new Byte[1024];
        int bytesRead = 0;
        var recordBytes = new List<Byte>();
        while (!stoppingToken.IsCancellationRequested && socket.Connected && MqttConnected())
        {
            bytesRead = socket.Receive(readBuffer);
            if (0 == bytesRead)
            {
                _logger.LogInformation("Socket reconnecting.");
                return;
            }
            for (int i = 0; i < bytesRead; ++i)
            {
                if ('\n' == readBuffer[i])
                {
                    var recordArray = recordBytes.ToArray();
                    var record = Encoding.ASCII.GetString(recordArray, 0, recordArray.Length).Trim();
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
        _logger.LogInformation("Socket disconnected");
    }

    /// <summary>
    /// Process ADS-B messages and build up full flight details
    /// from multiple messages; track flights within threshold
    /// in nautical miles; publish tracked flights to MQTT.
    /// </summary>
    public async Task ProcessAsync(CancellationToken stoppingToken)
    {
        if (_configuration is null)
        {
            throw new InvalidOperationException("_configuration");
        }
        // Base station coordinates
        _radiusNauticalMiles = _configuration.GetValue<double>("RADIUS_NM");
        _baseLatitudeRad = _configuration.GetValue<double>("LATITUDE") * Math.PI / 180.0;
        _baseLongitudeRad = _configuration.GetValue<double>("LONGITUDE") * Math.PI / 180.0;
        // TCP BaseStation output host and port.
        var beastHost = _configuration["BEAST_HOST"];
        if (beastHost is null)
        {
            throw new InvalidOperationException("BEAST_HOST");
        }
        int beastPort = _configuration.GetValue<int>("BEAST_PORT");
        // MQTT
        _topicBase = _configuration["TOPIC_BASE"];
        if (_topicBase is null)
        {
            throw new InvalidOperationException("_topicBase");
        }
        var mqttUsername = _configuration["MQTT_USERNAME"];
        if (mqttUsername is null)
        {
            throw new InvalidOperationException("MQTT_USERNAME");
        }
        var mqttPassword = _configuration["MQTT_PASSWORD"];
        if (mqttPassword is null)
        {
            throw new InvalidOperationException("MQTT_USERNAME");
        }
        var mqttServer = _configuration["MQTT_SERVER"];
        if (mqttServer is null)
        {
            throw new InvalidOperationException("MQTT_SERVER");
        }
        int mqttPort = _configuration.GetValue<int>("MQTT_PORT");
        bool mqttUseTls = _configuration.GetValue<bool>("MQTT_USE_TLS");

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ConnectMqtt(mqttUsername, mqttPassword, mqttServer, mqttPort, mqttUseTls))
                {
                    _logger.LogInformation("Could not connect to MQTT");
                    await Task.Delay(5000);
                    continue;
                }
                ReceiveDump1090(beastHost, beastPort, stoppingToken);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex.Message);
                await Task.Delay(5000);
            }
            catch (MqttCommunicationException ex)
            {
                _logger.LogError(ex.ToString());
                await DisconnectMqtt();
                await Task.Delay(5000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                await DisconnectMqtt();
                throw;
            }
        }
    }
}
