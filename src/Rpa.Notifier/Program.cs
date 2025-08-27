using Rpa.Core.Data;
using Rpa.Core.Services;
using Rpa.Notifier;
using Rpa.Notifier.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/notifier-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Rpa.Notifier service");

    var builder = Host.CreateApplicationBuilder(args);
    
    builder.Services.AddHostedService<Worker>();
    
    builder.Services.ConfigureDatabase(builder.Configuration);
    
    builder.Services.AddSingleton<IMessageQueue, RabbitMqService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<ITemplateService, TemplateService>();
    
    builder.Services.AddSerilog();

    var host = builder.Build();
    
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Rpa.Notifier service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}