namespace adsb2mqtt;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IFindAircraftType _findAircraftType;
    private readonly IConfiguration _configuration;

    public Worker(ILogger<Worker> logger,
                  IFindAircraftType findAircraftType)
    {
        _logger = logger;
        _findAircraftType = findAircraftType;
        _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional:true, reloadOnChange:true)
                .AddEnvironmentVariables()
                .Build();
        if (_configuration is null)
        {
            throw new InvalidOperationException("_configuration");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        var reader = new Dump1090Reader(_logger, _configuration, _findAircraftType);
        while (!stoppingToken.IsCancellationRequested)
        {
            await reader.ProcessAsync(stoppingToken);
        }
        _logger.LogInformation("Worker exiting at: {time}", DateTimeOffset.Now);
    }
}
