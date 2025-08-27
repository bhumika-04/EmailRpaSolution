using Microsoft.EntityFrameworkCore;
using Rpa.Core.Data;
using Rpa.Core.Models;
using Rpa.Core.Services;
using Rpa.Worker.Automation;

namespace Rpa.Worker;

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
        _logger.LogInformation("RPA Worker started at: {time}", DateTimeOffset.Now);

        await _messageQueue.StartConsumingAsync<EmailMessage>(
            QueueNames.JobProcessing,
            ProcessJobAsync,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(EmailMessage emailMessage)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        var websiteProcessor = scope.ServiceProvider.GetRequiredService<IWebsiteProcessor>();
        var anomalyDetector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        var job = await dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == emailMessage.JobId);
        if (job == null)
        {
            _logger.LogError("Job not found: {jobId}", emailMessage.JobId);
            return;
        }

        try
        {
            job.Status = JobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Processing job {jobId} of type {jobType}", job.Id, job.JobType);

            ProcessingResult result;

            switch (job.JobType?.ToLowerInvariant())
            {
                case "erp-estimation-workflow":
                    result = await ProcessErpEstimationWorkflow(job);
                    break;
                case "job-card-entry":
                    result = await ProcessJobCardEntry(job, websiteProcessor);
                    break;
                case "credential-update":
                    result = await ProcessCredentialUpdate(job, websiteProcessor);
                    break;
                case "costing-request":
                    result = await ProcessCostingRequest(job, websiteProcessor);
                    break;
                default:
                    result = await ProcessGeneralAutomation(job, websiteProcessor);
                    break;
            }

            var anomalyResult = await anomalyDetector.DetectAnomaliesAsync(job, result);
            if (anomalyResult.HasAnomalies)
            {
                _logger.LogWarning("Anomalies detected for job {jobId}: {anomalies}", 
                    job.Id, string.Join(", ", anomalyResult.Anomalies));
                result.Errors ??= new List<string>();
                result.Errors.AddRange(anomalyResult.Anomalies);
            }

            job.Status = result.Success ? JobStatus.Completed : JobStatus.Failed;
            job.ProcessingResult = System.Text.Json.JsonSerializer.Serialize(result);
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = result.Success ? null : result.Message;

            await dbContext.SaveChangesAsync();

            await _messageQueue.PublishAsync(new JobNotification
            {
                JobId = job.Id,
                Status = job.Status,
                Result = result,
                RecipientEmail = job.SenderEmail
            }, QueueNames.Notifications);

            _logger.LogInformation("Job {jobId} processed successfully. Status: {status}", 
                job.Id, job.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {jobId}", job.Id);
            
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.RetryCount++;
            job.CompletedAt = DateTime.UtcNow;

            if (job.RetryCount < 3)
            {
                job.Status = JobStatus.Retrying;
                job.CompletedAt = null;
                
                await Task.Delay(TimeSpan.FromMinutes(Math.Pow(2, job.RetryCount)));
                await _messageQueue.PublishAsync(emailMessage, QueueNames.JobProcessing);
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<ProcessingResult> ProcessJobCardEntry(Job job, IWebsiteProcessor processor)
    {
        try
        {
            if (string.IsNullOrEmpty(job.ExtractedCredentials) || string.IsNullOrEmpty(job.JobCardDetails))
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Missing credentials or job card details for job card entry",
                    Errors = new List<string> { "Extracted credentials or job card details are null or empty" }
                };
            }

            var credentials = System.Text.Json.JsonSerializer.Deserialize<ExtractedCredentials>(job.ExtractedCredentials);
            var jobCardInfo = System.Text.Json.JsonSerializer.Deserialize<JobCardInfo>(job.JobCardDetails);

            if (credentials?.SystemUrl == null || jobCardInfo?.JobNumber == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Invalid credentials or job card information",
                    Errors = new List<string> { "SystemUrl or JobNumber is missing" }
                };
            }

            var result = await processor.ProcessJobCardEntryAsync(credentials, jobCardInfo);
            return result;
        }
        catch (Exception ex)
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"Error processing job card entry: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task<ProcessingResult> ProcessCredentialUpdate(Job job, IWebsiteProcessor processor)
    {
        try
        {
            if (string.IsNullOrEmpty(job.ExtractedCredentials))
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "No credentials found in email for credential update"
                };
            }

            var credentials = System.Text.Json.JsonSerializer.Deserialize<ExtractedCredentials>(job.ExtractedCredentials);
            if (credentials == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Failed to deserialize credentials"
                };
            }

            return new ProcessingResult
            {
                Success = true,
                Message = "Credentials updated successfully",
                Data = new Dictionary<string, object> { { "updatedCredentials", credentials } }
            };
        }
        catch (Exception ex)
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"Error updating credentials: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task<ProcessingResult> ProcessCostingRequest(Job job, IWebsiteProcessor processor)
    {
        return new ProcessingResult
        {
            Success = true,
            Message = "Costing request processed successfully",
            Data = new Dictionary<string, object> { { "processedAt", DateTime.UtcNow } }
        };
    }

    private async Task<ProcessingResult> ProcessErpEstimationWorkflow(Job job)
    {
        using var scope = _serviceProvider.CreateScope();
        var erpProcessor = scope.ServiceProvider.GetRequiredService<IErpEstimationProcessor>();

        try
        {
            if (string.IsNullOrEmpty(job.Metadata))
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "No ERP job data found in job metadata"
                };
            }

            // Deserialize the extracted ERP data from job metadata
            var metadataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(job.Metadata);
            if (metadataDict == null || !metadataDict.ContainsKey("erpJobData"))
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "ERP job data not found in metadata"
                };
            }

            var erpDataJson = metadataDict["erpJobData"].ToString();
            var erpData = System.Text.Json.JsonSerializer.Deserialize<ErpJobData>(erpDataJson!);

            if (erpData == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Failed to deserialize ERP job data"
                };
            }

            _logger.LogInformation("Starting ERP estimation workflow for job {jobId}", job.Id);
            
            var result = await erpProcessor.ProcessEstimationWorkflowAsync(erpData);
            
            _logger.LogInformation("ERP estimation workflow completed for job {jobId}. Success: {success}", 
                job.Id, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ERP estimation workflow for job {jobId}", job.Id);
            return new ProcessingResult
            {
                Success = false,
                Message = $"ERP workflow error: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task<ProcessingResult> ProcessGeneralAutomation(Job job, IWebsiteProcessor processor)
    {
        return new ProcessingResult
        {
            Success = true,
            Message = "General automation completed",
            Data = new Dictionary<string, object> { { "processedAt", DateTime.UtcNow } }
        };
    }
}

// JobNotification moved to Rpa.Core.Models