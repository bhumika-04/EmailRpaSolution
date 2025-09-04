using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

Console.WriteLine("🔍 Manual Guided ERP Process Documentation");
Console.WriteLine("=" + new string('=', 50));
Console.WriteLine("I will open the ERP system and wait for your manual actions");
Console.WriteLine("Please perform each step manually while I observe and document");
Console.WriteLine("");

// Start Playwright browser
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = false,  // Keep browser visible
    SlowMo = 1000      // Slow down for observation
});

var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();

// Navigate to ERP system
Console.WriteLine("🚀 Navigating to ERP system at http://13.200.122.70/");
await page.GotoAsync("http://13.200.122.70/");
await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

Console.WriteLine("✅ ERP system loaded successfully");
Console.WriteLine("");
Console.WriteLine("📋 Now please perform the following steps manually:");
Console.WriteLine("1. Company Login (indusweb / 123)");
Console.WriteLine("2. User Login (Admin / 99811)");
Console.WriteLine("3. Navigate to Estimation");
Console.WriteLine("4. Handle tour guide");
Console.WriteLine("5. Handle Quotation Finalize popup (Category selection)");
Console.WriteLine("6. Add Quantity (10000)");
Console.WriteLine("7. Add Content (Reverse Tuck In)");
Console.WriteLine("8. Click Plan button");
Console.WriteLine("9. Fill planning sheet");
Console.WriteLine("10. Add Processes (_Cutting, F/B Printing)");
Console.WriteLine("");
Console.WriteLine("⏳ Browser will stay open for 30 minutes for manual testing...");
Console.WriteLine("🔍 I am watching and will document each action you perform");

// Keep browser open for manual testing
Console.WriteLine("");
Console.WriteLine("READY FOR MANUAL TESTING - PLEASE PROCEED");
Console.WriteLine("Press any key when you want to close the browser...");

// Wait for user input or 30 minutes
var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
try
{
    await Task.Run(() => Console.ReadKey(), cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("");
    Console.WriteLine("⏰ 30 minute timeout reached");
}

Console.WriteLine("");
Console.WriteLine("🏁 Manual testing session complete");
Console.WriteLine("Thank you for the step-by-step demonstration!");

await browser.CloseAsync();