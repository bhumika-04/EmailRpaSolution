using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Rpa.Core.Models;
using Rpa.Worker.Automation;
using Xunit;

namespace Rpa.Worker.Tests.Automation;

public class WebsiteProcessorTests : IDisposable
{
    private readonly Mock<ILogger<WebsiteProcessor>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly WebsiteProcessor _processor;

    public WebsiteProcessorTests()
    {
        _mockLogger = new Mock<ILogger<WebsiteProcessor>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _mockConfiguration.Setup(c => c["Browser:Type"]).Returns("chromium");
        _mockConfiguration.Setup(c => c.GetValue<bool>("Browser:Headless", true)).Returns(true);
        _mockConfiguration.Setup(c => c.GetValue<int>("Browser:SlowMo", 0)).Returns(0);
        _mockConfiguration.Setup(c => c.GetValue<int>("Browser:Timeout", 30000)).Returns(30000);

        _processor = new WebsiteProcessor(_mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public void WebsiteProcessor_Should_Initialize_Successfully()
    {
        // Arrange & Act & Assert
        _processor.Should().NotBeNull();
        _processor.Should().BeAssignableTo<IWebsiteProcessor>();
    }

    [Fact]
    public async Task ProcessJobCardEntryAsync_Should_Return_Error_For_Null_Credentials()
    {
        // Arrange
        ExtractedCredentials? credentials = null;
        var jobCardInfo = new JobCardInfo { JobNumber = "JOB-001" };

        // Act
        var result = await _processor.ProcessJobCardEntryAsync(credentials!, jobCardInfo);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Missing credentials");
    }

    [Fact]
    public async Task ProcessJobCardEntryAsync_Should_Return_Error_For_Missing_System_Url()
    {
        // Arrange
        var credentials = new ExtractedCredentials
        {
            Username = "testuser",
            Password = "testpass"
            // SystemUrl is null
        };
        var jobCardInfo = new JobCardInfo { JobNumber = "JOB-001" };

        // Act
        var result = await _processor.ProcessJobCardEntryAsync(credentials, jobCardInfo);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid credentials");
    }

    [Fact]
    public async Task ProcessJobCardEntryAsync_Should_Return_Error_For_Missing_Job_Number()
    {
        // Arrange
        var credentials = new ExtractedCredentials
        {
            Username = "testuser",
            Password = "testpass",
            SystemUrl = "https://example.com"
        };
        var jobCardInfo = new JobCardInfo(); // JobNumber is null

        // Act
        var result = await _processor.ProcessJobCardEntryAsync(credentials, jobCardInfo);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid credentials or job card information");
    }

    [Fact]
    public void WebsiteProcessor_Should_Be_Disposable()
    {
        // Arrange & Act & Assert
        _processor.Should().BeAssignableTo<IDisposable>();
        
        // Should not throw
        var disposing = () => _processor.Dispose();
        disposing.Should().NotThrow();
    }

    public void Dispose()
    {
        _processor?.Dispose();
    }
}

public class AnomalyDetectorServiceTests
{
    private readonly Mock<ILogger<AnomalyDetectorService>> _mockLogger;
    private readonly AnomalyDetectorService _detector;

    public AnomalyDetectorServiceTests()
    {
        _mockLogger = new Mock<ILogger<AnomalyDetectorService>>();
        _detector = new AnomalyDetectorService(_mockLogger.Object);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_Should_Detect_Old_Jobs()
    {
        // Arrange
        var oldJob = new Job
        {
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            Status = JobStatus.Processing
        };
        var result = new ProcessingResult { Success = true };

        // Act
        var anomalyResult = await _detector.DetectAnomaliesAsync(oldJob, result);

        // Assert
        anomalyResult.Should().NotBeNull();
        anomalyResult.HasAnomalies.Should().BeTrue();
        anomalyResult.Anomalies.Should().Contain(a => a.Contains("older than 7 days"));
    }

    [Fact]
    public async Task DetectAnomaliesAsync_Should_Detect_High_Retry_Count()
    {
        // Arrange
        var job = new Job
        {
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            RetryCount = 3,
            Status = JobStatus.Processing
        };
        var result = new ProcessingResult { Success = true };

        // Act
        var anomalyResult = await _detector.DetectAnomaliesAsync(job, result);

        // Assert
        anomalyResult.Should().NotBeNull();
        anomalyResult.HasAnomalies.Should().BeTrue();
        anomalyResult.Anomalies.Should().Contain(a => a.Contains("High retry count"));
    }

    [Fact]
    public async Task DetectAnomaliesAsync_Should_Detect_Successful_Result_Without_Data()
    {
        // Arrange
        var job = new Job
        {
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            RetryCount = 0,
            Status = JobStatus.Processing
        };
        var result = new ProcessingResult 
        { 
            Success = true,
            Data = new Dictionary<string, object>() // Empty data
        };

        // Act
        var anomalyResult = await _detector.DetectAnomaliesAsync(job, result);

        // Assert
        anomalyResult.Should().NotBeNull();
        anomalyResult.HasAnomalies.Should().BeTrue();
        anomalyResult.Anomalies.Should().Contain(a => a.Contains("Successful result but no data"));
    }

    [Fact]
    public async Task DetectAnomaliesAsync_Should_Return_No_Anomalies_For_Normal_Job()
    {
        // Arrange
        var job = new Job
        {
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            RetryCount = 0,
            Status = JobStatus.Processing
        };
        var result = new ProcessingResult 
        { 
            Success = true,
            Data = new Dictionary<string, object> { { "processed", true } }
        };

        // Act
        var anomalyResult = await _detector.DetectAnomaliesAsync(job, result);

        // Assert
        anomalyResult.Should().NotBeNull();
        anomalyResult.HasAnomalies.Should().BeFalse();
        anomalyResult.Anomalies.Should().BeEmpty();
    }
}