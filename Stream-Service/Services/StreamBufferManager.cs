using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Stream_Service.Services
{
    public class StreamBufferManager
    {
        private readonly ConcurrentDictionary<string, CircularAudioBuffer> _buffers = new();
        private readonly TimeSpan _bufferDuration = TimeSpan.FromHours(1);
        private readonly ILogger<StreamBufferManager>? _logger;

        public StreamBufferManager(ILogger<StreamBufferManager>? logger = null)
        {
            _logger = logger;
            InitializeBuffers();
        }

        private void InitializeBuffers()
        {
            var stations = new[] { "mbc", "lotus", "vasanthamfm" };
            foreach (var stationId in stations)
            {
                _buffers.TryAdd(stationId, new CircularAudioBuffer(_bufferDuration, _logger));
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

                // Remove old chunks beyond 1 hour
                // IMPORTANT: Calculate cutoff time BEFORE adding the new chunk's timestamp
                // to avoid race conditions where we might remove chunks we just added
                var cutoffTime = chunk.Timestamp - _maxDuration;
                int removedCount = 0;

                while (_chunks.Count > 1 && _chunks.First!.Value.Timestamp < cutoffTime)
                {
                    _chunks.RemoveFirst();
                    removedCount++;
                }

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
                    TotalBytes = _chunks.Sum(c => c.Data.Length),
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