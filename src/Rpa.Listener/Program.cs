using Rpa.Core.Data;
using Rpa.Core.Services;
using Rpa.Listener;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/listener-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Rpa.Listener service");

    var builder = Host.CreateApplicationBuilder(args);
    
    builder.Services.AddHostedService<Worker>();
    
    builder.Services.ConfigureDatabase(builder.Configuration);
    
    builder.Services.AddSingleton<IMessageQueue, RabbitMqService>();
    builder.Services.AddScoped<IEmailClassifier, EmailClassifierService>();
    builder.Services.AddScoped<IDataExtractor, DataExtractorService>();
    
    builder.Services.AddSerilog();

    var host = builder.Build();
    
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Rpa.Listener service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}