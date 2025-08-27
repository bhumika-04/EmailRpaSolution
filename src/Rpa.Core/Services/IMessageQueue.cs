using Rpa.Core.Models;

namespace Rpa.Core.Services;

public interface IMessageQueue
{
    Task PublishAsync<T>(T message, string queueName, CancellationToken cancellationToken = default) where T : class;
    Task<T?> ConsumeAsync<T>(string queueName, CancellationToken cancellationToken = default) where T : class;
    Task StartConsumingAsync<T>(string queueName, Func<T, Task> handler, CancellationToken cancellationToken = default) where T : class;
    Task AcknowledgeAsync(string deliveryTag);
    Task RejectAsync(string deliveryTag, bool requeue = false);
}

public interface IEmailClassifier
{
    Task<EmailClassificationResult> ClassifyAsync(EmailMessage emailMessage);
}

public interface IDataExtractor
{
    Task<ExtractedCredentials?> ExtractCredentialsAsync(string emailBody);
    Task<JobCardInfo?> ExtractJobCardInfoAsync(string emailBody);
    Task<Dictionary<string, object>> ExtractStructuredDataAsync(string emailBody, string classificationType);
}

public class EmailClassificationResult
{
    public string Classification { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public static class QueueNames
{
    public const string EmailProcessing = "email-processing";
    public const string JobProcessing = "job-processing";
    public const string Notifications = "notifications";
    public const string DeadLetter = "dead-letter";
}