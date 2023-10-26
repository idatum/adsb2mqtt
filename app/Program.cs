using adsb2mqtt;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
        services.AddSingleton<IFindAircraftType, FindAircraftType>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = false;
            options.TimestampFormat = "HH:mm:ss ";
        });
    })
    .Build();

host.Run();
