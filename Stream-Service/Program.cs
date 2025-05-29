using Stream_Service.BackgroundServices;
using Stream_Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<StreamBufferManager>();
builder.Services.AddScoped<IFMStreamService, FMStreamService>();
builder.Services.AddHostedService<StreamProcessingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
