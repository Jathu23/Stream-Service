using Stream_Service.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

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

        public async IAsyncEnumerable<byte[]> GetBufferedStreamAsync(string stationId, DateTime startTimestamp)
        {
            var chunks = _bufferManager.GetChunksFromTimestamp(stationId, startTimestamp);
            if (chunks == null || !chunks.Any())
            {
                _logger.LogWarning("No chunks found for station {StationId} starting at {StartTimestamp}", stationId, startTimestamp);
                yield break;
            }

            foreach (var chunk in chunks)
            {
                yield return chunk.AudioChunk;
                // Adjust delay to approximate playback speed (128kbps MP3, 64KB chunk = ~4 seconds of audio)
                await Task.Delay(4000); // 4 seconds per 64KB chunk at 128kbps
            }
        }

        public async Task UpdateStationAsync(string stationId, string url)
        {
            _logger.LogInformation("Updated station {StationId} with URL {Url}", stationId, url);
            await Task.CompletedTask;
        }

        private async IAsyncEnumerable<byte[]> FetchStreamAsync(string url, string stationId)
        {
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait // Changed to Wait to avoid dropping chunks
            });

            _ = StreamFromUrlAsync(url, stationId, 3, channel.Writer);

            await foreach (var chunk in channel.Reader.ReadAllAsync())
            {
                yield return chunk;
            }
        }

        private async Task StreamFromUrlAsync(string url, string stationId, int maxRetries, ChannelWriter<byte[]> writer)
        {
            try
            {
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        using var client = new HttpClient();
                        using var stream = await client.GetStreamAsync(url);
                        using var mp3Reader = new Mp3FileReader(stream); // Reintroduced Mp3FileReader for proper MP3 handling

                        var buffer = new byte[1024 * 64]; // 64KB chunks
                        int bytesRead;

                        while ((bytesRead = await mp3Reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            var chunk = new byte[bytesRead];
                            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                            await writer.WriteAsync(chunk);
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retry == maxRetries - 1) throw;
                        _logger.LogError(ex, "Error fetching stream for {StationId}, retry {Retry}", stationId, retry + 1);
                        await Task.Delay(1000);
                    }
                }
                writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch stream for {StationId} after {MaxRetries} retries", stationId, maxRetries);
                writer.Complete(ex);
            }
        }
    }
}