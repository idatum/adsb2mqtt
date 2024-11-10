namespace adsb2mqtt;
using System.Text.Json;

public class FindAircraftType : IFindAircraftType
{
    private readonly ILogger<FindAircraftType> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _aircraftDbPath;

    public FindAircraftType(ILogger<FindAircraftType> logger,
                            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        _configuration = configuration;
        // dump1090 aircraft database path.
        _aircraftDbPath = _configuration.GetValue<string>("AIRCRAFT_DB_PATH") ?? throw new InvalidOperationException("AIRCRAFT_DB_PATH");
    }

    public string? Find(string icao)
    {
        // Scan json files for aircraft type.
        for (int i = icao.Length; i >= 1; --i)
        {
            string jsonText;
            JsonDocument jsonDoc;
            try
            {
                var filename = $"{_aircraftDbPath}/{icao[..i]}.json";
                if (!File.Exists(filename))
                {
                    continue;
                }
                _logger.LogDebug("Reading {filename}.", filename);
                jsonText = File.ReadAllText(filename);
                jsonDoc = JsonDocument.Parse(jsonText);
                if (!jsonDoc.RootElement.TryGetProperty(icao.Substring(i), out JsonElement icaoElement))
                {
                    _logger.LogDebug("No property {icao[i..]}.", icao[i..]);
                    continue;
                }
                _logger.LogDebug("Found icao element {icao[..i]} in {filename}.", icao[i..], filename);
                if (!icaoElement.TryGetProperty("t", out JsonElement aircraftTypeElement))
                {
                    _logger.LogDebug("No property type property.");
                    continue;
                }
                var aircraftType = aircraftTypeElement.GetString();
                _logger.LogDebug("Aircraft type: {aircraftType}", aircraftType);
                return aircraftType;
            }
            catch (Exception ex)
            {
                _logger.LogError("{ex}", ex);
                continue;
            }
        }
        _logger.LogDebug("No aircraft info for {icao}.", icao);

        return string.Empty;
    }
}
