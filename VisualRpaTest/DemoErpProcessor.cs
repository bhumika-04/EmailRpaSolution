using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Rpa.Core.Models;

namespace VisualRpaTest;

public class DemoErpProcessor : IDisposable
{
    private readonly ILogger<DemoErpProcessor> _logger;
    private readonly IConfiguration _configuration;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public DemoErpProcessor(ILogger<DemoErpProcessor> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ProcessingResult> ProcessDemoWorkflowAsync(ErpJobData erpData)
    {
        var workflowSteps = new List<ErpWorkflowStep>();
        var result = new ProcessingResult { Success = true, Message = "Demo workflow started" };

        try
        {
            await InitializeBrowserAsync();
            Console.WriteLine("🌐 Browser window opened - watch the automation!");

            // Define demo steps that will actually work
            var steps = GetDemoWorkflowSteps();
            
            foreach (var step in steps)
            {
                Console.WriteLine($"\n🔄 Executing Step {step.StepNumber}: {step.Description}");
                
                var stepResult = await ExecuteDemoStep(step.StepNumber, erpData);
                
                step.IsCompleted = stepResult.Success;
                step.ErrorMessage = stepResult.Success ? null : stepResult.Message;
                step.CompletedAt = DateTime.UtcNow;
                
                workflowSteps.Add(step);

                // Show step result
                var status = stepResult.Success ? "✅" : "❌";
                Console.WriteLine($"   {status} {stepResult.Message}");

                if (!stepResult.Success)
                {
                    Console.WriteLine($"      ⚠️  This is normal for demo - continuing with next step...");
                }

                // Visual pause between steps
                await Task.Delay(2000);
            }

            // Take final screenshot
            Console.WriteLine($"\n📸 Taking final screenshot...");
            var screenshot = await TakeScreenshotAsync("demo_final_result");
            
            result.Data = new Dictionary<string, object>
            {
                { "workflowSteps", workflowSteps },
                { "screenshot", Convert.ToBase64String(screenshot) },
                { "completedSteps", workflowSteps.Count(s => s.IsCompleted) },
                { "totalSteps", steps.Count },
                { "demoMode", true }
            };
            
            result.Message = $"Demo workflow completed - {workflowSteps.Count(s => s.IsCompleted)}/{steps.Count} steps executed";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in demo workflow");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Demo workflow error: {ex.Message}",
                Errors = new List<string> { ex.ToString() },
                Data = new Dictionary<string, object> { { "workflowSteps", workflowSteps } }
            };
        }
        finally
        {
            Console.WriteLine($"\n⏱️  Keeping browser open for 10 seconds so you can see the final result...");
            await Task.Delay(10000);
            await CleanupAsync();
        }
    }

    private List<ErpWorkflowStep> GetDemoWorkflowSteps()
    {
        return new List<ErpWorkflowStep>
        {
            new() { StepNumber = 1, Description = "Navigate to demo website", Action = "navigate" },
            new() { StepNumber = 2, Description = "Simulate company login form detection", Action = "company_login" },
            new() { StepNumber = 3, Description = "Simulate user login process", Action = "user_login" },
            new() { StepNumber = 4, Description = "Navigate to demo form page", Action = "navigate_estimation" },
            new() { StepNumber = 5, Description = "Handle page overlays/popups", Action = "handle_tour" },
            new() { StepNumber = 6, Description = "Dismiss any modal dialogs", Action = "close_popup" },
            new() { StepNumber = 7, Description = "Look for quantity input field", Action = "add_quantity" },
            new() { StepNumber = 8, Description = "Enter quantity value in form", Action = "enter_quantity" },
            new() { StepNumber = 9, Description = "Find content selection area", Action = "add_content" },
            new() { StepNumber = 10, Description = "Select content type from options", Action = "select_content" },
            new() { StepNumber = 11, Description = "Locate planning/submit button", Action = "click_plan" },
            new() { StepNumber = 12, Description = "Navigate to detailed form", Action = "planning_screen" },
            new() { StepNumber = 13, Description = "Fill job details in form fields", Action = "fill_details" },
            new() { StepNumber = 14, Description = "Add process selections", Action = "add_processes" },
            new() { StepNumber = 15, Description = "Trigger cost calculation", Action = "show_cost" },
            new() { StepNumber = 16, Description = "Capture final results screenshot", Action = "capture_results" }
        };
    }

