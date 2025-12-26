using Microsoft.AspNetCore.Mvc;
using Stream_Service.Services;
using Stream_Service.Models;
using System;
using System.Threading.Tasks;

namespace Stream_Service.Controllers
{
    /// <summary>
    /// FM Stream Service API - Time-Shift Radio Streaming
    /// </summary>
    [Route("api/stream")]
    [ApiController]
    [Produces("application/json", "audio/mpeg")]
    public class StreamController : ControllerBase
    {
        private readonly IFMStreamService _streamService;
        private readonly StreamBufferManager _bufferManager;
        private readonly ILogger<StreamController> _logger;
        private readonly IConfiguration _configuration;

        public StreamController(IFMStreamService streamService, StreamBufferManager bufferManager, ILogger<StreamController> logger, IConfiguration configuration)
        {
            _streamService = streamService;
            _bufferManager = bufferManager;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Stream live audio for a station (near real-time with minimal delay)
        /// </summary>
        /// <param name="stationId">Station ID (mbc, lotus, or vasanthamfm)</param>
        /// <returns>Audio stream in MP3 format</returns>
        /// <response code="200">Returns the live audio stream</response>
        /// <response code="404">Station not found or no buffered data available</response>
        [HttpGet("live/{stationId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task GetLiveStream(string stationId)
        {
            _logger.LogInformation("Live stream request for station: {StationId} from {IP}", stationId, HttpContext.Connection.RemoteIpAddress);

            Response.ContentType = "audio/mpeg";
            Response.Headers.Append("Cache-Control", "no-cache, no-store");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("Accept-Ranges", "none");

            try
            {
                await foreach (var chunk in _streamService.GetLiveStreamAsync(stationId))
                {
                    await Response.Body.WriteAsync(chunk, 0, chunk.Length);
                    await Response.Body.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming live audio for {StationId}", stationId);
            }
        }

        /// <summary>
        /// Stream audio from X seconds ago (rewind/time-shift feature)
        /// </summary>
        /// <param name="stationId">Station ID (mbc, lotus, or vasanthamfm)</param>
        /// <param name="seconds">Number of seconds to rewind (0-3600, i.e., up to 1 hour)</param>
        /// <returns>Audio stream in MP3 format starting from the specified time</returns>
        /// <response code="200">Returns the rewound audio stream</response>
        /// <response code="400">Invalid seconds parameter (must be 0-3600)</response>
        /// <response code="404">No buffered data available for the requested time</response>
        [HttpGet("rewind/{stationId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task GetRewindStream(string stationId, [FromQuery] int seconds)
        {
            // Get station configuration to check its recording hours limit
            var stations = _configuration.GetSection("Stations").Get<List<Station>>() ?? new List<Station>();
            var station = stations.FirstOrDefault(s => s.Id == stationId);

            if (station == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync($"Station '{stationId}' not found");
                return;
            }

            // Calculate max allowed seconds based on station's recording hours
            var maxSeconds = station.RecordingHours * 3600;

            if (seconds < 0 || seconds > maxSeconds)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync($"Seconds must be between 0 and {maxSeconds} ({station.RecordingHours} hour(s))");
                _logger.LogWarning("Invalid rewind seconds {Seconds} for station {StationId}. Max allowed: {MaxSeconds}",
                    seconds, stationId, maxSeconds);
                return;
            }

            var startTimestamp = DateTime.UtcNow.AddSeconds(-seconds);
            _logger.LogInformation("Rewind stream request for station: {StationId}, going back {Seconds} seconds to {StartTime}",
                stationId, seconds, startTimestamp);

            Response.ContentType = "audio/mpeg";
            Response.Headers.Append("Cache-Control", "no-cache, no-store");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("Accept-Ranges", "none");

            try
            {
                await foreach (var chunk in _streamService.GetBufferedStreamAsync(stationId, startTimestamp))
                {
                    await Response.Body.WriteAsync(chunk, 0, chunk.Length);
                    await Response.Body.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming buffered audio for {StationId}", stationId);
            }
        }

        /// <summary>
        /// Get all available stations
        /// </summary>
        /// <returns>List of all configured stations with their details</returns>
        /// <response code="200">Returns the list of stations</response>
        [HttpGet("stations")]
        [ProducesResponseType(typeof(Station[]), StatusCodes.Status200OK)]
        public IActionResult GetStations()
        {
            var stations = _configuration.GetSection("Stations").Get<List<Station>>() ?? new List<Station>();
            return Ok(stations.Where(s => s.IsActive).Select(s => new
            {
                s.Id,
                s.Name,
                s.Icon,
                s.Gradient,
                s.RecordingHours
            }));
        }

        /// <summary>
        /// Get buffer status for a specific station
        /// </summary>
        /// <param name="stationId">Station ID (mbc, lotus, or vasanthamfm)</param>
        /// <returns>Buffer statistics including chunk count, total bytes, and buffer duration</returns>
        /// <response code="200">Returns the buffer status</response>
        [HttpGet("status/{stationId}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult GetBufferStatus(string stationId)
        {
            var status = _bufferManager.GetBufferStatus(stationId);

            return Ok(new
            {
                stationId,
                chunkCount = status.ChunkCount,
                totalBytes = status.TotalBytes,
                totalMB = Math.Round(status.TotalBytes / 1024.0 / 1024.0, 2),
                oldestChunkTime = status.OldestChunkTime,
                latestChunkTime = status.LatestChunkTime,
                bufferDuration = status.BufferDuration,
                bufferDurationMinutes = Math.Round(status.BufferDuration.TotalMinutes, 2),
                totalBytesReceived = status.TotalBytesReceived,
                totalMBReceived = Math.Round(status.TotalBytesReceived / 1024.0 / 1024.0, 2),
                totalChunksReceived = status.TotalChunksReceived,
                isRecording = status.ChunkCount > 0
            });
        }

        /// <summary>
        /// Get status for all configured stations
        /// </summary>
        /// <returns>Array of buffer status for all stations</returns>
        /// <response code="200">Returns the status of all stations</response>
        [HttpGet("status")]
        [ProducesResponseType(typeof(object[]), StatusCodes.Status200OK)]
        public IActionResult GetAllStatus()
        {
            var stationIds = _bufferManager.GetAllStationIds();
            var allStatus = stationIds.Select(id => new
            {
                stationId = id,
                status = _bufferManager.GetBufferStatus(id)
            }).ToList();

            return Ok(allStatus.Select(s => new
            {
                s.stationId,
                chunkCount = s.status.ChunkCount,
                totalMB = Math.Round(s.status.TotalBytes / 1024.0 / 1024.0, 2),
                bufferDurationMinutes = Math.Round(s.status.BufferDuration.TotalMinutes, 2),
                oldestChunkTime = s.status.OldestChunkTime,
                latestChunkTime = s.status.LatestChunkTime,
                isRecording = s.status.ChunkCount > 0
            }));
        }

        /// <summary>
        /// Health check endpoint - Verify if the service is running
        /// </summary>
        /// <returns>Service health status and timestamp</returns>
        /// <response code="200">Service is healthy and running</response>
        [HttpGet("/health")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "FM Stream Service"
            });
        }
    }
}