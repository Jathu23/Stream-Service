using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Stream_Service.Services
{
    public class StreamBufferManager
    {
        private readonly ConcurrentDictionary<string, CircularAudioBuffer> _buffers = new();
        private readonly ILogger<StreamBufferManager>? _logger;
        private readonly IConfiguration _configuration;

        public StreamBufferManager(IConfiguration configuration, ILogger<StreamBufferManager>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            InitializeBuffers();
        }

        private void InitializeBuffers()
        {
            // Load stations from configuration to get their specific recording hours
            var stations = _configuration.GetSection("Stations").Get<List<Models.Station>>() ?? new List<Models.Station>();

            foreach (var station in stations.Where(s => s.IsActive))
            {
                // Use station-specific recording hours
                var bufferDuration = TimeSpan.FromHours(station.RecordingHours);
                _buffers.TryAdd(station.Id, new CircularAudioBuffer(bufferDuration, _logger));

                _logger?.LogInformation("Initialized buffer for station {StationId} with {Hours} hour(s) capacity",
                    station.Id, station.RecordingHours);
            }
        }

        public void AddChunk(string stationId, byte[] audioData)
        {
            if (_buffers.TryGetValue(stationId, out var buffer))
            {
                buffer.AddChunk(audioData);
            }
        }

        public List<AudioChunk> GetAllChunks(string stationId)
        {
            if (_buffers.TryGetValue(stationId, out var buffer))
            {
                return buffer.GetAllChunks();
            }
            return new List<AudioChunk>();
        }

        public List<AudioChunk> GetChunksFrom(string stationId, DateTime startTime)
        {
            if (_buffers.TryGetValue(stationId, out var buffer))
            {
                return buffer.GetChunksFrom(startTime);
            }
            return new List<AudioChunk>();
        }

        public BufferStatus GetBufferStatus(string stationId)
        {
            if (_buffers.TryGetValue(stationId, out var buffer))
            {
                return buffer.GetStatus();
            }
            return new BufferStatus();
        }

        public List<string> GetAllStationIds()
        {
            return _buffers.Keys.ToList();
        }
    }

    public class CircularAudioBuffer
    {
        private readonly LinkedList<AudioChunk> _chunks = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly TimeSpan _maxDuration;
        private readonly ILogger? _logger;
        private long _totalBytesAdded = 0;
        private int _totalChunksAdded = 0;
        private long _currentBufferBytes = 0; // ✅ Cache to avoid Sum() on every status call

        public CircularAudioBuffer(TimeSpan maxDuration, ILogger? logger = null)
        {
            _maxDuration = maxDuration;
            _logger = logger;
        }

        public void AddChunk(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            var chunk = new AudioChunk
            {
                Timestamp = DateTime.UtcNow,
                Data = data
            };

            _lock.EnterWriteLock();
            try
            {
                _chunks.AddLast(chunk);
                _totalBytesAdded += data.Length;
                _totalChunksAdded++;
                _currentBufferBytes += data.Length; // ✅ Update cache

                // Remove old chunks beyond station's max duration
                // IMPORTANT: Calculate cutoff time BEFORE adding the new chunk's timestamp
                // to avoid race conditions where we might remove chunks we just added
                var cutoffTime = chunk.Timestamp - _maxDuration;
                int removedCount = 0;
                long removedBytes = 0;

                while (_chunks.Count > 1 && _chunks.First!.Value.Timestamp < cutoffTime)
                {
                    var removedChunk = _chunks.First.Value;
                    removedBytes += removedChunk.Data.Length;
                    _chunks.RemoveFirst();
                    removedCount++;
                }

                _currentBufferBytes -= removedBytes; // ✅ Update cache

                if (removedCount > 0)
                {
                    _logger?.LogDebug("Added chunk: {Size} bytes. Removed {Removed} old chunks. Total chunks: {Count}",
                        data.Length, removedCount, _chunks.Count);
                }
                else if (_totalChunksAdded % 100 == 0)
                {
                    _logger?.LogDebug("Added chunk: {Size} bytes. Total chunks: {Count}", data.Length, _chunks.Count);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public List<AudioChunk> GetAllChunks()
        {
            _lock.EnterReadLock();
            try
            {
                return _chunks.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<AudioChunk> GetChunksFrom(DateTime startTime)
        {
            _lock.EnterReadLock();
            try
            {
                return _chunks.Where(c => c.Timestamp >= startTime).ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public BufferStatus GetStatus()
        {
            _lock.EnterReadLock();
            try
            {
                var firstChunk = _chunks.FirstOrDefault();
                var lastChunk = _chunks.LastOrDefault();

                // Calculate buffer duration
                TimeSpan bufferDuration = TimeSpan.Zero;
                if (lastChunk != null && firstChunk != null)
                {
                    // Calculate actual duration between oldest and newest chunk
                    bufferDuration = lastChunk.Timestamp - firstChunk.Timestamp;

                    // Cap at max duration to show consistent value when buffer is full
                    // This prevents showing 0 or very small values due to timing issues
                    if (_chunks.Count > 10) // If we have reasonable number of chunks
                    {
                        // Buffer is likely full or filling up
                        // Show the lesser of actual duration or max duration
                        if (bufferDuration > _maxDuration)
                        {
                            bufferDuration = _maxDuration;
                        }
                    }
                }

                return new BufferStatus
                {
                    ChunkCount = _chunks.Count,
                    TotalBytes = _currentBufferBytes, // ✅ Use cached value instead of Sum()
                    OldestChunkTime = firstChunk?.Timestamp,
                    LatestChunkTime = lastChunk?.Timestamp,
                    BufferDuration = bufferDuration,
                    TotalBytesReceived = _totalBytesAdded,
                    TotalChunksReceived = _totalChunksAdded
                };
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public class AudioChunk
    {
        public DateTime Timestamp { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class BufferStatus
    {
        public int ChunkCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTime? OldestChunkTime { get; set; }
        public DateTime? LatestChunkTime { get; set; }
        public TimeSpan BufferDuration { get; set; }
        public long TotalBytesReceived { get; set; }
        public int TotalChunksReceived { get; set; }
    }
}