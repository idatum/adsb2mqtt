namespace adsb2mqtt;

public interface IFindAircraftType
{
    String? Find(string icao);
}