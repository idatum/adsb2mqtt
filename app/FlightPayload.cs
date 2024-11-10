using System.Text.Json.Serialization;

namespace adsb2mqtt;

public struct FlightPayload
{
    [JsonInclude]
    public string icao;
    [JsonInclude]
    public string flt;
    [JsonInclude]
    public int alt;
    [JsonInclude]
    public int dir;
    [JsonInclude]
    public double lat;
    [JsonInclude]
    public double lng;
    [JsonInclude]
    public string t;
    [JsonInclude]
    public double nm;
}