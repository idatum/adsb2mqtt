namespace adsb2mqtt;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IFindAircraftType _findAircraftType;
    private readonly IConfiguration _configuration;

    public Worker(IConfiguration configuration,
                  ILogger<Worker> logger,
                  IFindAircraftType findAircraftType)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }
        if (findAircraftType is null)
        {
            throw new ArgumentNullException(nameof(findAircraftType));
        }
        _configuration = configuration;
        _logger = logger;
        _findAircraftType = findAircraftType;
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
