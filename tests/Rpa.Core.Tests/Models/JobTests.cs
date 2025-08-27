using FluentAssertions;
using Rpa.Core.Models;
using Xunit;

namespace Rpa.Core.Tests.Models;

public class JobTests
{
    [Fact]
    public void Job_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var job = new Job();

        // Assert
        job.Id.Should().NotBeEmpty();
        job.Status.Should().Be(JobStatus.Pending);
        job.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        job.RetryCount.Should().Be(0);
        job.Priority.Should().Be(5);
    }

    [Fact]
    public void Job_Should_Accept_Valid_Properties()
    {
        // Arrange
        var job = new Job();
        var emailSubject = "Test Job Card Entry";
        var senderEmail = "test@example.com";
        var emailBody = "Test email body with job details";

        // Act
        job.EmailSubject = emailSubject;
        job.SenderEmail = senderEmail;
        job.EmailBody = emailBody;
        job.Status = JobStatus.Processing;
        job.JobType = "job-card-entry";

        // Assert
        job.EmailSubject.Should().Be(emailSubject);
        job.SenderEmail.Should().Be(senderEmail);
        job.EmailBody.Should().Be(emailBody);
        job.Status.Should().Be(JobStatus.Processing);
        job.JobType.Should().Be("job-card-entry");
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Processing)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Retrying)]
    public void JobStatus_Should_Accept_All_Valid_Statuses(JobStatus status)
    {
        // Arrange
        var job = new Job();

        // Act
        job.Status = status;

        // Assert
        job.Status.Should().Be(status);
    }

    [Fact]
    public void Job_Should_Track_Processing_Times()
    {
        // Arrange
        var job = new Job();
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddMinutes(5);

        // Act
        job.StartedAt = startTime;
        job.CompletedAt = endTime;

        // Assert
        job.StartedAt.Should().Be(startTime);
        job.CompletedAt.Should().Be(endTime);
        (job.CompletedAt - job.StartedAt).Should().Be(TimeSpan.FromMinutes(5));
    }
}