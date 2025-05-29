using Stream_Service.Models;
using System.Collections.Concurrent;

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
                // Remove chunks older than 5 minutes
                buffers.RemoveAll(b => b.Timestamp < DateTime.UtcNow - _bufferDuration);
            }
        }

        public byte[] GetChunk(string stationId, DateTime timestamp)
        {
            if (_buffers.TryGetValue(stationId, out var buffers))
            {
                lock (buffers)
                {
                    // Find closest chunk within 1-second tolerance
                    return buffers.FirstOrDefault(b => Math.Abs((b.Timestamp - timestamp).TotalSeconds) < 1)?.AudioChunk;
                }
            }
            return null;
        }
    }
}
