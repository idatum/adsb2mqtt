namespace adsb2mqtt;

using System.Text.Json;

public class FindAircraftType : IFindAircraftType
{
    private readonly ILogger<FindAircraftType> _logger;
    private readonly IConfiguration _configuration;
    private string? _aircraftDbPath;

    public FindAircraftType(ILogger<FindAircraftType> logger,
                            IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public string? Find(string icao)
    {
        if (_aircraftDbPath is null)
        {
            // dump1090 aircraft database path.
            _aircraftDbPath = _configuration.GetValue<string>("AIRCRAFT_DB_PATH");
            if (_aircraftDbPath is null)
            {
                throw new ArgumentNullException("AIRCRAFT_DB_PATH");
            }
        }

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
                _logger.LogDebug($"Reading {filename}.");
                jsonText = File.ReadAllText(filename);
                jsonDoc = JsonDocument.Parse(jsonText);
                JsonElement icaoElement;
                if (!jsonDoc.RootElement.TryGetProperty(icao.Substring(i), out icaoElement))
                {
                    _logger.LogDebug($"No property {icao.Substring(i)}.");
                    continue;
                }
                _logger.LogDebug($"Found icao element {icao.Substring(i)} in {filename}.");
                JsonElement aircraftTypeElement;
                if (!icaoElement.TryGetProperty("t", out aircraftTypeElement))
                {
                    _logger.LogDebug("No property type property.");
                    continue;
                }
                var aircraftType = aircraftTypeElement.GetString();
                _logger.LogDebug($"Aircraft type: {aircraftType}");
                return aircraftType;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex.ToString());
                continue;
            }
        }
        _logger.LogDebug($"No aircraft info for {icao}.");

        return string.Empty;
    }
}