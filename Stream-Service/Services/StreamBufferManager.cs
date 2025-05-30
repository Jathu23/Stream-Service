using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Stream_Service.Services
{
    public class StreamBufferManager
    {
        private readonly ConcurrentDictionary<string, List<StreamBuffer>> _buffers = new();
        private readonly TimeSpan _bufferDuration = TimeSpan.FromMinutes(5);

        public void AddChunk(string stationId, byte[] chunk, DateTime timestamp)
        {
            var buffers = _buffers.GetOrAdd(stationId, new List<StreamBuffer>());
            lock (buffers)
            {
                buffers.Add(new StreamBuffer { StationId = stationId, Timestamp = timestamp, AudioChunk = chunk });
                buffers.RemoveAll(b => b.Timestamp < DateTime.UtcNow - _bufferDuration);
                Console.WriteLine($"Added chunk for {stationId} at {timestamp}, total chunks: {buffers.Count}");
            }
        }

        public byte[] GetChunk(string stationId, DateTime timestamp)
        {
            if (_buffers.TryGetValue(stationId, out var buffers))
            {
                lock (buffers)
                {
                    var chunk = buffers.FirstOrDefault(b => Math.Abs((b.Timestamp - timestamp).TotalSeconds) < 1);
                    return chunk?.AudioChunk;
                }
            }
            return null;
        }

        public List<StreamBuffer> GetChunksFromTimestamp(string stationId, DateTime startTimestamp)
        {
            if (_buffers.TryGetValue(stationId, out var buffers))
            {
                lock (buffers)
                {
                    return buffers
                        .Where(b => b.Timestamp >= startTimestamp)
                        .OrderBy(b => b.Timestamp)
                        .ToList();
                }
            }
            return null;
        }
    }

    public class StreamBuffer
    {
        public string StationId { get; set; }
        public DateTime Timestamp { get; set; }
        public byte[] AudioChunk { get; set; }
    }
}