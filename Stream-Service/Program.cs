using Stream_Service.BackgroundServices;
using Stream_Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");
app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    throw;
}