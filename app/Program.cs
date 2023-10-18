namespace adsb2mqtt
{
    class Program
    {
        public static async Task Main(string[] _)
        {
            await Dump1090Reader.Process();
        }
    }
}
