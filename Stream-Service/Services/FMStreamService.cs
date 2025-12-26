using Stream_Service.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private const int MP3_BITRATE = 128000; // 128 kbps - standard MP3 bitrate

        public FMStreamService(StreamBufferManager bufferManager, IConfiguration config, ILogger<FMStreamService> logger)
        {
            _bufferManager = bufferManager;
            _config = config;
            _logger = logger;
        }

        public async IAsyncEnumerable<byte[]> GetLiveStreamAsync(string stationId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting live stream for {StationId}", stationId);

            var chunks = _bufferManager.GetAllChunks(stationId);
            if (chunks == null || chunks.Count == 0)
            {
                _logger.LogWarning("No buffered data available for {StationId}", stationId);
                yield break;
            }

            // Start streaming from the most recent chunks (last 5 seconds of buffer)
            var startTime = DateTime.UtcNow.AddSeconds(-5);
            var recentChunks = chunks.Where(c => c.Timestamp >= startTime).ToList();

            if (recentChunks.Count == 0)
            {
                recentChunks = chunks.TakeLast(10).ToList(); // Fallback to last 10 chunks
            }

            _logger.LogInformation("Streaming {Count} initial chunks for {StationId}", recentChunks.Count, stationId);

            // Stream initial buffered chunks
            foreach (var chunk in recentChunks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Live stream cancelled for {StationId}", stationId);
                    yield break;
                }

                yield return chunk.Data;
                await Task.Delay(CalculateChunkDelay(chunk.Data.Length), cancellationToken);
            }

            // Continue streaming new chunks as they arrive
            var lastChunkTime = recentChunks.Last().Timestamp;
            while (!cancellationToken.IsCancellationRequested)
            {
                var newChunks = _bufferManager.GetChunksFrom(stationId, lastChunkTime.AddMilliseconds(1));

                if (newChunks.Count > 0)
                {
                    foreach (var chunk in newChunks)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Live stream cancelled for {StationId}", stationId);
                            yield break;
                        }

                        yield return chunk.Data;
                        lastChunkTime = chunk.Timestamp;
                        await Task.Delay(CalculateChunkDelay(chunk.Data.Length), cancellationToken);
                    }
                }
                else
                {
                    // No new chunks yet, wait a bit
                    await Task.Delay(100, cancellationToken);
                }
            }

            _logger.LogInformation("Live stream ended for {StationId}", stationId);
        }

        public async IAsyncEnumerable<byte[]> GetBufferedStreamAsync(string stationId, DateTime startTimestamp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting buffered stream for {StationId} from {StartTime}", stationId, startTimestamp);

            var chunks = _bufferManager.GetChunksFrom(stationId, startTimestamp);
            if (chunks == null || chunks.Count == 0)
            {
                _logger.LogWarning("No buffered data available for {StationId} from {StartTime}", stationId, startTimestamp);
                yield break;
            }

            _logger.LogInformation("Streaming {Count} buffered chunks for {StationId}", chunks.Count, stationId);

            // Stream all buffered chunks from the requested time
            foreach (var chunk in chunks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Buffered stream cancelled for {StationId}", stationId);
                    yield break;
                }

                yield return chunk.Data;
                await Task.Delay(CalculateChunkDelay(chunk.Data.Length), cancellationToken);
            }

            // Once we catch up to live, continue with live streaming
            var lastChunkTime = chunks.Last().Timestamp;
            _logger.LogInformation("Caught up to live stream for {StationId}, continuing with live data", stationId);

            while (!cancellationToken.IsCancellationRequested)
            {
                var newChunks = _bufferManager.GetChunksFrom(stationId, lastChunkTime.AddMilliseconds(1));

                if (newChunks.Count > 0)
                {
                    foreach (var chunk in newChunks)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("Buffered stream cancelled for {StationId}", stationId);
                            yield break;
                        }

                        yield return chunk.Data;
                        lastChunkTime = chunk.Timestamp;
                        await Task.Delay(CalculateChunkDelay(chunk.Data.Length), cancellationToken);
                    }
                }
                else
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            _logger.LogInformation("Buffered stream ended for {StationId}", stationId);
        }

        public async Task UpdateStationAsync(string stationId, string url)
        {
            _logger.LogInformation("Updated station {StationId} with URL {Url}", stationId, url);
            await Task.CompletedTask;
        }

        // Calculate appropriate delay based on chunk size and bitrate
        // This ensures smooth playback without buffering
        private int CalculateChunkDelay(int chunkSizeBytes)
        {
            // Calculate duration of audio in the chunk
            // Formula: (bytes * 8 bits) / bitrate = seconds
            var durationSeconds = (chunkSizeBytes * 8.0) / MP3_BITRATE;
            var delayMs = (int)(durationSeconds * 1000);

            // Add small buffer to prevent underruns (10% extra time)
            return (int)(delayMs * 0.9);
        }

        public async IAsyncEnumerable<byte[]> FetchStreamAsync(string url, string stationId)
        {
            // Not implemented - handled by StreamProcessingService
            _logger.LogWarning("FetchStreamAsync called but not implemented");
            await Task.CompletedTask;
            yield break;
        }
    }
}