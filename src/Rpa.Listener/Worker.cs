using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Rpa.Core.Data;
using Rpa.Core.Models;
using Rpa.Core.Services;
using System.Text.RegularExpressions;

namespace Rpa.Listener;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageQueue _messageQueue;
    private readonly TimeSpan _pollingInterval;

    public Worker(
        ILogger<Worker> logger, 
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IMessageQueue messageQueue)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _messageQueue = messageQueue;
        _pollingInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("Email:PollingIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Listener Worker started at: {time}", DateTimeOffset.Now);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing emails");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task ProcessEmailsAsync(CancellationToken cancellationToken)
    {
        using var client = new ImapClient();
        
        try
        {
            var host = _configuration["Email:Imap:Host"] ?? throw new InvalidOperationException("IMAP host not configured");
            var port = _configuration.GetValue<int>("Email:Imap:Port", 993);
            var username = _configuration["Email:Imap:Username"] ?? throw new InvalidOperationException("IMAP username not configured");
            var password = _configuration["Email:Imap:Password"] ?? throw new InvalidOperationException("IMAP password not configured");
            
            await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect, cancellationToken);
            await client.AuthenticateAsync(username, password, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var query = MailKit.Search.SearchQuery.NotSeen;
            var uids = await inbox.SearchAsync(query, cancellationToken);

            _logger.LogInformation("Found {count} unread emails", uids.Count);

            foreach (var uid in uids)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var message = await inbox.GetMessageAsync(uid, cancellationToken);
                    
                    if (await ShouldProcessEmail(message))
                    {
                        await ProcessSingleEmailAsync(message, cancellationToken);
                        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
                        _logger.LogInformation("Processed email: {subject}", message.Subject);
                    }
                    else
                    {
                        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
                        _logger.LogDebug("Skipped email (doesn't match criteria): {subject}", message.Subject);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email with UID: {uid}", uid);
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to IMAP server");
        }
    }

    private async Task<bool> ShouldProcessEmail(MimeMessage message)
    {
        var allowedSenders = _configuration.GetSection("Email:AllowedSenders").Get<string[]>() ?? Array.Empty<string>();
        var requiredSubjectPatterns = _configuration.GetSection("Email:SubjectPatterns").Get<string[]>() ?? Array.Empty<string>();

        if (allowedSenders.Any())
        {
            var sender = message.From.OfType<MailboxAddress>().FirstOrDefault()?.Address?.ToLowerInvariant();
            if (sender == null || !allowedSenders.Any(s => sender.Contains(s.ToLowerInvariant())))
            {
                return false;
            }
        }

        if (requiredSubjectPatterns.Any())
        {
            var subject = message.Subject ?? string.Empty;
            if (!requiredSubjectPatterns.Any(pattern => Regex.IsMatch(subject, pattern, RegexOptions.IgnoreCase)))
            {
                return false;
            }
        }

        return await Task.FromResult(true);
    }

    private async Task ProcessSingleEmailAsync(MimeMessage mimeMessage, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        var classifier = scope.ServiceProvider.GetRequiredService<IEmailClassifier>();
        var extractor = scope.ServiceProvider.GetRequiredService<IDataExtractor>();

        var emailMessage = new EmailMessage
        {
            JobId = Guid.NewGuid(),
            Subject = mimeMessage.Subject ?? string.Empty,
            From = mimeMessage.From.OfType<MailboxAddress>().FirstOrDefault()?.Address ?? string.Empty,
            To = string.Join(", ", mimeMessage.To.OfType<MailboxAddress>().Select(x => x.Address)),
            Body = mimeMessage.TextBody ?? mimeMessage.HtmlBody ?? string.Empty,
            ReceivedAt = mimeMessage.Date.DateTime,
            Priority = ExtractPriorityFromSubject(mimeMessage.Subject ?? string.Empty)
        };

        try
        {
            var classificationResult = await classifier.ClassifyAsync(emailMessage);
            emailMessage.Classification = classificationResult.Classification;
            emailMessage.ConfidenceScore = classificationResult.Confidence;

            var credentials = await extractor.ExtractCredentialsAsync(emailMessage.Body);
            var jobCardInfo = await extractor.ExtractJobCardInfoAsync(emailMessage.Body);
            
            // Extract structured data based on classification
            var structuredData = await extractor.ExtractStructuredDataAsync(emailMessage.Body, emailMessage.Classification);

            var job = new Job
            {
                Id = emailMessage.JobId,
                EmailSubject = emailMessage.Subject,
                SenderEmail = emailMessage.From,
                EmailBody = emailMessage.Body,
                Status = JobStatus.Pending,
                JobType = emailMessage.Classification,
                ExtractedCredentials = credentials != null ? System.Text.Json.JsonSerializer.Serialize(credentials) : null,
                JobCardDetails = jobCardInfo != null ? System.Text.Json.JsonSerializer.Serialize(jobCardInfo) : null,
                Metadata = structuredData.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(structuredData) : null,
                Priority = emailMessage.Priority,
                CreatedAt = DateTime.UtcNow
            };

            await dbContext.Jobs.AddAsync(job, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await _messageQueue.PublishAsync(emailMessage, QueueNames.JobProcessing, cancellationToken);
            
            _logger.LogInformation("Email processed and queued: JobId={jobId}, Classification={classification}", 
                emailMessage.JobId, emailMessage.Classification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing email message: {subject}", emailMessage.Subject);
            throw;
        }
    }

    private static int ExtractPriorityFromSubject(string subject)
    {
        if (subject.ToUpperInvariant().Contains("URGENT") || subject.ToUpperInvariant().Contains("HIGH PRIORITY"))
            return 1;
        if (subject.ToUpperInvariant().Contains("LOW PRIORITY"))
            return 9;
        return 5;
    }
}