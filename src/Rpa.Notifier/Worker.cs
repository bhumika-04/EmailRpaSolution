using Microsoft.EntityFrameworkCore;
using Rpa.Core.Data;
using Rpa.Core.Models;
using Rpa.Core.Services;
using Rpa.Notifier.Services;

namespace Rpa.Notifier;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageQueue _messageQueue;

    public Worker(
        ILogger<Worker> logger,
        IServiceProvider serviceProvider,
        IMessageQueue messageQueue)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _messageQueue = messageQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Notifier Worker started at: {time}", DateTimeOffset.Now);

        await _messageQueue.StartConsumingAsync<JobNotification>(
            QueueNames.Notifications,
            ProcessNotificationAsync,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessNotificationAsync(JobNotification notification)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>();

        try
        {
            var job = await dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == notification.JobId);
            if (job == null)
            {
                _logger.LogError("Job not found for notification: {jobId}", notification.JobId);
                return;
            }

            _logger.LogInformation("Processing notification for job {jobId}, status: {status}", 
                notification.JobId, notification.Status);

            var emailContent = await templateService.GenerateEmailContentAsync(job, notification.Result);
            
            // Check if there's a screenshot in the result data
            if (notification.Result.Data?.ContainsKey("screenshot") == true && 
                notification.Result.Data["screenshot"] is string screenshotBase64)
            {
                var screenshot = Convert.FromBase64String(screenshotBase64);
                var screenshotFileName = $"ERP_Costing_Results_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                
                await emailService.SendNotificationWithScreenshotAsync(
                    recipientEmail: notification.RecipientEmail,
                    subject: $"ERP Costing Complete - {job.EmailSubject} - Job: {job.Id}",
                    htmlBody: emailContent.HtmlBody,
                    textBody: emailContent.TextBody,
                    screenshot: screenshot,
                    screenshotFileName: screenshotFileName
                );
            }
            else
            {
                await emailService.SendNotificationAsync(
                    recipientEmail: notification.RecipientEmail,
                    subject: $"Automation Job {notification.Status}: {job.EmailSubject}",
                    htmlBody: emailContent.HtmlBody,
                    textBody: emailContent.TextBody
                );
            }

            _logger.LogInformation("Notification sent successfully for job {jobId} to {email}", 
                notification.JobId, notification.RecipientEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification for job {jobId}", notification.JobId);
            throw;
        }
    }
}