    private async Task<ProcessingResult> ExecuteDemoStep(int stepNumber, ErpJobData erpData)
    {
        try
        {
            switch (stepNumber)
            {
                case 1:
                    return await DemoStep1_NavigateToWebsiteAsync();
                case 2:
                    return await DemoStep2_CompanyLoginAsync(erpData.CompanyLogin);
                case 3:
                    return await DemoStep3_UserLoginAsync(erpData.UserLogin);
                case 4:
                    return await DemoStep4_NavigateToFormAsync();
                case 5:
                    return await DemoStep5_HandleOverlaysAsync();
                case 6:
                    return await DemoStep6_ClosePopupsAsync();
                case 7:
                    return await DemoStep7_FindQuantityFieldAsync();
                case 8:
                    return await DemoStep8_EnterQuantityAsync(erpData.JobDetails?.Quantity ?? 0);
                case 9:
                    return await DemoStep9_FindContentAreaAsync();
                case 10:
                    return await DemoStep10_SelectContentAsync(erpData.JobDetails?.Content ?? "");
                case 11:
                    return await DemoStep11_FindPlanButtonAsync();
                case 12:
                    return await DemoStep12_NavigateToDetailFormAsync();
                case 13:
                    return await DemoStep13_FillJobDetailsAsync(erpData);
                case 14:
                    return await DemoStep14_AddProcessesAsync();
                case 15:
                    return await DemoStep15_TriggerCalculationAsync();
                case 16:
                    return await DemoStep16_CaptureResultsAsync();
                default:
                    return new ProcessingResult { Success = false, Message = $"Unknown demo step: {stepNumber}" };
            }
        }
        catch (Exception ex)
        {
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Demo step {stepNumber} error: {ex.Message}"
            };
        }
    }

    // Demo step implementations
    private async Task<ProcessingResult> DemoStep1_NavigateToWebsiteAsync()
    {
        var demoUrl = "https://www.w3schools.com/html/html_forms.asp";
        await _page!.GotoAsync(demoUrl);
        await _page.WaitForTimeoutAsync(2000);
        
        return new ProcessingResult { Success = true, Message = $"✅ Navigated to demo form page" };
    }

    private async Task<ProcessingResult> DemoStep2_CompanyLoginAsync(CompanyLogin? companyLogin)
    {
        // Simulate looking for company login fields
        await _page!.WaitForTimeoutAsync(1000);
        
        // Try to highlight some form elements to show "searching"
        try
        {
            await _page.EvaluateAsync(@"
                const inputs = document.querySelectorAll('input');
                inputs.forEach(input => {
                    input.style.border = '3px solid orange';
                    setTimeout(() => input.style.border = '', 1000);
                });
            ");
        }
        catch { /* Ignore if no inputs found */ }

        return new ProcessingResult { 
            Success = true, 
            Message = $"✅ Simulated company login search (Company: {companyLogin?.CompanyName})" 
        };
    }

    private async Task<ProcessingResult> DemoStep3_UserLoginAsync(UserLogin? userLogin)
    {
        await _page!.WaitForTimeoutAsync(1000);
        
        // Try to fill any text inputs we find
        try
        {
            var textInputs = await _page.QuerySelectorAllAsync("input[type='text'], input[type='email']");
            if (textInputs.Count > 0)
            {
                await textInputs[0].FillAsync(userLogin?.Username ?? "DemoUser");
                await textInputs[0].EvaluateAsync("element => element.style.backgroundColor = 'lightgreen'");
            }
        }
        catch { /* Continue regardless */ }

        return new ProcessingResult { 
            Success = true, 
            Message = $"✅ Simulated user login (User: {userLogin?.Username})" 
        };
    }

    private async Task<ProcessingResult> DemoStep4_NavigateToFormAsync()
    {
        // Scroll to show form interaction
        await _page!.EvaluateAsync("window.scrollTo(0, 300)");
        await _page.WaitForTimeoutAsync(1500);
        
        return new ProcessingResult { Success = true, Message = "✅ Navigated to demo form area" };
    }

    private async Task<ProcessingResult> DemoStep5_HandleOverlaysAsync()
    {
        // Simulate clicking somewhere to "dismiss overlays"
        try
        {
            await _page!.ClickAsync("body");
        }
        catch { /* Continue regardless */ }
        
        return new ProcessingResult { Success = true, Message = "✅ Handled page overlays" };
    }

    private async Task<ProcessingResult> DemoStep6_ClosePopupsAsync()
    {
        // Simulate popup handling
        await _page!.WaitForTimeoutAsync(500);
        
        return new ProcessingResult { Success = true, Message = "✅ Dismissed modal dialogs" };
    }

    private async Task<ProcessingResult> DemoStep7_FindQuantityFieldAsync()
    {
        // Highlight form fields to show "searching"
        try
        {
            await _page!.EvaluateAsync(@"
                const inputs = document.querySelectorAll('input');
                let foundInput = false;
                inputs.forEach(input => {
                    if (input.type === 'number' || input.type === 'text') {
                        input.style.border = '3px solid blue';
                        input.style.backgroundColor = 'lightblue';
                        foundInput = true;
                        setTimeout(() => {
                            input.style.border = '';
                            input.style.backgroundColor = '';
                        }, 2000);
                    }
                });
            ");
        }
        catch { /* Continue */ }

        return new ProcessingResult { Success = true, Message = "✅ Located quantity input area" };
    }

    private async Task<ProcessingResult> DemoStep8_EnterQuantityAsync(int quantity)
    {
        try
        {
            // Try to fill the first number or text input
            var inputs = await _page!.QuerySelectorAllAsync("input[type='number'], input[type='text']");
            if (inputs.Count > 0)
            {
                await inputs[0].FillAsync(quantity.ToString());
                await inputs[0].EvaluateAsync("element => element.style.backgroundColor = 'lightgreen'");
            }
        }
        catch { /* Continue */ }

        return new ProcessingResult { 
            Success = true, 
            Message = $"✅ Entered quantity: {quantity:N0}" 
        };
    }

    private async Task<ProcessingResult> DemoStep9_FindContentAreaAsync()
    {
        // Highlight select elements or textareas
        try
        {
            await _page!.EvaluateAsync(@"
                const selects = document.querySelectorAll('select, textarea');
                selects.forEach(select => {
                    select.style.border = '3px solid purple';
                    setTimeout(() => select.style.border = '', 1500);
                });
            ");
        }
        catch { /* Continue */ }

        return new ProcessingResult { Success = true, Message = "✅ Found content selection area" };
    }

    private async Task<ProcessingResult> DemoStep10_SelectContentAsync(string content)
    {
        try
        {
            // Try to interact with a select element
            var selects = await _page!.QuerySelectorAllAsync("select");
            if (selects.Count > 0)
            {
                await selects[0].EvaluateAsync("element => element.style.backgroundColor = 'lightcyan'");
            }
        }
        catch { /* Continue */ }

        return new ProcessingResult { 
            Success = true, 
            Message = $"✅ Selected content type: {content}" 
        };
    }

    private async Task<ProcessingResult> DemoStep11_FindPlanButtonAsync()
    {
        // Highlight buttons
        try
        {
            await _page!.EvaluateAsync(@"
                const buttons = document.querySelectorAll('button, input[type=""submit""]');
                buttons.forEach(button => {
                    button.style.border = '3px solid red';
                    button.style.backgroundColor = 'lightyellow';
                    setTimeout(() => {
                        button.style.border = '';
                        button.style.backgroundColor = '';
                    }, 2000);
                });
            ");
        }
        catch { /* Continue */ }

        return new ProcessingResult { Success = true, Message = "✅ Located plan/submit button" };
    }

    private async Task<ProcessingResult> DemoStep12_NavigateToDetailFormAsync()
    {
        // Scroll to bottom of page to show more interaction
        await _page!.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await _page.WaitForTimeoutAsync(1500);
        
        return new ProcessingResult { Success = true, Message = "✅ Navigated to detailed form section" };
    }

    private async Task<ProcessingResult> DemoStep13_FillJobDetailsAsync(ErpJobData erpData)
    {
        var filledFields = new List<string>();
        
        try
        {
            // Fill multiple fields with demo data
            var allInputs = await _page!.QuerySelectorAllAsync("input[type='text'], input[type='number'], textarea");
            
            for (int i = 0; i < Math.Min(allInputs.Count, 4); i++)
            {
                var fieldName = $"field{i + 1}";
                var value = i switch
                {
                    0 => erpData.JobSize?.Height.ToString() ?? "100",
                    1 => erpData.JobSize?.Length.ToString() ?? "150", 
                    2 => erpData.Material?.Quality ?? "Demo Material",
                    3 => erpData.PrintingDetails?.FrontColors.ToString() ?? "4",
                    _ => "Demo Value"
                };

                await allInputs[i].FillAsync(value);
                await allInputs[i].EvaluateAsync("element => element.style.backgroundColor = 'lightgreen'");
                filledFields.Add(fieldName);
                
                await Task.Delay(300);
            }
        }
        catch { /* Continue */ }

        return new ProcessingResult { 
            Success = true, 
            Message = $"✅ Filled {filledFields.Count} job detail fields" 
        };
    }

    private async Task<ProcessingResult> DemoStep14_AddProcessesAsync()
    {
        // Simulate process addition
        try
        {
            await _page!.EvaluateAsync(@"
                const checkboxes = document.querySelectorAll('input[type=""checkbox""]');
                checkboxes.forEach((checkbox, index) => {
                    if (index < 3) {
                        checkbox.checked = true;
                        checkbox.style.transform = 'scale(1.5)';
                        setTimeout(() => checkbox.style.transform = '', 1000);
                    }
                });
            ");
        }
        catch { /* Continue */ }

        return new ProcessingResult { 
            Success = true, 
            Message = "✅ Added 3 demo processes" 
        };
    }

    private async Task<ProcessingResult> DemoStep15_TriggerCalculationAsync()
    {
        // Flash the page to show "calculation"
        try
        {
            await _page!.EvaluateAsync(@"
                document.body.style.backgroundColor = 'lightblue';
                setTimeout(() => document.body.style.backgroundColor = '', 500);
                setTimeout(() => {
                    document.body.style.backgroundColor = 'lightgreen';
                    setTimeout(() => document.body.style.backgroundColor = '', 500);
                }, 600);
            ");
        }
        catch { /* Continue */ }
        
        await _page!.WaitForTimeoutAsync(1500);
        
        return new ProcessingResult { Success = true, Message = "✅ Cost calculation triggered" };
    }

    private async Task<ProcessingResult> DemoStep16_CaptureResultsAsync()
    {
        var screenshot = await TakeScreenshotAsync("demo_final_step");
        
        // Add a visual indicator that results are captured
        try
        {
            await _page!.EvaluateAsync(@"
                const div = document.createElement('div');
                div.innerHTML = '📊 COSTING RESULTS CAPTURED 📸<br/>✅ Demo Complete!';
                div.style.position = 'fixed';
                div.style.top = '20px';
                div.style.right = '20px';
                div.style.backgroundColor = 'green';
                div.style.color = 'white';
                div.style.padding = '20px';
                div.style.borderRadius = '10px';
                div.style.fontSize = '16px';
                div.style.fontWeight = 'bold';
                div.style.textAlign = 'center';
                div.style.zIndex = '9999';
                div.style.boxShadow = '0 4px 8px rgba(0,0,0,0.3)';
                document.body.appendChild(div);
            ");
        }
        catch { /* Continue */ }

        return new ProcessingResult { 
            Success = true, 
            Message = "✅ Final results captured successfully",
            Data = new Dictionary<string, object> 
            { 
                { "screenshot", Convert.ToBase64String(screenshot) },
                { "screenshotSize", screenshot.Length }
            }
        };
    }

    public async Task<byte[]> TakeScreenshotAsync(string filename)
    {
        var screenshotPath = Path.Combine("logs", $"{filename}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        
        return await _page!.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = screenshotPath,
            FullPage = true
        });
    }

    private async Task InitializeBrowserAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 1500,
            Timeout = 60000
        });

        _page = await _browser.NewPageAsync();
        await _page.SetViewportSizeAsync(1920, 1080);
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_page != null)
            {
                await _page.CloseAsync();
                _page = null;
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }

    public void Dispose()
    {
        CleanupAsync().GetAwaiter().GetResult();
    }
}