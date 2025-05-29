namespace Stream_Service.Models
{
    public class StreamBuffer
    {
        public string StationId { get; set; }
        public DateTime Timestamp { get; set; }
        public byte[] AudioChunk { get; set; }
    }
}
