namespace Stream_Service.Services
{
    public interface IFMStreamService
    {
        IAsyncEnumerable<byte[]> GetLiveStreamAsync(string stationId);
        IAsyncEnumerable<byte[]> GetBufferedStreamAsync(string stationId, DateTime startTimestamp);
        Task UpdateStationAsync(string stationId, string url);
    }
}