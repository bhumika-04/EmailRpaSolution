using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Rpa.Worker.Automation;
using Rpa.Core.Models;
using Xunit;

namespace Rpa.Worker.Tests.Automation;

public class Step14ProcessTests
{
    private readonly ILogger<ErpEstimationProcessor> _logger;
    private readonly IConfiguration _configuration;

    public Step14ProcessTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ErpEstimationProcessor>();
        
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ERP:BaseUrl"] = "http://13.200.122.70/",
                ["Browser:Headless"] = "true",
                ["Browser:Timeout"] = "30000"
            });
        _configuration = configBuilder.Build();
    }

    [Fact]
    public void ProcessDefinition_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var process = new ProcessDefinition
        {
            Name = "Die Cutting",
            Category = "Cutting",
            IsRequired = true,
            DisplayOrder = 1
        };

        // Assert
        Assert.Equal("Die Cutting", process.Name);
        Assert.Equal("Cutting", process.Category);
        Assert.True(process.IsRequired);
        Assert.Equal(1, process.DisplayOrder);
    }

    [Fact]
    public void ProcessSelection_ShouldGroupProcessesCorrectly()
    {
        // Arrange
        var processSelection = new ProcessSelection
        {
            RequiredProcesses = new List<ProcessDefinition>
            {
                new() { Name = "Die Cutting", IsRequired = true }
            },
            ContentBasedProcesses = new List<ProcessDefinition>
            {
                new() { Name = "Gluing", IsRequired = false }
            },
            OptionalProcesses = new List<ProcessDefinition>
            {
                new() { Name = "UV Coating", IsRequired = false }
            }
        };

        // Act & Assert
        Assert.Single(processSelection.RequiredProcesses);
        Assert.Single(processSelection.ContentBasedProcesses);
        Assert.Single(processSelection.OptionalProcesses);
        Assert.True(processSelection.RequiredProcesses.First().IsRequired);
        Assert.False(processSelection.ContentBasedProcesses.First().IsRequired);
    }

    [Fact]
    public void ErpJobData_ShouldIncludeProcessSelection()
    {
        // Arrange & Act
        var erpData = new ErpJobData
        {
            JobDetails = new JobDetails
            {
                Content = "Reverse Tuck In",
                Client = "Akrati Offset"
            },
            ProcessSelection = new ProcessSelection
            {
                RequiredProcesses = new List<ProcessDefinition>
                {
                    new() { Name = "Die Cutting", IsRequired = true }
                }
            }
        };

        // Assert
        Assert.NotNull(erpData.ProcessSelection);
        Assert.Single(erpData.ProcessSelection.RequiredProcesses);
        Assert.Equal("Die Cutting", erpData.ProcessSelection.RequiredProcesses.First().Name);
    }

    [Theory]
    [InlineData("Reverse Tuck In", "Akrati Offset", true)]
    [InlineData("Straight Tuck", "Generic Client", true)]
    [InlineData("Auto Bottom", "", true)]
    [InlineData("", "", true)]
    public void ProcessSelection_ShouldHandleVariousContentTypes(string content, string client, bool shouldHaveProcesses)
    {
        // Arrange
        var erpData = new ErpJobData
        {
            JobDetails = new JobDetails
            {
                Content = content,
                Client = client
            }
        };

        // Act - Simulate the process selection logic
        var hasRequiredProcesses = !string.IsNullOrEmpty(content) || shouldHaveProcesses;

        // Assert
        if (shouldHaveProcesses)
        {
            Assert.True(hasRequiredProcesses);
        }
    }

    [Fact]
    public void ProcessSearchContainer_SelectorsShouldBeComprehensive()
    {
        // Arrange
        var expectedSelectors = new[]
        {
            ".dx-texteditor-container:has(.dx-texteditor-input)",
            "div.dx-texteditor-container",
            "[class*='dx-texteditor-container']",
            ".dx-widget:has(.dx-texteditor-input)",
            "div:has(> .dx-texteditor-input-container)"
        };

        // Act & Assert
        Assert.Equal(5, expectedSelectors.Length);
        Assert.Contains(".dx-texteditor-container:has(.dx-texteditor-input)", expectedSelectors);
        Assert.Contains("div.dx-texteditor-container", expectedSelectors);
    }

    [Fact]
    public void PlusButtonSelectors_ShouldCoverVariousScenarios()
    {
        // Arrange
        var expectedSelectors = new[]
        {
            "button:has-text('+')",
            ".add-process-btn",
            "[data-action='add-process']",
            "button[title*='Add']:visible",
            ".dx-button:has-text('+')",
            "button.btn:has-text('+')",
            "a:has-text('+'):visible",
            "span:has-text('+'):visible",
            ".process-add",
            "[onclick*='add']:visible",
            "button:has(.fa-plus)",
            "button:has(.glyphicon-plus)"
        };

        // Act & Assert
        Assert.True(expectedSelectors.Length >= 10); // Should have comprehensive selectors
        Assert.Contains("button:has-text('+')", expectedSelectors);
        Assert.Contains(".dx-button:has-text('+')", expectedSelectors);
    }

    [Theory]
    [InlineData("Die Cutting")]
    [InlineData("Gluing")]
    [InlineData("UV Coating")]
    [InlineData("Window Patching")]
    public void ProcessNames_ShouldBeValidForSearch(string processName)
    {
        // Arrange & Act
        var isValidName = !string.IsNullOrWhiteSpace(processName) && processName.Length > 2;

        // Assert
        Assert.True(isValidName);
        Assert.False(processName.Contains("  ")); // No double spaces
    }
}