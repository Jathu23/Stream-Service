namespace Stream_Service.Services
{
    public interface IFMStreamService
    {
        IAsyncEnumerable<byte[]> GetLiveStreamAsync(string stationId, CancellationToken cancellationToken = default);
        IAsyncEnumerable<byte[]> GetBufferedStreamAsync(string stationId, DateTime startTimestamp, CancellationToken cancellationToken = default);
        Task UpdateStationAsync(string stationId, string url);
        IAsyncEnumerable<byte[]> FetchStreamAsync(string url, string stationId);
    }
}