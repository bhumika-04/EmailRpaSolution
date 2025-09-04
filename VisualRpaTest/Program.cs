using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rpa.Core.Models;
using Rpa.Worker.Automation;
using System.Text.Json;

Console.WriteLine("🤖 Live ERP Automation - Testing Finish Field & Process Issues");
Console.WriteLine("=" + new string('=', 60));
Console.WriteLine("This will test the planning sheet with enhanced debugging");
Console.WriteLine("🔍 Focus: Finish field selection and process addition");
Console.WriteLine("🚀 Auto-starting ERP test in 2 seconds...");
await Task.Delay(2000);

// Configuration setup
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var loggerFactory = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger<ErpEstimationProcessor>();

// Create test data (your example)
var testErpData = new ErpJobData
{
    CompanyLogin = new CompanyLogin
    {
        CompanyName = "indusweb",
        Password = "123"
    },
    UserLogin = new UserLogin
    {
        Username = "Admin", 
        Password = "99811"
    },
    JobDetails = new JobDetails
    {
        Client = "Akrati Offset",
        Content = "Reverse Tuck In", 
        Quantity = 10000
    },
    JobSize = new JobSize
    {
        Height = 100,
        Length = 150,
        Width = 50,
        OFlap = 20,
        PFlap = 20
    },
    Material = new Material
    {
        Quality = "Real Art Paper",
        Gsm = 250,
        Mill = "Bajaj",
        Finish = "Gloss"
    },
    PrintingDetails = new PrintingDetails
    {
        FrontColors = 4,
        BackColors = 0,
        SpecialFront = 0,
        SpecialBack = 0,
        Style = "Single Side",
        Plate = "CTP Plate"
    },
    WastageFinishing = new WastageFinishing
    {
        MakeReadySheets = 0,
        WastageType = "Machine Default",
        GrainDirection = "Across",
        OnlineCoating = "Aqua Matt"
    },
    FinishingFields = new FinishingFields
    {
        Trimming = "0/0/0/0",
        Striping = "0/0/0/0",
        Gripper = "0",
        ColorStrip = "0",
        FinishedFormat = "Sheet Form"
    },
    ProcessSelection = new ProcessSelection
    {
        RequiredProcesses = new List<ProcessDefinition>
        {
            new ProcessDefinition
            {
                Name = "Cutting",
                Category = "Finishing",
                IsRequired = true,
                DisplayOrder = 1,
                Parameters = new Dictionary<string, string>()
            },
            new ProcessDefinition
            {
                Name = "F/B Printing",
                Category = "Printing",
                IsRequired = true,
                DisplayOrder = 2,
                Parameters = new Dictionary<string, string>()
            }
        },
        OptionalProcesses = new List<ProcessDefinition>(),
        ContentBasedProcesses = new List<ProcessDefinition>()
    }
};

Console.WriteLine("\n📋 Real ERP Data Loaded:");
Console.WriteLine($"   🏢 Company: {testErpData.CompanyLogin.CompanyName}");
Console.WriteLine($"   👤 User: {testErpData.UserLogin.Username}");
Console.WriteLine($"   🎯 Client: {testErpData.JobDetails.Client}");
Console.WriteLine($"   📦 Content: {testErpData.JobDetails.Content}");
Console.WriteLine($"   🔢 Quantity: {testErpData.JobDetails.Quantity:N0}");
Console.WriteLine($"   📏 Size: {testErpData.JobSize.Height}x{testErpData.JobSize.Length}x{testErpData.JobSize.Width}mm");

try
{
    Console.WriteLine("\n🚀 Starting ERP Test with Enhanced Debugging...");
    Console.WriteLine("🎯 Watch for these key logs:");
    Console.WriteLine("   • === Starting Segment 2: Raw Material ===");
    Console.WriteLine("   • 🔍 Processing material field: Finish = Gloss");
    Console.WriteLine("   • === Starting Segment 5: Process Details ===");
    Console.WriteLine("   • Process grid is empty - attempting to add processes");

    // Initialize the ERP processor
    using var processor = new ErpEstimationProcessor(logger, configuration);

    // Run the complete workflow
    var result = await processor.ProcessEstimationWorkflowAsync(testErpData);

    Console.WriteLine($"\n🎯 ERP Test Result:");
    Console.WriteLine($"   ✅ Success: {result.Success}");
    Console.WriteLine($"   📝 Message: {result.Message}");

    if (result.Data != null)
    {
        Console.WriteLine($"   📊 Data Elements: {result.Data.Count}");
        
        if (result.Data.ContainsKey("completedSteps"))
        {
            Console.WriteLine($"   ✅ Steps Executed: {result.Data["completedSteps"]}/{result.Data["totalSteps"]}");
        }
    }

    if (!result.Success && result.Errors?.Any() == true)
    {
        Console.WriteLine("\n❌ Errors encountered:");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"   • {error}");
        }
    }

    Console.WriteLine("\n🏁 ERP test completed!");
    Console.WriteLine("🎉 Workflow finished successfully!");
    await Task.Delay(5000); // 5-second pause to see results
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error during ERP automation: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    
    // Keep browser open for 30 seconds to see what happened
    Console.WriteLine("\n⏳ Keeping browser open for 30 seconds to inspect...");
    Console.WriteLine("🔍 Check the visible browser window to see where the process stopped.");
    await Task.Delay(30000);
    
    Console.WriteLine("\n❌ Program will exit automatically in 5 seconds...");
    await Task.Delay(5000);
}