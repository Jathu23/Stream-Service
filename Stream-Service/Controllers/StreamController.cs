using Microsoft.AspNetCore.Mvc;
using Stream_Service.Services;

namespace Stream_Service.Controllers
{
    [Route("api/stream")]
    public class StreamController : ControllerBase
    {
        private readonly IFMStreamService _streamService;

        public StreamController(IFMStreamService streamService)
        {
            _streamService = streamService;
        }

        [HttpGet("live/{stationId}")]
        public async Task GetLiveStream(string stationId)
        {
            Response.ContentType = "audio/mpeg";
            Response.Headers.Add("Cache-Control", "no-cache");
            await foreach (var chunk in _streamService.GetLiveStreamAsync(stationId))
            {
                await Response.Body.WriteAsync(chunk, 0, chunk.Length);
                await Response.Body.FlushAsync();
            }
        }

        [HttpGet("rewind/{stationId}")]
        public async Task GetRewindStream(string stationId, [FromQuery] int seconds)
        {
            var startTimestamp = DateTime.UtcNow.AddSeconds(-seconds);
            Response.ContentType = "audio/mpeg";
            Response.Headers.Add("Cache-Control", "no-cache");
            await foreach (var chunk in _streamService.GetBufferedStreamAsync(stationId, startTimestamp))
            {
                await Response.Body.WriteAsync(chunk, 0, chunk.Length);
                await Response.Body.FlushAsync();
            }
        }

        [HttpPost("update")]
        public async Task<IActionResult> UpdateStation([FromBody] Station model)
        {
            await _streamService.UpdateStationAsync(model.Id, model.Url);
            return Ok();
        }
    }

    public class Station
    {
        public string Id { get; set; }
        public string Url { get; set; }
    }
}