using Stream_Service.BackgroundServices;
using Stream_Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
});

// Add services to the container.
builder.Services.AddControllers();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FM Stream Service API",
        Version = "v1",
        Description = "Time-Shift Radio Streaming API with 1-Hour Buffer - Record live FM streams and allow users to rewind up to 1 hour",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "FM Stream Service",
            Email = "support@fmstream.com"
        }
    });
});

// Register services
builder.Services.AddSingleton<StreamBufferManager>();
builder.Services.AddSingleton<IFMStreamService, FMStreamService>();
builder.Services.AddHostedService<StreamProcessingService>();

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FM Stream Service API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "FM Stream Service - API Documentation";
    });
}
else
{
    app.UseHttpsRedirection();
}

// Enable static files (HTML, CSS, JS, images, etc.)
app.UseDefaultFiles(); // Serves index.html by default
app.UseStaticFiles();  // Serves files from wwwroot folder

app.UseCors("AllowAll");
app.MapControllers();

try
{
    Console.WriteLine("===========================================");
    Console.WriteLine("FM Stream Service is starting...");
    Console.WriteLine("===========================================");
    Console.WriteLine("Server URL: http://localhost:5000");
    Console.WriteLine("Web Player: http://localhost:5000");
    Console.WriteLine("Swagger UI: http://localhost:5000/swagger");
    Console.WriteLine("===========================================");
    Console.WriteLine("API Endpoints:");
    Console.WriteLine("  Live Stream: http://localhost:5000/api/stream/live/{stationId}");
    Console.WriteLine("  Rewind: http://localhost:5000/api/stream/rewind/{stationId}?seconds=X");
    Console.WriteLine("  Status: http://localhost:5000/api/stream/status/{stationId}");
    Console.WriteLine("  Health: http://localhost:5000/health");
    Console.WriteLine("===========================================");
    Console.WriteLine("Available Stations: mbc, lotus, vasanthamfm");
    Console.WriteLine("===========================================");

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    throw;
}