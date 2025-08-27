using Rpa.Core.Data;
using Rpa.Core.Services;
using Rpa.Worker;
using Rpa.Worker.Automation;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Rpa.Worker service");

    var builder = Host.CreateApplicationBuilder(args);
    
    builder.Services.AddHostedService<Worker>();
    
    builder.Services.ConfigureDatabase(builder.Configuration);
    
    builder.Services.AddSingleton<IMessageQueue, RabbitMqService>();
    builder.Services.AddScoped<IWebsiteProcessor, WebsiteProcessor>();
    builder.Services.AddScoped<IErpEstimationProcessor, ErpEstimationProcessor>();
    builder.Services.AddScoped<IAnomalyDetector, AnomalyDetectorService>();
    
    builder.Services.AddSerilog();

    var host = builder.Build();
    
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Rpa.Worker service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}