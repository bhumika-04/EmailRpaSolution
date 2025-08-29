using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rpa.Core.Data;
using Serilog;

namespace Rpa.Core.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRpaCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RpaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        
        services.AddSingleton<IMessageQueue, RabbitMqService>();
        
        return services;
    }

    public static IServiceCollection AddSerilogLogging(this IServiceCollection services, IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console()
            .WriteTo.File(
                path: configuration["Logging:FilePath"] ?? "logs/rpa-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: int.Parse(configuration["Logging:RetainedFileCountLimit"] ?? "30")
            )
            .CreateLogger();

        services.AddSerilog();
        return services;
    }
}