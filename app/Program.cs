using adsb2mqtt;

var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .Build();

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(builder =>
    {
        builder.Sources.Clear();
        builder.AddConfiguration(configuration);
    })
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

