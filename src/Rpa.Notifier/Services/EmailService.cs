using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Rpa.Notifier.Services;

public interface IEmailService
{
    Task SendNotificationAsync(string recipientEmail, string subject, string htmlBody, string textBody);
    Task SendNotificationWithScreenshotAsync(string recipientEmail, string subject, string htmlBody, string textBody, byte[] screenshot, string screenshotFileName);
}

public interface ITemplateService
{
    Task<EmailContent> GenerateEmailContentAsync(Core.Models.Job job, Core.Models.ProcessingResult result);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly IConfiguration _configuration;

    public EmailService(ILogger<EmailService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SendNotificationAsync(string recipientEmail, string subject, string htmlBody, string textBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _configuration["Email:Smtp:FromName"] ?? "RPA Automation System",
                _configuration["Email:Smtp:FromEmail"] ?? "noreply@company.com"
            ));
            message.To.Add(new MailboxAddress("", recipientEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                TextBody = textBody,
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            var host = _configuration["Email:Smtp:Host"] ?? "localhost";
            var port = _configuration.GetValue<int>("Email:Smtp:Port", 587);
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var useSsl = _configuration.GetValue<bool>("Email:Smtp:UseSsl", true);

            await client.ConnectAsync(host, port, useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {recipient} with subject: {subject}", 
                recipientEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {recipient}", recipientEmail);
            throw;
        }
    }

    public async Task SendNotificationWithScreenshotAsync(string recipientEmail, string subject, string htmlBody, string textBody, byte[] screenshot, string screenshotFileName)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _configuration["Email:Smtp:FromName"] ?? "RPA Automation System",
                _configuration["Email:Smtp:FromEmail"] ?? "noreply@company.com"
            ));
            message.To.Add(new MailboxAddress("", recipientEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                TextBody = textBody,
                HtmlBody = htmlBody
            };

            // Add screenshot as attachment
            if (screenshot != null && screenshot.Length > 0)
            {
                bodyBuilder.Attachments.Add(screenshotFileName, screenshot, new ContentType("image", "png"));
            }

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            var host = _configuration["Email:Smtp:Host"] ?? "localhost";
            var port = _configuration.GetValue<int>("Email:Smtp:Port", 587);
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var useSsl = _configuration.GetValue<bool>("Email:Smtp:UseSsl", true);

            await client.ConnectAsync(host, port, useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email with screenshot sent successfully to {recipient} with subject: {subject}", 
                recipientEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email with screenshot to {recipient}", recipientEmail);
            throw;
        }
    }
}

public class TemplateService : ITemplateService
{
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(ILogger<TemplateService> logger)
    {
        _logger = logger;
    }

    public async Task<EmailContent> GenerateEmailContentAsync(Core.Models.Job job, Core.Models.ProcessingResult result)
    {
        var statusColor = result.Success ? "green" : "red";
        var statusIcon = result.Success ? "✅" : "❌";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; background-color: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .header {{ text-align: center; margin-bottom: 30px; padding: 20px; background-color: #f8f9fa; border-radius: 5px; }}
        .status {{ display: inline-block; padding: 10px 20px; border-radius: 20px; color: white; font-weight: bold; background-color: {statusColor}; }}
        .details {{ margin: 20px 0; }}
        .detail-row {{ margin: 10px 0; padding: 10px; background-color: #f8f9fa; border-left: 4px solid #007bff; }}
        .detail-label {{ font-weight: bold; color: #333; }}
        .detail-value {{ color: #666; margin-left: 10px; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #eee; text-align: center; color: #888; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>RPA Automation Report {statusIcon}</h1>
            <div class='status'>Status: {job.Status}</div>
        </div>
        
        <div class='details'>
            <div class='detail-row'>
                <span class='detail-label'>Job ID:</span>
                <span class='detail-value'>{job.Id}</span>
            </div>
            <div class='detail-row'>
                <span class='detail-label'>Job Type:</span>
                <span class='detail-value'>{job.JobType ?? "General Automation"}</span>
            </div>
            <div class='detail-row'>
                <span class='detail-label'>Original Subject:</span>
                <span class='detail-value'>{job.EmailSubject}</span>
            </div>
            <div class='detail-row'>
                <span class='detail-label'>Processing Time:</span>
                <span class='detail-value'>{(job.CompletedAt - job.StartedAt)?.TotalMinutes:F1} minutes</span>
            </div>
            <div class='detail-row'>
                <span class='detail-label'>Result Message:</span>
                <span class='detail-value'>{result.Message}</span>
            </div>
        </div>

        {(result.Errors?.Any() == true ? $@"
        <div class='details'>
            <h3 style='color: red;'>Errors:</h3>
            {string.Join("", result.Errors.Select(error => $"<div class='detail-row' style='border-left-color: red;'>{error}</div>"))}
        </div>" : "")}

        {(result.Data?.Any() == true ? $@"
        <div class='details'>
            <h3 style='color: green;'>Processing Data:</h3>
            {string.Join("", result.Data.Select(kvp => $"<div class='detail-row'><span class='detail-label'>{kvp.Key}:</span><span class='detail-value'>{kvp.Value}</span></div>"))}
        </div>" : "")}

        <div class='footer'>
            <p>This is an automated message from the RPA Automation System</p>
            <p>Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
        </div>
    </div>
</body>
</html>";

        var textBody = $@"
RPA Automation Report {statusIcon}

Job Details:
- Job ID: {job.Id}
- Job Type: {job.JobType ?? "General Automation"}
- Status: {job.Status}
- Original Subject: {job.EmailSubject}
- Processing Time: {(job.CompletedAt - job.StartedAt)?.TotalMinutes:F1} minutes
- Result: {result.Message}

{(result.Errors?.Any() == true ? $@"
Errors:
{string.Join(Environment.NewLine, result.Errors.Select(error => $"- {error}"))}" : "")}

{(result.Data?.Any() == true ? $@"
Processing Data:
{string.Join(Environment.NewLine, result.Data.Select(kvp => $"- {kvp.Key}: {kvp.Value}"))}" : "")}

---
This is an automated message from the RPA Automation System
Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
";

        return await Task.FromResult(new EmailContent
        {
            HtmlBody = htmlBody,
            TextBody = textBody
        });
    }
}

public class EmailContent
{
    public string HtmlBody { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
}