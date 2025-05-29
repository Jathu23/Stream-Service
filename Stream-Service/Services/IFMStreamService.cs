namespace Stream_Service.Services
{
    public interface IFMStreamService
    {
        IAsyncEnumerable<byte[]> GetLiveStreamAsync(string stationId);
        Task<byte[]> GetBufferedChunkAsync(string stationId, DateTime timestamp);
        Task UpdateStationAsync(string stationId, string url);
    }
}
