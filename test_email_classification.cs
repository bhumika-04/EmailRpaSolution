using Rpa.Core.Models;
using Rpa.Listener;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

// Simple console test for email classification
class Program
{
    static async Task Main()
    {
        // Setup
        var configuration = new ConfigurationBuilder().Build();
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<EmailClassifierService>();
        var classifier = new EmailClassifierService(logger, configuration);

        // Test email content (your example)
        var testEmail = new EmailMessage
        {
            Subject = "ERP ESTIMATION - Test Request",
            From = "bhumika.indasanalytics@gmail.com",
            Body = @"Company Log-IN:
Company Name: indusweb  
Password: 123
User Log-IN:
Username : Admin
Password: 99811
Client: Akrati Offset
Content: Reverse Tuck In
Quantity: 10000
Job Size (mm):
  Height = 100
  Length = 150
  Width  = 50
  O.flap = 20
  P.flap = 20
Material:
  Quality = Real Art Paper
  GSM     = 100
  Mill    = JK
  Finish  = Gloss
Printing:
  Front Colors   = 4
  Back Colors    = 0
  Special Front  = 0
  Special Back   = 0
  Style          = Single Side
  Plate          = CTP Plate
Wastage & Finishing:
  Make Ready Sheets = 0
  Wastage Type      = Standard
  Grain Direction   = Across
  Online Coating    = None
  Trimming (T/B/L/R)= 0/0/0/0
  Striping (T/B/L/R)= 0/0/0/0"
        };

        // Test classification
        Console.WriteLine("🧪 Testing Email Classification...");
        var result = await classifier.ClassifyAsync(testEmail);
        
        Console.WriteLine($"✅ Classification: {result.Classification}");
        Console.WriteLine($"✅ Confidence: {result.Confidence:P}");
        Console.WriteLine($"✅ Method: {result.Metadata["method"]}");

        // Test data extraction  
        Console.WriteLine("\n🧪 Testing Data Extraction...");
        var extractor = new DataExtractorService(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<DataExtractorService>());
        var structuredData = await extractor.ExtractStructuredDataAsync(testEmail.Body, result.Classification);
        
        Console.WriteLine($"✅ Extracted {structuredData.Count} data elements");
        foreach(var item in structuredData)
        {
            Console.WriteLine($"   - {item.Key}: {item.Value?.ToString()?.Substring(0, Math.Min(50, item.Value.ToString()?.Length ?? 0))}...");
        }

        Console.WriteLine("\n✅ Email classification test completed successfully!");
    }
}