using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Rpa.Core.Services;

public class RabbitMqService : IMessageQueue, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RabbitMqService(IConfiguration configuration, ILogger<RabbitMqService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
            Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = configuration["RabbitMQ:UserName"] ?? "guest",
            Password = configuration["RabbitMQ:Password"] ?? "guest",
            VirtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        DeclareQueues();
    }

    private void DeclareQueues()
    {
        var queues = new[]
        {
            QueueNames.EmailProcessing,
            QueueNames.JobProcessing,
            QueueNames.Notifications,
            QueueNames.DeadLetter
        };

        foreach (var queue in queues)
        {
            _channel.QueueDeclare(
                queue: queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", "" },
                    { "x-dead-letter-routing-key", QueueNames.DeadLetter }
                });
        }

        _logger.LogInformation("RabbitMQ queues declared successfully");
    }

    public async Task PublishAsync<T>(T message, string queueName, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = Guid.NewGuid().ToString();
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            _channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body);

            _logger.LogDebug("Message published to queue {QueueName}: {MessageId}", queueName, properties.MessageId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task<T?> ConsumeAsync<T>(string queueName, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var result = _channel.BasicGet(queueName, autoAck: false);
            if (result == null)
                return null;

            var json = Encoding.UTF8.GetString(result.Body.ToArray());
            var message = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to consume message from queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task StartConsumingAsync<T>(string queueName, Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        var consumer = new EventingBasicConsumer(_channel);
        
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var message = JsonSerializer.Deserialize<T>(json, _jsonOptions);

                if (message != null)
                {
                    await handler(message);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogDebug("Message processed successfully from queue {QueueName}", queueName);
                }
                else
                {
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                    _logger.LogWarning("Failed to deserialize message from queue {QueueName}", queueName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from queue {QueueName}", queueName);
                _channel.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        _logger.LogInformation("Started consuming from queue {QueueName}", queueName);

        await Task.CompletedTask;
    }

    public async Task AcknowledgeAsync(string deliveryTag)
    {
        if (ulong.TryParse(deliveryTag, out var tag))
        {
            _channel.BasicAck(tag, false);
        }
        await Task.CompletedTask;
    }

    public async Task RejectAsync(string deliveryTag, bool requeue = false)
    {
        if (ulong.TryParse(deliveryTag, out var tag))
        {
            _channel.BasicNack(tag, false, requeue);
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();
    }
}