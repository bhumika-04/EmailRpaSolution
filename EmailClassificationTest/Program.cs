using Rpa.Core.Models;
using Rpa.Listener;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

Console.WriteLine("🧪 Testing Email Classification and Data Extraction");
Console.WriteLine("=" + new string('=', 50));

var configuration = new ConfigurationBuilder().Build();
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var classifierLogger = loggerFactory.CreateLogger<EmailClassifierService>();
var extractorLogger = loggerFactory.CreateLogger<DataExtractorService>();

var classifier = new EmailClassifierService(classifierLogger, configuration);
var extractor = new DataExtractorService(extractorLogger);

var testEmail = new EmailMessage
{
    Subject = "ERP ESTIMATION - Test Request",  
    From = "test@example.com",
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
  Finish  = Gloss"
};

try
{
    Console.WriteLine("\n🔍 Testing Email Classification...");
    var result = await classifier.ClassifyAsync(testEmail);
    
    Console.WriteLine($"✅ Classification: {result.Classification}");
    Console.WriteLine($"✅ Confidence: {result.Confidence:P}");

    Console.WriteLine("\n📊 Testing Data Extraction...");
    var structuredData = await extractor.ExtractStructuredDataAsync(testEmail.Body, result.Classification);
    
    Console.WriteLine($"✅ Extracted {structuredData.Count} data elements");
    
    Console.WriteLine("\n🎉 Email classification test completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
}
