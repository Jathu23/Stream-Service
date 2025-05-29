using Stream_Service.Models;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Stream_Service.Services
{
    public class FMStreamService : IFMStreamService
    {
        private readonly StreamBufferManager _bufferManager;
        private readonly IConfiguration _config;
        private readonly ILogger<FMStreamService> _logger;

        public FMStreamService(StreamBufferManager bufferManager, IConfiguration config, ILogger<FMStreamService> logger)
        {
            _bufferManager = bufferManager;
            _config = config;
            _logger = logger;
        }

        public async IAsyncEnumerable<byte[]> GetLiveStreamAsync(string stationId)
        {
            var station = _config.GetSection("Stations").Get<List<Station>>().FirstOrDefault(s => s.Id == stationId);
            if (station == null)
            {
                _logger.LogError("Station {StationId} not found", stationId);
                yield break;
            }

            await foreach (var chunk in FetchStreamAsync(station.Url, stationId))
            {
                _bufferManager.AddChunk(stationId, chunk, DateTime.UtcNow);
                yield return chunk;
            }
        }

        private async IAsyncEnumerable<byte[]> FetchStreamAsync(string url, string stationId)
        {
            int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    using var client = new HttpClient();
                    using var stream = await client.GetStreamAsync(url);
                    using var reader = new Mp3FileReader(stream);

                    var buffer = new byte[1024 * 64]; // 64KB chunks
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        var chunk = buffer.Take(bytesRead).ToArray();
                        yield return chunk;
                    }
                    break; // Exit retry loop if successful
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching stream for {StationId}, retry {Retry}", stationId, retry + 1);
                    if (retry == maxRetries - 1)
                    {
                        _logger.LogError("Failed to fetch stream for {StationId} after {MaxRetries} retries", stationId, maxRetries);
                        yield break;
                    }
                    await Task.Delay(1000); // Wait 1s before retry
                }
            }
        }

        public async Task<byte[]> GetBufferedChunkAsync(string stationId, DateTime timestamp)
        {
            var chunk = _bufferManager.GetChunk(stationId, timestamp);
            if (chunk == null)
            {
                _logger.LogWarning("No chunk found for station {StationId} at {Timestamp}", stationId, timestamp);
            }
            return await Task.FromResult(chunk);
        }

        public async Task UpdateStationAsync(string stationId, string url)
        {
            _logger.LogInformation("Updated station {StationId} with URL {Url}", stationId, url);
            // In production, update config or DB
            await Task.CompletedTask;
        }
    }
}