using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Rpa.Core.Data;
using Rpa.Core.Models;
using Xunit;

namespace Rpa.Core.Tests.Data;

public class RpaDbContextTests : IDisposable
{
    private readonly RpaDbContext _context;

    public RpaDbContextTests()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new RpaDbContext(options);
    }

    [Fact]
    public async Task DbContext_Should_Save_And_Retrieve_Job()
    {
        // Arrange
        var job = new Job
        {
            EmailSubject = "Test Job",
            SenderEmail = "test@example.com",
            EmailBody = "Test body",
            JobType = "job-card-entry",
            Status = JobStatus.Pending
        };

        // Act
        await _context.Jobs.AddAsync(job);
        await _context.SaveChangesAsync();

        var retrievedJob = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == job.Id);

        // Assert
        retrievedJob.Should().NotBeNull();
        retrievedJob!.EmailSubject.Should().Be("Test Job");
        retrievedJob.SenderEmail.Should().Be("test@example.com");
        retrievedJob.Status.Should().Be(JobStatus.Pending);
    }

    [Fact]
    public async Task DbContext_Should_Update_Job_Status()
    {
        // Arrange
        var job = new Job
        {
            EmailSubject = "Test Job",
            SenderEmail = "test@example.com",
            EmailBody = "Test body",
            Status = JobStatus.Pending
        };

        await _context.Jobs.AddAsync(job);
        await _context.SaveChangesAsync();

        // Act
        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updatedJob = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == job.Id);

        // Assert
        updatedJob.Should().NotBeNull();
        updatedJob!.Status.Should().Be(JobStatus.Processing);
        updatedJob.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DbContext_Should_Handle_Complex_Json_Data()
    {
        // Arrange
        var credentials = new ExtractedCredentials
        {
            Username = "testuser",
            Password = "testpass",
            SystemUrl = "https://example.com"
        };

        var jobCardInfo = new JobCardInfo
        {
            JobNumber = "JOB-001",
            Description = "Test job card",
            CustomerName = "Test Customer",
            EstimatedCost = 1500.00m
        };

        var job = new Job
        {
            EmailSubject = "Test Job",
            SenderEmail = "test@example.com",
            EmailBody = "Test body",
            ExtractedCredentials = System.Text.Json.JsonSerializer.Serialize(credentials),
            JobCardDetails = System.Text.Json.JsonSerializer.Serialize(jobCardInfo)
        };

        // Act
        await _context.Jobs.AddAsync(job);
        await _context.SaveChangesAsync();

        var retrievedJob = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == job.Id);

        // Assert
        retrievedJob.Should().NotBeNull();
        retrievedJob!.ExtractedCredentials.Should().NotBeNull();
        retrievedJob.JobCardDetails.Should().NotBeNull();

        var retrievedCredentials = System.Text.Json.JsonSerializer.Deserialize<ExtractedCredentials>(retrievedJob.ExtractedCredentials!);
        var retrievedJobCardInfo = System.Text.Json.JsonSerializer.Deserialize<JobCardInfo>(retrievedJob.JobCardDetails!);

        retrievedCredentials.Should().NotBeNull();
        retrievedCredentials!.Username.Should().Be("testuser");
        retrievedCredentials.SystemUrl.Should().Be("https://example.com");

        retrievedJobCardInfo.Should().NotBeNull();
        retrievedJobCardInfo!.JobNumber.Should().Be("JOB-001");
        retrievedJobCardInfo.EstimatedCost.Should().Be(1500.00m);
    }

    [Fact]
    public async Task DbContext_Should_Query_By_Status()
    {
        // Arrange
        var pendingJob = new Job { EmailSubject = "Pending Job", SenderEmail = "test1@example.com", EmailBody = "Body", Status = JobStatus.Pending };
        var processingJob = new Job { EmailSubject = "Processing Job", SenderEmail = "test2@example.com", EmailBody = "Body", Status = JobStatus.Processing };
        var completedJob = new Job { EmailSubject = "Completed Job", SenderEmail = "test3@example.com", EmailBody = "Body", Status = JobStatus.Completed };

        await _context.Jobs.AddRangeAsync(pendingJob, processingJob, completedJob);
        await _context.SaveChangesAsync();

        // Act
        var pendingJobs = await _context.Jobs.Where(j => j.Status == JobStatus.Pending).ToListAsync();
        var processingJobs = await _context.Jobs.Where(j => j.Status == JobStatus.Processing).ToListAsync();

        // Assert
        pendingJobs.Should().HaveCount(1);
        pendingJobs[0].EmailSubject.Should().Be("Pending Job");

        processingJobs.Should().HaveCount(1);
        processingJobs[0].EmailSubject.Should().Be("Processing Job");
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}