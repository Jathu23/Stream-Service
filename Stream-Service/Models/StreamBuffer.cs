namespace Stream_Service.Models
{
    public class StreamBuffer
    {
        public string StationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public byte[] AudioChunk { get; set; } = Array.Empty<byte>();
    }
}
