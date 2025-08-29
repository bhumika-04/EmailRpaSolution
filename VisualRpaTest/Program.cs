using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rpa.Core.Models;
using Rpa.Worker.Automation;
using System.Text.Json;

Console.WriteLine("🤖 Live ERP Automation - Real System Test");
Console.WriteLine("=" + new string('=', 60));
Console.WriteLine("This will connect to your actual ERP at http://13.200.122.70/");
Console.WriteLine("🚨 WARNING: This will interact with your real ERP system!");
Console.WriteLine("Press ENTER to start real ERP automation...");
Console.ReadLine();

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
        Gsm = 100,
        Mill = "JK",
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
        WastageType = "Standard",
        GrainDirection = "Across",
        OnlineCoating = "None",
        Trimming = "0/0/0/0",
        Striping = "0/0/0/0"
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
    Console.WriteLine("\n🚀 Starting Real ERP Automation...");
    Console.WriteLine("Watch the browser window - you'll see each step happening!");
    Console.WriteLine("\n📝 16-Step Real ERP Process:");
    Console.WriteLine("1-3. Company & User Login");
    Console.WriteLine("4-6. Navigate to Estimation & Handle Tour");
    Console.WriteLine("7-10. Add Quantity & Content");
    Console.WriteLine("11-13. Planning & Job Details");
    Console.WriteLine("14-16. Add Processes & Show Cost");

    // Initialize the real ERP processor
    using var processor = new ErpEstimationProcessor(logger, configuration);

    // Run the complete real workflow
    var result = await processor.ProcessEstimationWorkflowAsync(testErpData);

    Console.WriteLine($"\n🎯 ERP Automation Result:");
    Console.WriteLine($"   ✅ Success: {result.Success}");
    Console.WriteLine($"   📝 Message: {result.Message}");

    if (result.Data != null)
    {
        Console.WriteLine($"   📊 Data Elements: {result.Data.Count}");
        
        if (result.Data.ContainsKey("completedSteps"))
        {
            Console.WriteLine($"   ✅ Steps Executed: {result.Data["completedSteps"]}/{result.Data["totalSteps"]}");
        }

        if (result.Data.ContainsKey("workflowSteps"))
        {
            Console.WriteLine($"   📋 Workflow Steps Recorded");
        }

        if (result.Data.ContainsKey("screenshot"))
        {
            var screenshotData = result.Data["screenshot"].ToString();
            if (!string.IsNullOrEmpty(screenshotData))
            {
                Console.WriteLine($"   📸 Final Screenshot: {screenshotData.Length} characters");
            }
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

    Console.WriteLine("\n🏁 Real ERP automation completed!");
    Console.WriteLine("Press ENTER to exit...");
    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error during ERP automation: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    
    // Keep browser open for 10 seconds to see what happened
    Console.WriteLine("\n⏳ Keeping browser open for 10 seconds to inspect...");
    await Task.Delay(10000);
    
    Console.WriteLine("\nPress ENTER to exit...");
    Console.ReadLine();
}