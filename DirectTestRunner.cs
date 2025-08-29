using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rpa.Worker.Automation;
using Rpa.Core.Models;

namespace DirectTestRunner;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Direct ERP Automation Test Runner");
        Console.WriteLine("=====================================");
        
        // Setup logging
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<ErpEstimationProcessor>();
        
        // Setup configuration with default values
        var configData = new Dictionary<string, string>
        {
            {"ERP:BaseUrl", "http://13.200.122.70/"},
            {"Browser:Type", "chromium"},
            {"Browser:Headless", "false"},
            {"Browser:SlowMo", "1000"},
            {"Browser:Timeout", "60000"},
            {"Automation:ScreenshotPath", "logs/screenshots/"}
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        // Create test data matching your email
        var erpData = new ErpJobData
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

        Console.WriteLine("📧 Test Data Created:");
        Console.WriteLine($"   Company: {erpData.CompanyLogin?.CompanyName}");
        Console.WriteLine($"   User: {erpData.UserLogin?.Username}");
        Console.WriteLine($"   Client: {erpData.JobDetails?.Client}");
        Console.WriteLine($"   Content: {erpData.JobDetails?.Content}");
        Console.WriteLine($"   Quantity: {erpData.JobDetails?.Quantity:N0}");
        Console.WriteLine();

        try
        {
            Console.WriteLine("🔧 Initializing ERP Estimation Processor...");
            var processor = new ErpEstimationProcessor(logger, configuration);

            Console.WriteLine("🌐 Starting Browser Automation...");
            Console.WriteLine("   Browser will open and navigate to ERP system");
            Console.WriteLine("   Watch the automation execute all 16 steps!");
            Console.WriteLine();
            
            var result = await processor.ProcessEstimationWorkflowAsync(erpData);

            Console.WriteLine("📊 Automation Results:");
            Console.WriteLine("======================");
            Console.WriteLine($"✅ Success: {result.Success}");
            Console.WriteLine($"📝 Message: {result.Message}");
            
            if (result.Data?.ContainsKey("workflowSteps") == true)
            {
                var steps = result.Data["workflowSteps"] as List<object>;
                Console.WriteLine($"🔄 Completed Steps: {steps?.Count ?? 0}/16");
            }

            if (result.Data?.ContainsKey("filledFields") == true)
            {
                var filledFields = result.Data["filledFields"] as List<string>;
                Console.WriteLine($"📋 Planning Sheet Fields Filled: {filledFields?.Count ?? 0}");
                
                if (filledFields?.Any() == true)
                {
                    Console.WriteLine("\n📝 Sample Filled Fields:");
                    foreach (var field in filledFields.Take(10))
                    {
                        Console.WriteLine($"   ✓ {field}");
                    }
                    if (filledFields.Count > 10)
                    {
                        Console.WriteLine($"   ... and {filledFields.Count - 10} more fields");
                    }
                }
            }

            if (result.Data?.ContainsKey("screenshot") == true)
            {
                var screenshot = result.Data["screenshot"] as string;
                if (!string.IsNullOrEmpty(screenshot))
                {
                    Console.WriteLine($"📷 Screenshot Captured: {screenshot.Length} bytes");
                }
            }

            if (!result.Success && result.Errors?.Any() == true)
            {
                Console.WriteLine("\n❌ Errors Encountered:");
                foreach (var error in result.Errors.Take(5))
                {
                    Console.WriteLine($"   • {error}");
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Critical Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }

        Console.WriteLine();
        Console.WriteLine("🎯 Test Complete! Press any key to exit...");
        Console.ReadKey();
    }
}