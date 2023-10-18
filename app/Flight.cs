namespace adsb2mqtt
{
    using System;

    public struct Flight
    {
        private string callSign;

        public string Icao;
        public string Callsign
        {
            get { return string.IsNullOrEmpty(callSign) ? Icao : callSign; }
            set { callSign = value; }
        }
        public string Altitude;
        public string Direction;
        public string Latitude;
        public string Longitude;
        public string? AircraftType;
        public DateTime LogDateTime;

        public bool Complete
        {
            get
            { 
                return !(string.IsNullOrEmpty(Icao) ||
                        string.IsNullOrEmpty(Altitude) ||
                        string.IsNullOrEmpty(Direction) ||
                        string.IsNullOrEmpty(Latitude) ||
                        string.IsNullOrEmpty(Longitude) ||
                        LogDateTime.Ticks == new DateTime().Ticks);
            }
        }
    }
}