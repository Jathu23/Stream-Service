using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stream_Service.Models;
using Stream_Service.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Stream_Service.BackgroundServices
{
    public class StreamProcessingService : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<StreamProcessingService> _logger;
        private readonly StreamBufferManager _bufferManager;
        private readonly int _chunkSize = 64 * 1024; // 64KB chunks
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Long timeout for streaming
        };

        public StreamProcessingService(IConfiguration config, ILogger<StreamProcessingService> logger, StreamBufferManager bufferManager)
        {
            _config = config;
            _logger = logger;
            _bufferManager = bufferManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var stations = _config.GetSection("Stations").Get<List<Station>>();
            if (stations == null || stations.Count == 0)
            {
                _logger.LogWarning("No stations configured in appsettings.json");
                return;
            }

            var tasks = stations
                .Where(s => s.IsActive)
                .Select(s => ProcessStationAsync(s, stoppingToken))
                .ToArray();

            _logger.LogInformation("Starting {Count} station processors", tasks.Length);
            await Task.WhenAll(tasks);
        }

        private async Task ProcessStationAsync(Station station, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting continuous stream recording for station: {StationId} from {Url}", station.Id, station.Url);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // ✅ Use static HttpClient - prevents socket exhaustion
                    // Get the stream
                    using var stream = await _httpClient.GetStreamAsync(station.Url, stoppingToken);
                    _logger.LogInformation("Connected to stream for {StationId}", station.Id);

                    var buffer = new byte[_chunkSize];
                    int bytesRead;
                    int chunkCount = 0;

                    // Continuously read chunks from the stream
                    while (!stoppingToken.IsCancellationRequested &&
                           (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken)) > 0)
                    {
                        // Create a copy of the actual data read
                        var chunk = new byte[bytesRead];
                        Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);

                        // Add to circular buffer
                        _bufferManager.AddChunk(station.Id, chunk);

                        chunkCount++;
                        if (chunkCount % 100 == 0) // Log every 100 chunks
                        {
                            _logger.LogDebug("Station {StationId}: Recorded {Count} chunks", station.Id, chunkCount);
                        }
                    }

                    _logger.LogWarning("Stream ended for {StationId}, reconnecting...", station.Id);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Stream recording cancelled for {StationId}", station.Id);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recording stream for {StationId}, retrying in 5 seconds...", station.Id);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Stopped stream recording for {StationId}", station.Id);
        }
    }
}