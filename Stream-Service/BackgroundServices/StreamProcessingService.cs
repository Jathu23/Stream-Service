using Stream_Service.Models;
using Stream_Service.Services;

namespace Stream_Service.BackgroundServices
{
    public class StreamProcessingService : BackgroundService
    {
        private readonly IFMStreamService _streamService;
        private readonly IConfiguration _config;
        private readonly ILogger<StreamProcessingService> _logger;

        public StreamProcessingService(IFMStreamService streamService, IConfiguration config, ILogger<StreamProcessingService> logger)
        {
            _streamService = streamService;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var stations = _config.GetSection("Stations").Get<List<Station>>();
            var tasks = stations.Select(s => ProcessStationAsync(s, stoppingToken)).ToArray();
            await Task.WhenAll(tasks);
        }

        private async Task ProcessStationAsync(Station station, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting stream processing for {StationId}", station.Id);
            await foreach (var chunk in _streamService.GetLiveStreamAsync(station.Id).WithCancellation(stoppingToken))
            {
                // Chunks are stored in buffer by FMStreamService
                await Task.Delay(1000, stoppingToken); // Adjust based on chunk size
            }
        }
    }
}
