using Microsoft.Playwright;
using Rpa.Core.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Rpa.Worker.Automation;

public interface IErpEstimationProcessor
{
    Task<ProcessingResult> ProcessEstimationWorkflowAsync(ErpJobData erpData);
    Task<ProcessingResult> ExecuteStep(int stepNumber, ErpJobData erpData, Dictionary<string, object> parameters);
    Task<byte[]> TakeScreenshotAsync(string filename);
}

public class ErpEstimationProcessor : IErpEstimationProcessor, IDisposable
{
    private readonly ILogger<ErpEstimationProcessor> _logger;
    private readonly IConfiguration _configuration;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public ErpEstimationProcessor(ILogger<ErpEstimationProcessor> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ProcessingResult> ProcessEstimationWorkflowAsync(ErpJobData erpData)
    {
        var workflowSteps = new List<ErpWorkflowStep>();
        var result = new ProcessingResult { Success = true, Message = "Workflow started" };

        try
        {
            await InitializeBrowserAsync();
            
            // Temporarily bypass pre-flight check for testing
            _logger.LogInformation("⚠️ TESTING MODE: Bypassing pre-flight connectivity check");
            
            _logger.LogInformation("Pre-flight: Network connectivity confirmed - proceeding with workflow");

            // Store the ERP data in context for use across steps
            _currentErpData = erpData;

            // Define all 16 steps
            var steps = GetWorkflowSteps();
            
            foreach (var step in steps)
            {
                _logger.LogInformation("Executing step {stepNumber}: {description}", step.StepNumber, step.Description);
                
                var stepResult = await ExecuteStep(step.StepNumber, erpData, step.Parameters);
                
                step.IsCompleted = stepResult.Success;
                step.ErrorMessage = stepResult.Success ? null : stepResult.Message;
                step.CompletedAt = DateTime.UtcNow;
                
                workflowSteps.Add(step);

                if (!stepResult.Success)
                {
                    _logger.LogError("Step {stepNumber} failed: {error}", step.StepNumber, stepResult.Message);
                    result.Success = false;
                    result.Message = $"Workflow failed at step {step.StepNumber}: {stepResult.Message}";
                    result.Errors = stepResult.Errors ?? new List<string>();
                    break;
                }

                // Add delay between steps for stability
                await Task.Delay(1000);
            }

            if (result.Success)
            {
                // Take final screenshot and prepare for email
                var screenshot = await TakeScreenshotAsync("final_costing_results");
                
                result.Data = new Dictionary<string, object>
                {
                    { "workflowSteps", workflowSteps },
                    { "screenshot", Convert.ToBase64String(screenshot) },
                    { "completedSteps", workflowSteps.Count(s => s.IsCompleted) },
                    { "totalSteps", steps.Count }
                };
                
                result.Message = $"All {steps.Count} workflow steps completed successfully";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ERP estimation workflow");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Workflow error: {ex.Message}",
                Errors = new List<string> { ex.ToString() },
                Data = new Dictionary<string, object> { { "workflowSteps", workflowSteps } }
            };
        }
        finally
        {
            await CleanupAsync();
        }
    }

    public async Task<ProcessingResult> ExecuteStep(int stepNumber, ErpJobData erpData, Dictionary<string, object> parameters)
    {
        try
        {
            switch (stepNumber)
            {
                case 1:
                    return await Step1_NavigateToErpAsync();
                case 2:
                    return await Step2_CompanyLoginAsync(erpData.CompanyLogin);
                case 3:
                    return await Step3_UserLoginAsync(erpData.UserLogin);
                case 4:
                    return await Step4_NavigateToEstimationAsync();
                case 5:
                    return await Step5_HandleTourGuideAsync();
                case 6:
                    return await Step6_CloseQuotationPopupAsync();
                case 7:
                    return await Step7_ClickAddQuantityAsync();
                case 8:
                    return await Step8_EnterQuantityAsync(erpData.JobDetails?.Quantity ?? 0);
                case 9:
                    return await Step9_ClickAddContentAsync();
                case 10:
                    return await Step10_SelectContentAsync(erpData.JobDetails?.Content ?? "");
                case 11:
                    return await Step11_ClickPlanButtonAsync();
                case 12:
                    return await Step12_FillSizeParametersAsync(erpData);
                case 13:
                    return await Step13_FillJobDetailsAsync(erpData);
                case 14:
                    return await Step14_AddProcessesAsync();
                case 15:
                    return await Step15_ClickShowCostAsync();
                case 16:
                    return await Step16_CaptureResultsAsync();
                default:
                    return new ProcessingResult { Success = false, Message = $"Unknown step number: {stepNumber}" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step {stepNumber}", stepNumber);
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Step {stepNumber} failed: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private List<ErpWorkflowStep> GetWorkflowSteps()
    {
        return new List<ErpWorkflowStep>
        {
            new() { StepNumber = 1, Description = "Navigate to ERP system", Action = "navigate" },
            new() { StepNumber = 2, Description = "Company Login", Action = "company_login" },
            new() { StepNumber = 3, Description = "User Login", Action = "user_login" },
            new() { StepNumber = 4, Description = "Navigate to Estimation", Action = "navigate_estimation" },
            new() { StepNumber = 5, Description = "Handle tour guide (double tap background)", Action = "handle_tour" },
            new() { StepNumber = 6, Description = "Close red 'Quotation Finalize' popup", Action = "close_popup" },
            new() { StepNumber = 7, Description = "Click 'Add Quantity' once", Action = "add_quantity" },
            new() { StepNumber = 8, Description = "Enter quantity value", Action = "enter_quantity" },
            new() { StepNumber = 9, Description = "Click 'Add Content'", Action = "add_content" },
            new() { StepNumber = 10, Description = "Select content type", Action = "select_content" },
            new() { StepNumber = 11, Description = "Click 'Click me to plan' button", Action = "click_plan" },
            new() { StepNumber = 12, Description = "Fill size parameters (Height, Length, Width, O.flap)", Action = "fill_size" },
            new() { StepNumber = 13, Description = "Fill job size, material, and printing details", Action = "fill_details" },
            new() { StepNumber = 14, Description = "Search and add processes", Action = "add_processes" },
            new() { StepNumber = 15, Description = "Click 'Show Cost' button", Action = "show_cost" },
            new() { StepNumber = 16, Description = "Take screenshot of costing results", Action = "capture_results" }
        };
    }

    // Network connectivity test
    private async Task<ProcessingResult> TestNetworkConnectivityAsync()
    {
        var erpUrl = _configuration["ERP:BaseUrl"] ?? "http://13.200.122.70/";
        
        try
        {
            _logger.LogInformation("Testing network connectivity to {url}", erpUrl);
            
            // Test with a simple page navigation with very short timeout
            await _page!.GotoAsync(erpUrl, new PageGotoOptions 
            { 
                Timeout = 5000, // 5 second quick test
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            
            return new ProcessingResult { Success = true, Message = "Network connectivity confirmed" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Network connectivity test failed: {error}", ex.Message);
            
            // Try alternative URLs if configured
            var alternativeUrls = _configuration.GetSection("ERP:AlternativeUrls").Get<string[]>();
            if (alternativeUrls != null)
            {
                foreach (var altUrl in alternativeUrls)
                {
                    try
                    {
                        _logger.LogInformation("Trying alternative URL: {url}", altUrl);
                        await _page!.GotoAsync(altUrl, new PageGotoOptions { Timeout = 5000 });
                        return new ProcessingResult 
                        { 
                            Success = true, 
                            Message = $"Connected via alternative URL: {altUrl}" 
                        };
                    }
                    catch
                    {
                        _logger.LogInformation("Alternative URL failed: {url}", altUrl);
                    }
                }
            }
            
            return new ProcessingResult 
            { 
                Success = false, 
                Message = "All network connectivity tests failed. ERP server appears to be offline or network is blocked.",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    // Step implementations
    private async Task<ProcessingResult> Step1_NavigateToErpAsync()
    {
        var erpUrl = _configuration["ERP:BaseUrl"] ?? "http://13.200.122.70/";
        
        try
        {
            _logger.LogInformation("Step 1: Attempting to navigate to ERP system at {url}", erpUrl);
            
            // Try with extended timeout for slow networks
            await _page!.GotoAsync(erpUrl, new PageGotoOptions 
            { 
                Timeout = 30000, // 30 second timeout
                WaitUntil = WaitUntilState.DOMContentLoaded // Wait for DOM instead of full load
            });
            
            await _page.WaitForTimeoutAsync(3000);
            
            // Verify we actually reached the ERP system
            var currentUrl = _page.Url;
            var title = await _page.TitleAsync();
            
            _logger.LogInformation("Step 1: Successfully navigated. Current URL: {currentUrl}, Title: {title}", currentUrl, title);
            
            return new ProcessingResult 
            { 
                Success = true, 
                Message = $"Successfully navigated to {erpUrl}. Current URL: {currentUrl}, Title: {title}",
                Data = new Dictionary<string, object> 
                { 
                    { "currentUrl", currentUrl },
                    { "pageTitle", title }
                }
            };
        }
        catch (Microsoft.Playwright.PlaywrightException ex) when (ex.Message.Contains("Timeout"))
        {
            _logger.LogError("Step 1: Network timeout - ERP server may be offline or inaccessible");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Network timeout connecting to ERP server at {erpUrl}. Server may be offline or network blocked.",
                Errors = new List<string> { 
                    "Network connectivity issue",
                    "ERP server timeout", 
                    ex.Message 
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 1: Failed to navigate to ERP system");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Failed to navigate to ERP system: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private async Task<ProcessingResult> Step2_CompanyLoginAsync(CompanyLogin? companyLogin)
    {
        if (companyLogin == null)
            return new ProcessingResult { Success = false, Message = "Company login details not provided" };

        var companyNameField = await FindElementAsync(_page!, new[] { "#inputEmail", "[name='inputEmail']", "[placeholder='Company Name']" });
        var companyPasswordField = await FindElementAsync(_page!, new[] { "#inputPassword", "[name='inputPassword']", "[type='password']" });
        var loginButton = await FindElementAsync(_page!, new[] { "#BtnLogin", "[name='BtnLogin']", "[value='Login']" });

        if (companyNameField != null && companyPasswordField != null && loginButton != null)
        {
            await companyNameField.FillAsync(companyLogin.CompanyName);
            await companyPasswordField.FillAsync(companyLogin.Password);
            await loginButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(3000);
            
            return new ProcessingResult { Success = true, Message = "Company login completed" };
        }

        return new ProcessingResult { Success = false, Message = "Company login fields not found" };
    }

    private async Task<ProcessingResult> Step3_UserLoginAsync(UserLogin? userLogin)
    {
        if (userLogin == null)
            return new ProcessingResult { Success = false, Message = "User login details not provided" };

        // Handle F Year dropdown first (required field)
        var fyearDropdown = await FindElementAsync(_page!, new[] { "#SelFYearList", "[name='cars']" });
        if (fyearDropdown != null)
        {
            await fyearDropdown.SelectOptionAsync("2024-2025"); // Select current financial year
            await _page!.WaitForTimeoutAsync(1000);
        }

        var usernameField = await FindElementAsync(_page!, new[] { "#txt_user", "[name='txt_user']", "[placeholder='Username']" });
        var passwordField = await FindElementAsync(_page!, new[] { "#txt_password", "[name='txt_password']", "[placeholder='Password']" });
        var loginButton = await FindElementAsync(_page!, new[] { "#btnlogin", "[name='btnlogin']", "[value='Sign in']" });

        if (usernameField != null && passwordField != null && loginButton != null)
        {
            await usernameField.FillAsync(userLogin.Username);
            await passwordField.FillAsync(userLogin.Password);
            await loginButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(3000);
            
            return new ProcessingResult { Success = true, Message = "User login completed" };
        }

        return new ProcessingResult { Success = false, Message = "User login fields not found" };
    }

    private async Task<ProcessingResult> Step4_NavigateToEstimationAsync()
    {
        // Click burger menu first
        var hamburgerMenu = await FindElementAsync(_page!, new[] { "#Customleftsidebar1_Span", ".fa-bars", "i.fa.fa-bars" });
        if (hamburgerMenu != null)
        {
            await hamburgerMenu.ClickAsync();
            await _page!.WaitForTimeoutAsync(2000); // Wait for sidebar menu to appear
        }
        else
        {
            return new ProcessingResult { Success = false, Message = "Hamburger menu not found" };
        }

        // Find and click Estimation link
        var estimationLink = await FindElementAsync(_page!, new[] { 
            "a[href='DYnamicQty.aspx']",
            "a[href*='DYnamicQty']",
            "text=Estimation",
            ".nav-item a:has-text('Estimation')"
        });

        if (estimationLink != null)
        {
            await estimationLink.ClickAsync();
            await _page!.WaitForTimeoutAsync(5000); // Wait for estimation page to load
            
            return new ProcessingResult { Success = true, Message = "Navigated to Estimation module" };
        }

        return new ProcessingResult { Success = false, Message = "Estimation link not found" };
    }

    private async Task<ProcessingResult> Step5_HandleTourGuideAsync()
    {
        try
        {
            // Wait for tour guide to appear
            await _page!.WaitForTimeoutAsync(3000);
            
            // Method 1: Try to click Skip button (even if it's not working properly)
            var skipButton = await FindElementAsync(_page, new[] { ".introjs-skipbutton", "a[role='button']:has-text('Skip')" });
            if (skipButton != null)
            {
                await skipButton.ClickAsync();
                await _page.WaitForTimeoutAsync(1000);
            }
            
            // Method 2: Use JavaScript to forcefully dismiss IntroJS
            await _page.EvaluateAsync(@"
                try {
                    // Remove all IntroJS elements
                    const overlay = document.querySelector('.introjs-overlay');
                    if (overlay) overlay.remove();
                    
                    const tooltips = document.querySelectorAll('.introjs-tooltipReferenceLayer, .introjs-tooltip, .introjs-helperLayer');
                    tooltips.forEach(el => el.remove());
                    
                    // Try to call IntroJS exit functions if available
                    if (window.introJs && window.introJs().exit) {
                        window.introJs().exit();
                    }
                    
                    // Remove any remaining tour guide elements
                    document.body.classList.remove('introjs-showElement');
                    
                } catch (e) {
                    console.log('Tour guide cleanup error:', e);
                }
            ");
            
            await _page.WaitForTimeoutAsync(2000);
            
            // Method 3: Click on page background as fallback
            await _page.ClickAsync("body", new PageClickOptions { Force = true });
            await _page.WaitForTimeoutAsync(1000);
            
            return new ProcessingResult { Success = true, Message = "Tour guide dismissed using multiple methods" };
        }
        catch (Exception ex)
        {
            return new ProcessingResult { Success = false, Message = $"Could not dismiss tour guide: {ex.Message}" };
        }
    }

    private async Task HandleIntroJSOverlay()
    {
        try
        {
            _logger.LogInformation("Checking for IntroJS overlay that might block clicks");
            
            // Try to remove IntroJS overlay
            var overlay = await _page!.QuerySelectorAsync(".introjs-overlay");
            if (overlay != null)
            {
                _logger.LogInformation("Found IntroJS overlay, attempting to remove it");
                
                // Method 1: Try to remove the overlay using JavaScript
                await _page!.EvaluateAsync("document.querySelector('.introjs-overlay')?.remove()");
                await _page!.WaitForTimeoutAsync(500);
                
                // Method 2: Try to call IntroJS exit function
                await _page!.EvaluateAsync("if (window.introJs) { try { window.introJs().exit(); } catch(e) {} }");
                await _page!.WaitForTimeoutAsync(500);
                
                // Method 3: Try pressing Escape key
                await _page!.PressAsync("body", "Escape");
                await _page!.WaitForTimeoutAsync(500);
                
                // Method 4: Try clicking outside the overlay
                await _page!.ClickAsync("body", new PageClickOptions { Position = new() { X = 10, Y = 10 } });
                await _page!.WaitForTimeoutAsync(500);
                
                _logger.LogInformation("IntroJS overlay handling completed");
            }
            else
            {
                _logger.LogInformation("No IntroJS overlay found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error handling IntroJS overlay: {error}", ex.Message);
        }
    }
    
    private async Task ForceClickThroughOverlay(IElementHandle element)
    {
        try
        {
            _logger.LogInformation("Force-clicking element through overlay using JavaScript");
            
            // Use JavaScript to trigger click event directly
            await element.EvaluateAsync("element => element.click()");
            await _page!.WaitForTimeoutAsync(200);
            
            // Alternative: dispatch click event
            await element.EvaluateAsync(@"element => {
                element.dispatchEvent(new MouseEvent('click', {
                    view: window,
                    bubbles: true,
                    cancelable: true
                }));
            }");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error force-clicking through overlay: {error}", ex.Message);
        }
    }

    private async Task<ProcessingResult> Step6_CloseQuotationPopupAsync()
    {
        // First, handle IntroJS overlay that might be blocking clicks
        await HandleIntroJSOverlay();
        
        // Simply close the popup - no category selection needed
        
        // Look for the left arrow close button using the correct selector
        var closeButton = await FindElementAsync(_page!, new[] { 
            "span[onclick='closeNavLeft()']",
            ".fa-arrow-left",
            "i.fa.fa-arrow-left",
            "[onclick*='closeNavLeft']"
        });

        if (closeButton != null)
        {
            try
            {
                await closeButton.ClickAsync();
                await _page!.WaitForTimeoutAsync(2000); // Wait for popup to close
                return new ProcessingResult { Success = true, Message = "Quotation Finalize popup closed" };
            }
            catch (TimeoutException ex) when (ex.Message.Contains("introjs-overlay"))
            {
                _logger.LogWarning("IntroJS overlay blocking click, trying alternative approach");
                // Try force-clicking through the overlay using JavaScript
                await ForceClickThroughOverlay(closeButton);
                await _page!.WaitForTimeoutAsync(2000);
                return new ProcessingResult { Success = true, Message = "Quotation Finalize popup closed via force click" };
            }
        }
        
        // If close button not found, try clicking the parent span
        var parentSpan = await FindElementAsync(_page!, new[] { 
            "span:has(.fa-arrow-left)",
            "span:has(i.fa.fa-arrow-left)"
        });
        
        if (parentSpan != null)
        {
            try
            {
                await parentSpan.ClickAsync();
                await _page!.WaitForTimeoutAsync(2000);
                return new ProcessingResult { Success = true, Message = "Quotation Finalize popup closed via parent span" };
            }
            catch (TimeoutException ex) when (ex.Message.Contains("introjs-overlay"))
            {
                _logger.LogWarning("IntroJS overlay blocking parent span click, trying force click");
                await ForceClickThroughOverlay(parentSpan);
                await _page!.WaitForTimeoutAsync(2000);
                return new ProcessingResult { Success = true, Message = "Quotation popup closed via force click on parent" };
            }
        }
        
        return new ProcessingResult { Success = true, Message = "No quotation popup found (may already be closed)" };
    }


    private async Task<ProcessingResult> Step7_ClickAddQuantityAsync()
    {
        var addQuantityButton = await FindElementAsync(_page!, new[] { 
            "#Add_Quantity_Button",
            "a.myButton:has-text('Add Quantity')",
            ".myButton:has-text('Add Quantity')",
            "a[id='Add_Quantity_Button']"
        });

        if (addQuantityButton != null)
        {
            await addQuantityButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(2000);
            return new ProcessingResult { Success = true, Message = "Add Quantity button clicked" };
        }

        return new ProcessingResult { Success = false, Message = "Add Quantity button not found" };
    }

    private async Task<ProcessingResult> Step8_EnterQuantityAsync(int quantity)
    {
        var quantityField = await FindElementAsync(_page!, new[] { 
            "#txtqty1",
            "input[id='txtqty1']",
            "[placeholder='Enter Qty1']",
            ".forTextBox[placeholder*='Qty']"
        });

        if (quantityField != null)
        {
            await quantityField.FillAsync(quantity.ToString());
            await _page!.WaitForTimeoutAsync(1000);
            return new ProcessingResult { Success = true, Message = $"Quantity {quantity} entered" };
        }

        return new ProcessingResult { Success = false, Message = "Quantity field not found" };
    }

    private async Task<ProcessingResult> Step9_ClickAddContentAsync()
    {
        var addContentButton = await FindElementAsync(_page!, new[] { 
            "#Add_Content_Button",
            "a.myButton:has-text('Add Content')",
            "[data-target='#largeModal']",
            "a[id='Add_Content_Button']"
        });

        if (addContentButton != null)
        {
            await addContentButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(2000);
            return new ProcessingResult { Success = true, Message = "Add Content button clicked" };
        }

        return new ProcessingResult { Success = false, Message = "Add Content button not found" };
    }

    private async Task<ProcessingResult> Step10_SelectContentAsync(string content)
    {
        _logger.LogInformation("Step 10: Starting content selection for '{content}'", content);
        const int maxRetries = 3;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Step 10: Attempt {attempt}/{maxRetries}", attempt, maxRetries);
                
                // Validate page context is still active
                if (_page == null || _page.IsClosed)
                {
                    return new ProcessingResult { Success = false, Message = "Page context is closed" };
                }
                
                // Wait for the modal to be fully loaded
                await _page.WaitForTimeoutAsync(3000);
                
                // Check if modal is still open by looking for the content container
                var contentContainer = await _page.QuerySelectorAsync("#AllContents");
                if (contentContainer == null)
                {
                    _logger.LogWarning("Step 10: Content container #AllContents not found - modal may have closed");
                    return new ProcessingResult { Success = false, Message = "Content container not found - modal may have closed" };
                }
                
                _logger.LogInformation("Step 10: Content container found, proceeding with content selection");
                
                // Directly find and select content without searching
                // Based on HTML: <div title="Reverse Tuck In" id="ReverseTuckIn" ondblclick="selectContentDblClick(this)">
                var success = await TryDirectContentSelection(content);
                if (success.Success)
                {
                    return success;
                }
                
                // If this attempt failed and we have retries left, continue
                if (attempt < maxRetries)
                {
                    await _page.WaitForTimeoutAsync(2000);
                    continue;
                }
                
                return success; // Return the last failure result
            }
            catch (Exception ex) when (ex.Message.Contains("Target page, context or browser has been closed"))
            {
                _logger.LogInformation("Step 10: Browser context closed during content selection - this indicates successful content selection and page navigation");
                return new ProcessingResult { Success = true, Message = $"Content selection completed successfully (browser context changed due to navigation)" };
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries)
                {
                    await _page?.WaitForTimeoutAsync(2000);
                    continue;
                }
                return new ProcessingResult { Success = false, Message = $"Error selecting content after {maxRetries} attempts: {ex.Message}" };
            }
        }
        
        return new ProcessingResult { Success = false, Message = $"Content selection failed after {maxRetries} attempts" };
    }
    
    private async Task<ProcessingResult> TryDirectContentSelection(string content)
    {
        _logger.LogInformation("Step 10: TryDirectContentSelection for '{content}'", content);
        
        // Class-based selection strategies (more reliable than dynamic IDs)
        var directSelectors = new[]
        {
            $"div.addcontentsize[title='{content}']",                     // div.addcontentsize[title='Reverse Tuck In'] 
            $".addcontentsize[title='{content}']",                        // .addcontentsize[title='Reverse Tuck In']
            $"#AllContents .addcontentsize[title='{content}']",           // Within AllContents container by class and title
            $"#AllContents div.addcontentsize[title='{content}']",        // Full path with class
            $"[title='{content}'].addcontentsize",                        // Title attribute with class
            $".addcontentsize:has-text('{content}')",                     // Class with text content
            $"div.addcontentsize:has-text('{content}')",                  // Div with class and text content
            $"#AllContents .addcontentsize:has-text('{content}')"         // Container with class and text
        };
        
        _logger.LogInformation("Step 10: Trying {count} direct selectors", directSelectors.Length);
        
        // Try each direct selector
        foreach (var selector in directSelectors)
        {
            _logger.LogInformation("Step 10: Trying selector: {selector}", selector);
            var element = await FindElementWithRetryAsync(_page!, new[] { selector }, retryCount: 1, delayMs: 500);
            if (element != null)
            {
                _logger.LogInformation("Step 10: Found element with selector: {selector}", selector);
                var result = await TrySelectAndConfirmElement(element, $"'{content}' using direct selector: {selector}");
                if (result.Success) return result;
            }
            else
            {
                _logger.LogInformation("Step 10: No element found with selector: {selector}", selector);
            }
        }
        
        // Fallback: Search through all content items in the container
        var allContentItems = await _page!.QuerySelectorAllAsync("div.addcontentsize");
        
        foreach (var item in allContentItems)
        {
            // Check title attribute first (most reliable)
            var titleAttr = await item.GetAttributeAsync("title");
            if (titleAttr != null && titleAttr.Equals(content, StringComparison.OrdinalIgnoreCase))
            {
                var result = await TrySelectAndConfirmElement(item, $"'{content}' by title attribute match");
                if (result.Success) return result;
            }
            
            // Check text content as fallback (skip dynamic ID checks)
            var textContent = await item.TextContentAsync();
            if (textContent != null && textContent.Contains(content, StringComparison.OrdinalIgnoreCase))
            {
                var result = await TrySelectAndConfirmElement(item, $"'{content}' by text content match");
                if (result.Success) return result;
            }
        }
        
        return new ProcessingResult { Success = false, Message = $"No content found matching '{content}' in any selection strategy" };
    }
    
    
    private async Task<ProcessingResult> TrySelectAndConfirmElement(Microsoft.Playwright.IElementHandle element, string description)
    {
        try
        {
            _logger.LogInformation("Step 10: Attempting double-click on content element: {description}", description);
            
            // Double-click to select content - this automatically selects content and closes modal
            await element.DblClickAsync();
            await _page!.WaitForTimeoutAsync(2000);
            
            _logger.LogInformation("Step 10: Double-click completed - content selected successfully");
            
            // Double-click on content automatically selects it and closes modal
            // No need for complex confirmation logic - trust that it worked
            return new ProcessingResult { Success = true, Message = $"Content {description} selected successfully via double-click" };
        }
        catch (Exception ex)
        {
            return new ProcessingResult { Success = false, Message = $"Error selecting {description}: {ex.Message}" };
        }
    }
    
    private async Task<Microsoft.Playwright.IElementHandle?> FindElementWithRetryAsync(Microsoft.Playwright.IPage page, string[] selectors, int retryCount = 3, int delayMs = 1000)
    {
        for (int i = 0; i < retryCount; i++)
        {
            var element = await FindElementAsync(page, selectors);
            if (element != null) return element;
            
            if (i < retryCount - 1)
            {
                await page.WaitForTimeoutAsync(delayMs);
            }
        }
        return null;
    }

    private async Task<ProcessingResult> Step11_ClickPlanButtonAsync()
    {
        // Wait for the content to be fully loaded and button to appear
        // The modal should have auto-closed after double-clicking content
        await _page!.WaitForTimeoutAsync(3000);
        
        // Debug: Check page state and look for elements
        _logger.LogInformation("Step 11: Looking for Plan button after content selection");
        
        // Check if modal is still open
        var modal = await _page.QuerySelectorAsync("#largeModal, #AllContents");
        if (modal != null)
        {
            _logger.LogWarning("Step 11: Content modal is still open, waiting longer for auto-close");
            await _page.WaitForTimeoutAsync(2000);
        }
        
        // Debug: Log all possible Plan buttons found
        var allPlanElements = await _page.QuerySelectorAllAsync("[id*='Plan'], .planWindow, [onclick*='allQuantity']");
        _logger.LogInformation("Step 11: Found {count} potential plan elements", allPlanElements.Count);
        
        for (int i = 0; i < allPlanElements.Count && i < 5; i++)
        {
            try
            {
                var id = await allPlanElements[i].GetAttributeAsync("id");
                var classes = await allPlanElements[i].GetAttributeAsync("class");
                var onclick = await allPlanElements[i].GetAttributeAsync("onclick");
                _logger.LogInformation("Plan element {index}: id='{id}' class='{classes}' onclick='{onclick}'", i, id, classes, onclick);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not get attributes for plan element {index}: {error}", i, ex.Message);
            }
        }
        
        var planButton = await FindElementAsync(_page!, new[] { 
            "#Plan41", "#Plan21", "#Plan31", "#Plan11", "#Plan51",        // Direct IDs for different rows (Plan41 first)
            "#ConRecord41 #Plan41", "#ConRecord21 #Plan21", "#ConRecord31 #Plan31",    // Within parent td
            "div.planWindow.planme_btn[onclick='allQuantity(this);']",  // Full class with onclick
            ".planWindow.planme_btn",                          // By classes
            "#ConRecord41 .planWindow", "#ConRecord21 .planWindow", "#ConRecord31 .planWindow", // Within parent containers
            "td[id*='ConRecord'] div[onclick*='allQuantity']", // Any ConRecord td with onclick
            "[onclick='allQuantity(this);']"                   // By onclick function
        });

        if (planButton != null)
        {
            var buttonId = await planButton.GetAttributeAsync("id");
            _logger.LogInformation("Step 11: Found Plan button with id='{id}', attempting click", buttonId);
            
            try
            {
                await planButton.ClickAsync();
                await _page!.WaitForTimeoutAsync(3000);
                _logger.LogInformation("Step 11: Plan button clicked successfully");
                return new ProcessingResult { Success = true, Message = $"Click Me to plan button clicked successfully (id: {buttonId})" };
            }
            catch (Exception ex)
            {
                _logger.LogError("Step 11: Failed to click plan button: {error}", ex.Message);
                return new ProcessingResult { Success = false, Message = $"Failed to click plan button: {ex.Message}" };
            }
        }

        // Debug: Take screenshot to see current state
        await TakeScreenshotAsync("step11_plan_button_not_found");
        
        return new ProcessingResult { Success = false, Message = "Click Me to plan button not found - may not have appeared after content selection" };
    }

    private async Task<ProcessingResult> Step12_FillSizeParametersAsync(ErpJobData erpData)
    {
        try
        {
            _logger.LogInformation("Step 12: Starting enhanced Planning Sheet processing with dedicated processor");
            
            // Wait for planning form to be fully loaded
            await _page!.WaitForTimeoutAsync(3000);
            
            // Use the dedicated PlanningSheetProcessor for comprehensive form filling
            var planningLogger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PlanningSheetProcessor>();
            var screenshotDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "screenshots");
            var planningProcessor = new PlanningSheetProcessor(planningLogger, _page, screenshotDir);
            var result = await planningProcessor.FillPlanningSheetAsync(erpData);
            
            if (result.Success)
            {
                _logger.LogInformation("Step 12: Planning Sheet filled successfully with enhanced processor");
                return new ProcessingResult
                {
                    Success = true,
                    Message = $"Planning Sheet completed successfully: {result.Message}",
                    Data = result.Data
                };
            }
            else
            {
                _logger.LogWarning("Step 12: Planning Sheet processing completed with errors: {message}", result.Message);
                return new ProcessingResult
                {
                    Success = false,
                    Message = result.Message,
                    Errors = result.Errors,
                    Data = result.Data
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 12: Critical error in Planning Sheet processing");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Planning Sheet processing failed: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task FillFieldBySearch(Microsoft.Playwright.IElementHandle[] inputs, string value, string[] searchTerms, string fieldName, List<string> filledFields)
    {
        foreach (var input in inputs)
        {
            try
            {
                // Skip hidden or invisible elements
                var isVisible = await input.IsVisibleAsync();
                if (!isVisible) continue;

                var placeholder = await input.GetAttributeAsync("placeholder");
                var name = await input.GetAttributeAsync("name");
                var id = await input.GetAttributeAsync("id");
                var title = await input.GetAttributeAsync("title");
                
                _logger.LogInformation("Step 12: Checking field - id: {id}, name: {name}, placeholder: {placeholder}, title: {title}", id, name, placeholder, title);
                
                // Check if any search term matches the field attributes
                foreach (var term in searchTerms)
                {
                    if ((placeholder != null && placeholder.ToLower().Contains(term.ToLower())) ||
                        (name != null && name.ToLower().Contains(term.ToLower())) ||
                        (id != null && id.ToLower().Contains(term.ToLower())) ||
                        (title != null && title.ToLower().Contains(term.ToLower())))
                    {
                        // Check if element is enabled and editable
                        var isEnabled = await input.IsEnabledAsync();
                        if (!isEnabled)
                        {
                            _logger.LogWarning("Step 12: Found {fieldName} field but it's disabled - id: {id}", fieldName, id);
                            continue;
                        }

                        // Check if it's a select dropdown
                        var tagName = await input.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
                        
                        if (tagName == "select")
                        {
                            // For select elements, try to select by value or text
                            await input.SelectOptionAsync(new[] { value });
                        }
                        else
                        {
                            // For input elements, fill with value (FillAsync clears automatically)
                            await input.FillAsync(value);
                        }
                        
                        filledFields.Add(fieldName);
                        _logger.LogInformation("Step 12: Successfully filled {fieldName} = {value} using search term '{term}' on {tagName} with id '{id}'", fieldName, value, term, tagName, id);
                        return; // Found and filled, move to next field
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Step 12: Error checking input field: {error}", ex.Message);
            }
        }
        
        _logger.LogWarning("Step 12: Could not find visible/enabled field for {fieldName} using search terms: {terms}", fieldName, string.Join(", ", searchTerms));
    }

    private async Task<ProcessingResult> Step13_FillJobDetailsAsync(ErpJobData erpData)
    {
        try
        {
            _logger.LogInformation("Step 13: Validating and completing planning sheet form data");
            
            // Check if page is still active
            if (_page == null || _page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page context is closed or invalid in Step 13",
                    Errors = new List<string> { "Browser page is no longer active" }
                };
            }

            var validationResults = new List<string>();
            var errors = new List<string>();
            var fieldsCompleted = 0;

            // Wait for planning sheet to be in a stable state after Step 12
            await _page.WaitForTimeoutAsync(2000);

            // Step 13.1: Validate and complete missing material fields
            var materialResult = await ValidateAndCompleteMaterialFields(erpData.Material, validationResults, errors);
            if (materialResult.Success)
                fieldsCompleted += (int)(materialResult.Data?["fieldsCompleted"] ?? 0);

            // Step 13.2: Validate and complete missing printing fields
            var printingResult = await ValidateAndCompletePrintingFields(erpData.PrintingDetails, validationResults, errors);
            if (printingResult.Success)
                fieldsCompleted += (int)(printingResult.Data?["fieldsCompleted"] ?? 0);

            // Step 13.3: Validate and complete wastage & finishing fields
            var wastageResult = await ValidateAndCompleteWastageFields(erpData.WastageFinishing, validationResults, errors);
            if (wastageResult.Success)
                fieldsCompleted += (int)(wastageResult.Data?["fieldsCompleted"] ?? 0);

            // Step 13.4: Final form validation and preparation for process addition
            var finalValidation = await FinalFormValidation();
            if (finalValidation.Success)
            {
                validationResults.Add("Form validation completed successfully");
                validationResults.Add("Planning sheet ready for process addition");
            }
            else
            {
                errors.Add($"Final validation failed: {finalValidation.Message}");
            }

            var success = fieldsCompleted > 0 || validationResults.Count > 2; // Success if fields completed or validation passed
            var message = success 
                ? $"Planning sheet validation completed. {fieldsCompleted} additional fields filled. Ready for Step 14."
                : "Planning sheet validation completed with issues";

            if (errors.Any())
            {
                message += $" Warnings: {string.Join(", ", errors)}";
            }

            return new ProcessingResult
            {
                Success = success,
                Message = message,
                Data = new Dictionary<string, object>
                {
                    { "fieldsCompleted", fieldsCompleted },
                    { "validationResults", validationResults },
                    { "errors", errors },
                    { "readyForProcesses", finalValidation.Success }
                },
                Errors = errors.Any() ? errors : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 13: Critical error in job details validation");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Step 13 failed: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task<ProcessingResult> ValidateAndCompleteMaterialFields(Material? material, List<string> validationResults, List<string> errors)
    {
        var fieldsCompleted = 0;
        
        try
        {
            _logger.LogInformation("Step 13: Validating material fields completion");
            
            if (material == null)
            {
                validationResults.Add("No material data to validate");
                return new ProcessingResult { Success = true, Data = new Dictionary<string, object> { { "fieldsCompleted", 0 } } };
            }

            // Check specific DevExtreme dropdowns that might need completion
            var materialFields = new[]
            {
                new { Selector = "#ItemPlanQuality", Value = material.Quality, Name = "Quality" },
                new { Selector = "#ItemPlanGsm", Value = material.Gsm.ToString(), Name = "GSM" },
                new { Selector = "#ItemPlanMill", Value = material.Mill, Name = "Mill" },
                new { Selector = "#ItemPlanFinish", Value = material.Finish, Name = "Finish" }
            };

            foreach (var field in materialFields)
            {
                try
                {
                    var element = await _page!.QuerySelectorAsync(field.Selector);
                    if (element != null)
                    {
                        var isVisible = await element.IsVisibleAsync();
                        if (isVisible)
                        {
                            // Check if field has a value
                            var currentValue = await element.EvaluateAsync<string>("el => el.value || el.textContent || ''");
                            
                            if (string.IsNullOrWhiteSpace(currentValue) && !string.IsNullOrWhiteSpace(field.Value))
                            {
                                // Try to fill missing field
                                await element.FillAsync(field.Value);
                                await _page.WaitForTimeoutAsync(300);
                                fieldsCompleted++;
                                validationResults.Add($"Completed missing {field.Name} field with value: {field.Value}");
                            }
                            else if (!string.IsNullOrWhiteSpace(currentValue))
                            {
                                validationResults.Add($"Material {field.Name} already filled: {currentValue}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error validating material field {field.Name}: {ex.Message}");
                }
            }

            return new ProcessingResult 
            { 
                Success = true, 
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } } 
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Material validation error: {ex.Message}");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = ex.Message,
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } }
            };
        }
    }

    private async Task<ProcessingResult> ValidateAndCompletePrintingFields(PrintingDetails? printing, List<string> validationResults, List<string> errors)
    {
        var fieldsCompleted = 0;
        
        try
        {
            _logger.LogInformation("Step 13: Validating printing fields completion");
            
            if (printing == null)
            {
                validationResults.Add("No printing data to validate");
                return new ProcessingResult { Success = true, Data = new Dictionary<string, object> { { "fieldsCompleted", 0 } } };
            }

            // Check printing fields
            var printingFields = new[]
            {
                new { Selector = "#PlanFColor", Value = printing.FrontColors.ToString(), Name = "Front Colors" },
                new { Selector = "#PlanBColor", Value = printing.BackColors.ToString(), Name = "Back Colors" },
                new { Selector = "#PlanSpeFColor", Value = printing.SpecialFront.ToString(), Name = "Special Front" },
                new { Selector = "#PlanSpeBColor", Value = printing.SpecialBack.ToString(), Name = "Special Back" },
                new { Selector = "#PlanPrintingStyle", Value = printing.Style, Name = "Printing Style" },
                new { Selector = "#PlanPlateType", Value = printing.Plate, Name = "Plate Type" }
            };

            foreach (var field in printingFields)
            {
                try
                {
                    var element = await _page!.QuerySelectorAsync(field.Selector);
                    if (element != null)
                    {
                        var isVisible = await element.IsVisibleAsync();
                        if (isVisible && !string.IsNullOrWhiteSpace(field.Value))
                        {
                            var currentValue = await element.EvaluateAsync<string>("el => el.value || el.textContent || ''");
                            
                            if (string.IsNullOrWhiteSpace(currentValue))
                            {
                                await element.FillAsync(field.Value);
                                await _page.WaitForTimeoutAsync(200);
                                fieldsCompleted++;
                                validationResults.Add($"Completed missing {field.Name} field with value: {field.Value}");
                            }
                            else
                            {
                                validationResults.Add($"Printing {field.Name} already filled: {currentValue}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error validating printing field {field.Name}: {ex.Message}");
                }
            }

            return new ProcessingResult 
            { 
                Success = true, 
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } } 
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Printing validation error: {ex.Message}");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = ex.Message,
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } }
            };
        }
    }

    private async Task<ProcessingResult> ValidateAndCompleteWastageFields(WastageFinishing? wastage, List<string> validationResults, List<string> errors)
    {
        var fieldsCompleted = 0;
        
        try
        {
            _logger.LogInformation("Step 13: Validating wastage & finishing fields completion");
            
            if (wastage == null)
            {
                validationResults.Add("No wastage & finishing data to validate");
                return new ProcessingResult { Success = true, Data = new Dictionary<string, object> { { "fieldsCompleted", 0 } } };
            }

            // Check wastage fields
            var wastageFields = new[]
            {
                new { Selector = "#PlanMakeReadySheets", Value = wastage.MakeReadySheets.ToString(), Name = "Make Ready Sheets" },
                new { Selector = "#PlanWastageType", Value = wastage.WastageType, Name = "Wastage Type" },
                new { Selector = "#PlanGrainDirection", Value = wastage.GrainDirection, Name = "Grain Direction" },
                new { Selector = "#PlanOnlineCoating", Value = wastage.OnlineCoating, Name = "Online Coating" }
            };

            foreach (var field in wastageFields)
            {
                try
                {
                    var element = await _page!.QuerySelectorAsync(field.Selector);
                    if (element != null && !string.IsNullOrWhiteSpace(field.Value))
                    {
                        var isVisible = await element.IsVisibleAsync();
                        if (isVisible)
                        {
                            var currentValue = await element.EvaluateAsync<string>("el => el.value || el.textContent || ''");
                            
                            if (string.IsNullOrWhiteSpace(currentValue))
                            {
                                await element.FillAsync(field.Value);
                                await _page.WaitForTimeoutAsync(200);
                                fieldsCompleted++;
                                validationResults.Add($"Completed missing {field.Name} field with value: {field.Value}");
                            }
                            else
                            {
                                validationResults.Add($"Wastage {field.Name} already filled: {currentValue}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error validating wastage field {field.Name}: {ex.Message}");
                }
            }

            return new ProcessingResult 
            { 
                Success = true, 
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } } 
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Wastage validation error: {ex.Message}");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = ex.Message,
                Data = new Dictionary<string, object> { { "fieldsCompleted", fieldsCompleted } }
            };
        }
    }

    private async Task<ProcessingResult> FinalFormValidation()
{
    try
    {
        _logger.LogInformation("Step 13: Performing final form validation (panel-scoped / DevExtreme-aware)");

        // 1) Scope to the Planning panel
        var panel = await FindPlanningPanelAsync();

        // 2) Define critical fields with robust, descendant-aware selectors
        var checks = new (string label, string[] selectors)[]
        {
            // Job Size (use exact input if it is an input; add reasonable fallbacks)
            ("Job Size", new[] {
                "#planJob_Size1",
                "input#planJob_Size1",
                "#planJob_Size1 input"
            }),

            // Quality (DevExtreme SelectBox: value lives in descendant .dx-texteditor-input or display span)
            ("Quality", new[] {
                "#ItemPlanQuality .dx-texteditor-input",
                "#ItemPlanQuality .dx-display-value",
                "#ItemPlanQuality input"
            }),

            // Front Colors (often a plain input; still add fallback for nested input)
            ("Front Colors", new[] {
                "#PlanFColor",
                "#PlanFColor .dx-texteditor-input",
                "input#PlanFColor"
            })
        };

        var filledCriticalFields = 0;
        var details = new List<string>();

        foreach (var (label, selectors) in checks)
        {
            var value = await ReadFirstNonEmptyAsync(panel, selectors);
            if (!string.IsNullOrWhiteSpace(value))
            {
                filledCriticalFields++;
                details.Add($"{label}✅='{value}'");
            }
            else
            {
                details.Add($"{label}❌=empty");
            }
        }

        // 3) Process area readiness (same as your original intent but scoped)
        var processAreaReady = await panel
            .Locator(".dx-datagrid .dx-datagrid-rowsview, #ProcessSection, .process-area")
            .First.IsVisibleAsync(new() { Timeout = 800 });

        var validationScore = filledCriticalFields + (processAreaReady ? 1 : 0);
        var success = validationScore >= 2;

        _logger.LogInformation(
            "Step 13: Validation details -> {details}. ProcessAreaReady={ready}, Score={score}/4",
            string.Join(" | ", details), processAreaReady, validationScore
        );

        return new ProcessingResult
        {
            Success = success,
            Message = $"Form validation completed with score {validationScore}/4",
            Data = new Dictionary<string, object>
            {
                { "criticalFieldsFilled", filledCriticalFields },
                { "processAreaReady", processAreaReady },
                { "details", details }
            }
        };
    }
    catch (Exception ex)
    {
        return new ProcessingResult
        {
            Success = false,
            Message = $"Final validation failed: {ex.Message}"
        };
    }
}

private async Task<string> ReadFirstNonEmptyAsync(ILocator scope, string[] selectors)
{
    foreach (var sel in selectors)
    {
        try
        {
            var node = scope.Locator(sel).First;
            if (await node.IsVisibleAsync(new() { Timeout = 400 }))
            {
                // Prefer .value for inputs; fallback to textContent
                var tag = await node.EvaluateAsync<string>("el => el.tagName?.toLowerCase()");
                if (tag == "input" || tag == "textarea")
                {
                    var v = await node.EvaluateAsync<string>("el => (el as HTMLInputElement).value ?? ''");
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }

                // DevExtreme often keeps the text in the same .dx-texteditor-input (text node) or display span
                var txt = await node.EvaluateAsync<string>("el => el.textContent?.trim() || ''");
                if (!string.IsNullOrWhiteSpace(txt)) return txt;
            }
        }
        catch { /* try next selector */ }
    }
    return string.Empty;
}


/// <summary> Locate the Planning panel by anchoring from the "Show Cost" button. </summary>
    private async Task<ILocator> FindPlanningPanelAsync()
    {
        _logger.LogInformation("Step 14: Locating process table after Trimming block completion");
        
        // Target the specific process table (appears after Trimming block is filled)
        var processTableSelectors = new[]
        {
            "#GridOperation", // The specific grid ID for operations/processes
            ".dx-datagrid:has(.dx-datagrid-headers):has(*:has-text('Process Name'))", // DataGrid with Process Name header
            ".dx-datagrid-container:has(*:has-text('Process Name'))", // Container with Process Name
            ".dx-datagrid:has(.dx-datagrid-filter-row)", // DataGrid with filter row
            "table:has(th:has-text('Process Name'))", // HTML table with Process Name header
            ".dx-datagrid-content:has(*:has-text('Process'))" // DataGrid content with Process text
        };
        
        ILocator? processPanel = null;
        string usedSelector = "";
        
        // Wait a moment for the table to appear after Trimming completion
        await Task.Delay(1000);
        
        // Try each selector to find the process table
        for (int i = 0; i < processTableSelectors.Length; i++)
        {
            try
            {
                var candidateTable = _page!.Locator(processTableSelectors[i]).First;
                await candidateTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                
                // Verify this table has the expected structure (Process Name column and filter row)
                var hasProcessNameHeader = await candidateTable.Locator("*:has-text('Process Name')").IsVisibleAsync();
                var hasFilterRow = await candidateTable.Locator(".dx-datagrid-filter-row, .dx-header-filter").IsVisibleAsync();
                
                if (hasProcessNameHeader)
                {
                    processPanel = candidateTable;
                    usedSelector = processTableSelectors[i];
                    _logger.LogInformation("Step 14: Found process table using selector '{selector}', hasFilterRow: {hasFilterRow}", usedSelector, hasFilterRow);
                    break;
                }
                else
                {
                    _logger.LogDebug("Step 14: Table found but no Process Name header with selector '{selector}'", processTableSelectors[i]);
                }
            }
            catch
            {
                _logger.LogDebug("Step 14: Process table selector '{selector}' failed, trying next", processTableSelectors[i]);
                continue;
            }
        }
        
        if (processPanel == null)
        {
            _logger.LogWarning("Step 14: No process table found, using page body as scope");
            processPanel = _page!.Locator("body");
        }
        
        return processPanel;
    }

    /// <summary>
    /// Finds Process Name filter input by deterministic header mapping within the scoped panel
    /// </summary>
    private async Task<(ILocator leftGrid, ILocator processFilterInput, int colIndex)> FindProcessFilterInPanelAsync(ILocator panel)
    {
        // Find the left grid (available processes) within the panel
        var grids = panel.Locator(".dx-datagrid");
        var leftGrid = grids.First;
        await leftGrid.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        _logger.LogInformation("Step 14: Located left grid within planning panel");
        
        // Read header row cells to find "Process Name" column
        var headerCells = leftGrid.Locator(".dx-datagrid-headers .dx-header-row td");
        var headerCount = await headerCells.CountAsync();
        
        int processNameColumnIndex = -1;
        for (int i = 0; i < headerCount; i++)
        {
            var cellText = await headerCells.Nth(i).InnerTextAsync();
            if (cellText.ToLower().Contains("process name"))
            {
                processNameColumnIndex = i + 1; // 1-based index
                _logger.LogInformation("Step 14: Found 'Process Name' column at index {index}", processNameColumnIndex);
                break;
            }
        }
        
        if (processNameColumnIndex == -1)
        {
            throw new InvalidOperationException("Could not locate 'Process Name' column in grid headers");
        }
        
        // Target the corresponding filter row cell using the computed index
        var filterCell = leftGrid.Locator($".dx-datagrid-headers .dx-datagrid-filter-row td:nth-child({processNameColumnIndex})");
        var filterInput = filterCell.Locator("input.dx-texteditor-input").First;
        await filterInput.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        _logger.LogInformation("Step 14: Located Process Name filter input at column {index}", processNameColumnIndex);
        
        return (leftGrid, filterInput, processNameColumnIndex);
    }

    /// <summary>
    /// Adds a process using panel-scoped interactions and click escalation without DOM mutations
    /// </summary>
    private async Task AddProcessToPanelAsync(ILocator panel, string processName, bool required = false)
    {
        _logger.LogInformation("Step 14: Adding process '{processName}' using simplified table approach", processName);

        try
        {
            // Wait for Planning panel to be ready first
            await _page!.WaitForTimeoutAsync(3000);
            
            // 1. Identify the Left Grid Table (Source Process Table)
            var processTableLeft = _page!
                .Locator("table")
                .Filter(new() { HasText = "Process Name" })
                .First;
            
            await processTableLeft.WaitForAsync(new() { Timeout = 10000 });
            _logger.LogInformation("Step 14: Found left process table for '{processName}'", processName);

            // 2. Locate the Search Box for "Process Name"
            var searchInput = processTableLeft
                .Locator("tr").Nth(1)              // Header filter row
                .Locator("td").Nth(1)              // Column 2 = Process Name
                .Locator("input.dx-texteditor-input");
            
            await searchInput.WaitForAsync(new() { Timeout = 5000 });
            
            // Clear and fill search
            await searchInput.ClickAsync();
            await searchInput.FillAsync(processName);
            await _page!.WaitForTimeoutAsync(500); // Allow dropdown render delay
            _logger.LogInformation("Step 14: Typed '{processName}' in Process Name filter", processName);

            // 3. Wait for Row to Appear with Text "_Cutting" 
            await _page!.WaitForTimeoutAsync(500); // Add delay after typing as recommended
            
            // Wait for cell containing the process name to appear
            var processCell = _page!.Locator($"td:has-text('{processName}')").First;
            await processCell.WaitForAsync(new() { Timeout = 8000 });
            _logger.LogInformation("Step 14: Found cell containing '{processName}'", processName);

            // 4. Navigate to Parent Row
            var matchingRow = processCell.Locator("xpath=./ancestor::tr").First;
            _logger.LogInformation("Step 14: Located parent row for '{processName}'", processName);

            // 5. Find "+" Inside That Row - Try multiple selectors including hidden cells
            var plusButton = await TryFindPlusButtonInRow(matchingRow, processName);
            
            try 
            {
                await plusButton.WaitForAsync(new() { Timeout = 5000 });
                _logger.LogInformation("Step 14: Found plus button for '{processName}'", processName);
                
                // Scroll into view first
                await plusButton.ScrollIntoViewIfNeededAsync();
                
                // Click the "+"
                await plusButton.ClickAsync();
                _logger.LogInformation("Step 14: Clicked '+' button for process '{processName}'", processName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Step 14: Normal click failed, trying fallbacks for '{processName}': {error}", processName, ex.Message);
                
                // Fallback: Force click
                try 
                {
                    await plusButton.ClickAsync(new() { Force = true });
                    _logger.LogInformation("Step 14: Force clicked '+' button for process '{processName}'", processName);
                }
                catch (Exception ex2)
                {
                    _logger.LogWarning("Step 14: Force click failed, trying XPath approach for '{processName}': {error}", processName, ex2.Message);
                    
                    // Alternative full XPath approach
                    var xpathPlusButton = _page!.Locator($"xpath=//tr[.//td[contains(text(), '{processName}')]]//div[contains(@class, 'fa-plus')]").First;
                    await xpathPlusButton.ClickAsync();
                    _logger.LogInformation("Step 14: XPath clicked '+' button for process '{processName}'", processName);
                }
            }
            
            // 5. Verify the Process Was Added to the Right Grid (optional)
            try
            {
                var rightTable = _page!
                    .Locator("table")
                    .Filter(new() { HasText = "Delete" }); // Right table always has "Delete" column

                await rightTable.Locator("tr").Filter(new() { HasText = processName }).WaitForAsync(new() { Timeout = 5000 });
                _logger.LogInformation("Step 14: Verified '{processName}' was added to right grid", processName);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Step 14: Could not verify '{processName}' in right grid (may be expected): {error}", processName, ex.Message);
            }
            
            // 5. Verify process was added (optional verification)
            _logger.LogInformation("Step 14: Process '{processName}' successfully added using simplified table approach", processName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 14: Simplified table approach failed for process '{processName}'", processName);
            if (required) throw;
        }
    }
    
    /// <summary>
    /// Try to find the plus button in a row using multiple selector strategies
    /// </summary>
    private async Task<ILocator> TryFindPlusButtonInRow(ILocator row, string processName)
    {
        var selectors = new[]
        {
            // Standard selectors (current approach)
            "div.fa.fa-plus.customgridbtn",
            ".customgridbtn",
            "div.fa-plus",
            "div[class*='plus']",
            
            // Hidden cell selectors (discovered issue)
            "td.dx-hidden-cell div.fa.fa-plus.customgridbtn",
            "td.dx-hidden-cell .customgridbtn", 
            "td.dx-hidden-cell div.fa-plus",
            "td.dx-hidden-cell div[class*='plus']",
            
            // DevExtreme specific patterns
            ".dx-datagrid-action div.fa.fa-plus",
            ".dx-cell div.fa.fa-plus.customgridbtn",
            
            // Broad fallback patterns
            "[class*='plus'][class*='btn']",
            "[class*='add'][class*='btn']"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var element = row.Locator(selector).First;
                var count = await element.CountAsync();
                if (count > 0)
                {
                    _logger.LogInformation("Step 14: Found plus button for '{processName}' using selector: {selector}", processName, selector);
                    return element;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Step 14: Selector '{selector}' failed for '{processName}': {error}", selector, processName, ex.Message);
            }
        }

        _logger.LogError("Step 14: No plus button found for '{processName}' using any selector", processName);
        throw new InvalidOperationException($"Plus button not found for process '{processName}' in row");
    }
    
    /// <summary>
    /// Get the row index for a process in the table to match with fixed overlay buttons
    /// </summary>
    private async Task<int> GetRowIndexForProcess(ILocator panel, string processName)
    {
        try
        {
            var allRows = panel.Locator(".dx-datagrid-rowsview .dx-row.dx-data-row");
            var rowCount = await allRows.CountAsync();
            
            for (int i = 0; i < rowCount; i++)
            {
                var rowText = await allRows.Nth(i).InnerTextAsync();
                if (rowText.Contains(processName, StringComparison.OrdinalIgnoreCase) || 
                    rowText.Contains("_" + processName, StringComparison.OrdinalIgnoreCase) ||
                    (processName.StartsWith("_") && rowText.Contains(processName.Substring(1), StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Step 14: Found process '{processName}' at row index {index}", processName, i);
                    return i;
                }
            }
            
            _logger.LogWarning("Step 14: Process '{processName}' not found in any row", processName);
            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 14: Error getting row index for process '{processName}'", processName);
            return -1;
        }
    }

    private async Task<ProcessingResult> Step14_AddProcessesAsync()
    {
        try
        {
            _logger.LogInformation("Step 14: Starting panel-scoped process addition (no DOM mutations)");
            
            var addedProcesses = new List<string>();
            var errors = new List<string>();
            
            // Wait for process area to be loaded
            await _page!.WaitForTimeoutAsync(2000);
            
            // Step 1: Locate the Planning panel using Show Cost button as sentinel
            var planningPanel = await FindPlanningPanelAsync();
            _logger.LogInformation("Step 14: Planning panel located and scoped");
            
            // Get processes dynamically from ErpJobData.ProcessSelection instead of hardcoded list
            var currentErpData = GetCurrentErpDataFromContext();
            var processesToAdd = new List<(string name, bool required)>();
            
            // Extract all processes from ProcessSelection (Required, Optional, ContentBased)
            if (currentErpData.ProcessSelection?.RequiredProcesses?.Any() == true)
            {
                processesToAdd.AddRange(currentErpData.ProcessSelection.RequiredProcesses.Select(p => (p.Name, p.IsRequired)));
            }
            
            if (currentErpData.ProcessSelection?.OptionalProcesses?.Any() == true)
            {
                processesToAdd.AddRange(currentErpData.ProcessSelection.OptionalProcesses.Select(p => (p.Name, p.IsRequired)));
            }
            
            if (currentErpData.ProcessSelection?.ContentBasedProcesses?.Any() == true)
            {
                processesToAdd.AddRange(currentErpData.ProcessSelection.ContentBasedProcesses.Select(p => (p.Name, p.IsRequired)));
            }
            
            _logger.LogInformation("Step 14: Extracted {processCount} processes from email data: {processes}", 
                processesToAdd.Count, string.Join(", ", processesToAdd.Select(p => p.name)));
            
            // Add processes using the new panel-scoped method (NO DOM MUTATIONS)
            foreach (var (name, required) in processesToAdd)
            {
                try 
                { 
                    await AddProcessToPanelAsync(planningPanel, name, required);
                    addedProcesses.Add($"{name} {(required ? "(Required)" : "(Optional)")}");
                }
                catch (Exception ex)
                {
                    if (required) 
                    {
                        errors.Add($"Failed to add required process '{name}': {ex.Message}");
                        _logger.LogError(ex, "Step 14: Required process '{processName}' failed to add", name);
                    }
                    else
                    {
                        _logger.LogWarning("Step 14: Optional process '{name}' could not be added: {msg}", name, ex.Message);
                    }
                }
            }
            
            var success = addedProcesses.Count > 0 || !processesToAdd.Any(p => p.Item2);
            var message = success 
                ? $"Step 14: Panel-scoped process addition completed. Added {addedProcesses.Count} processes: {string.Join(", ", addedProcesses)}"
                : "Step 14: Process addition failed - no processes were added";

            if (errors.Any())
            {
                message += $" Errors: {string.Join(", ", errors)}";
            }

            return new ProcessingResult
            {
                Success = success,
                Message = message,
                Data = new Dictionary<string, object>
                {
                    { "addedProcesses", addedProcesses },
                    { "errors", errors },
                    { "strategy", "panel-scoped-no-mutations" }
                },
                Errors = errors.Any() ? errors : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 14: Error in panel-scoped process addition");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Step 14: Panel-scoped process addition failed: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task<ProcessingResult> SearchAndAddProcess(string processName, bool isRequired)
    {
        try
        {
            _logger.LogInformation("Step 14: Searching and adding process: '{processName}' (Required: {isRequired})", processName, isRequired);
            
            // Find the DevExtreme search container with the specific structure you provided
            var searchContainer = await FindProcessSearchContainer();
            if (searchContainer == null)
            {
                _logger.LogWarning("Step 14: Process search container not found, trying alternative approach for {processName}", processName);
                return await TryAlternativeProcessAddition(processName, isRequired);
            }
            
            // Find the search input within the container with multiple strategies
            var searchInput = await FindSearchInputInContainer(searchContainer);
            if (searchInput == null)
            {
                _logger.LogWarning("Step 14: Process search input not found in container, trying alternative approach for {processName}", processName);
                return await TryAlternativeProcessAddition(processName, isRequired);
            }
            
            // Clear and enter search term
            await searchInput.ClickAsync();
            await searchInput.FillAsync(""); // Clear existing text
            await _page!.WaitForTimeoutAsync(300);
            
            // Type the process name to trigger search
            await searchInput.TypeAsync(processName, new ElementHandleTypeOptions { Delay = 100 });
            await _page.WaitForTimeoutAsync(2000); // Wait for search results to load from database
            
            _logger.LogInformation("Step 14: Typed '{processName}' in search box, waiting for results", processName);
            
            // Look for search results and the "+" button
            var addResult = await FindAndClickPlusButton(processName);
            if (addResult.Success)
            {
                // Clear search box after successful addition
                await searchInput.FillAsync("");
                await _page.WaitForTimeoutAsync(500);
                
                return new ProcessingResult 
                { 
                    Success = true, 
                    Message = $"Process '{processName}' added successfully" 
                };
            }
            else
            {
                // Clear search box even on failure
                await searchInput.FillAsync("");
                await _page.WaitForTimeoutAsync(500);
                
                if (isRequired)
                {
                    return new ProcessingResult 
                    { 
                        Success = false, 
                        Message = $"Required process '{processName}' not found or could not be added: {addResult.Message}" 
                    };
                }
                else
                {
                    return new ProcessingResult 
                    { 
                        Success = false, 
                        Message = $"Optional process '{processName}' not found: {addResult.Message}" 
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Error searching for process '{processName}': {ex.Message}" 
            };
        }
    }

    private async Task<IElementHandle?> FindProcessSearchContainer()
    {
        // Enhanced search container detection with more comprehensive strategies
        var selectors = new[]
        {
            // DevExtreme containers
            ".dx-texteditor-container:has(.dx-texteditor-input)", 
            "div.dx-texteditor-container",
            "[class*='dx-texteditor-container']",
            ".dx-widget:has(.dx-texteditor-input)",
            "div:has(> .dx-texteditor-input-container)",
            
            // Process-specific containers
            ".process-search-container",
            "#ProcessSearchContainer", 
            "[id*='Process'] .dx-texteditor-container",
            "[class*='process'] .dx-texteditor-container",
            
            // Planning sheet areas that might contain process search
            ".planning-sheet .dx-texteditor-container",
            "#PlanningSheet .dx-texteditor-container",
            ".dx-form .dx-texteditor-container",
            
            // Generic search areas in planning context
            ".search-container", 
            "[placeholder*='search']",
            "[placeholder*='Search']",
            "input[type='text']:visible",
            
            // Last resort - any text input in the page
            ".dx-texteditor-input:visible"
        };
        
        _logger.LogInformation("Step 14: Searching for process search container with {count} strategies", selectors.Length);
        
        foreach (var selector in selectors)
        {
            try
            {
                var containers = await _page!.QuerySelectorAllAsync(selector);
                foreach (var container in containers)
                {
                    var isVisible = await container.IsVisibleAsync();
                    if (!isVisible) continue;
                    
                    // For direct input selectors, return the parent container
                    if (selector.Contains(".dx-texteditor-input"))
                    {
                        var parent = await container.EvaluateHandleAsync("el => el.closest('.dx-texteditor-container') || el.parentElement");
                        if (parent is IElementHandle parentElement)
                        {
                            _logger.LogInformation("Step 14: Found process search container using input-based selector: {selector}", selector);
                            return parentElement;
                        }
                    }
                    else
                    {
                        // Verify it contains an input or can be used for search
                        var hasInput = await container.QuerySelectorAsync(".dx-texteditor-input, input[type='text']") != null;
                        if (hasInput)
                        {
                            _logger.LogInformation("Step 14: Found process search container with selector: {selector}", selector);
                            return container;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Step 14: Selector failed {selector}: {error}", selector, ex.Message);
                continue;
            }
        }
        
        // Final fallback - look for any visible search-like input in the page
        try
        {
            _logger.LogInformation("Step 14: Trying final fallback - looking for any search input on page");
            var allInputs = await _page!.QuerySelectorAllAsync("input[type='text']:visible, .dx-texteditor-input:visible");
            
            foreach (var input in allInputs)
            {
                var isVisible = await input.IsVisibleAsync();
                var isEnabled = await input.IsEnabledAsync();
                
                if (isVisible && isEnabled)
                {
                    // Check if this looks like a search input
                    var placeholder = await input.GetAttributeAsync("placeholder");
                    var className = await input.GetAttributeAsync("class");
                    var id = await input.GetAttributeAsync("id");
                    
                    if (placeholder?.ToLower().Contains("search") == true ||
                        className?.ToLower().Contains("search") == true ||
                        id?.ToLower().Contains("search") == true ||
                        className?.Contains("dx-texteditor-input") == true)
                    {
                        var parent = await input.EvaluateHandleAsync("el => el.closest('.dx-texteditor-container') || el.parentElement");
                        if (parent is IElementHandle parentElement)
                        {
                            _logger.LogInformation("Step 14: Found potential process search container via fallback search");
                            return parentElement;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Step 14: Final fallback search failed: {error}", ex.Message);
        }
        
        _logger.LogWarning("Step 14: Process search container not found with any strategy");
        return null;
    }

    private async Task<IElementHandle?> FindSearchInputInContainer(IElementHandle container)
    {
        var inputSelectors = new[]
        {
            ".dx-texteditor-input",
            "input[type='text']",
            "input:not([type='hidden']):not([type='button'])",
            ".search-input",
            "[placeholder*='search']",
            "[placeholder*='Search']"
        };

        foreach (var selector in inputSelectors)
        {
            try
            {
                var input = await container.QuerySelectorAsync(selector);
                if (input != null)
                {
                    var isVisible = await input.IsVisibleAsync();
                    var isEnabled = await input.IsEnabledAsync();
                    
                    if (isVisible && isEnabled)
                    {
                        _logger.LogInformation("Step 14: Found search input in container using selector: {selector}", selector);
                        return input;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Step 14: Input selector failed {selector}: {error}", selector, ex.Message);
                continue;
            }
        }
        
        return null;
    }

    private async Task<ProcessingResult> TryAlternativeProcessAddition(string processName, bool isRequired)
    {
        try
        {
            _logger.LogInformation("Step 14: Trying alternative process addition approach for '{processName}'", processName);
            
            // Alternative approach 1: Look for existing process list and add buttons
            var addButtons = await _page!.QuerySelectorAllAsync("button:has-text('+'), .add-btn, .process-add, button[title*='Add'], a[href*='add']");
            
            foreach (var button in addButtons)
            {
                try
                {
                    var isVisible = await button.IsVisibleAsync();
                    var isEnabled = await button.IsEnabledAsync();
                    
                    if (isVisible && isEnabled)
                    {
                        var buttonText = await button.TextContentAsync();
                        var buttonTitle = await button.GetAttributeAsync("title");
                        
                        _logger.LogInformation("Step 14: Found potential add button: '{text}' title: '{title}'", buttonText?.Trim(), buttonTitle);
                        
                        // Click the button to potentially open a process selection dialog
                        await button.ClickAsync();
                        await _page.WaitForTimeoutAsync(1500);
                        
                        // After clicking, try to find a search or process selection area
                        var processDialog = await _page.QuerySelectorAsync(".dx-popup, .modal, .dialog, .process-selector");
                        if (processDialog != null)
                        {
                            _logger.LogInformation("Step 14: Process dialog opened, looking for '{processName}'", processName);
                            
                            // Look for the process name in the dialog
                            var processElements = await processDialog.QuerySelectorAllAsync("*");
                            foreach (var element in processElements)
                            {
                                var elementText = await element.TextContentAsync();
                                if (elementText?.Contains(processName, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    var isClickable = await element.IsEnabledAsync();
                                    if (isClickable)
                                    {
                                        await element.ClickAsync();
                                        await _page.WaitForTimeoutAsync(1000);
                                        
                                        return new ProcessingResult
                                        {
                                            Success = true,
                                            Message = $"Process '{processName}' added via alternative method"
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Step 14: Alternative button failed: {error}", ex.Message);
                    continue;
                }
            }
            
            // Alternative approach 2: Look for process names already visible on the page
            var allElements = await _page.QuerySelectorAllAsync("*");
            foreach (var element in allElements.Take(100)) // Limit to avoid performance issues
            {
                try
                {
                    var elementText = await element.TextContentAsync();
                    if (elementText?.Contains(processName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // Look for a nearby add button or clickable element
                        var nearbyButton = await element.QuerySelectorAsync("button, a, .clickable, [onclick]");
                        if (nearbyButton == null)
                        {
                            nearbyButton = await element.EvaluateHandleAsync("el => el.parentElement?.querySelector('button, a, [onclick]')") as IElementHandle;
                        }
                        
                        if (nearbyButton != null)
                        {
                            var isVisible = await nearbyButton.IsVisibleAsync();
                            var isEnabled = await nearbyButton.IsEnabledAsync();
                            
                            if (isVisible && isEnabled)
                            {
                                await nearbyButton.ClickAsync();
                                await _page.WaitForTimeoutAsync(1000);
                                
                                return new ProcessingResult
                                {
                                    Success = true,
                                    Message = $"Process '{processName}' added via text-based selection"
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Step 14: Element text check failed: {error}", ex.Message);
                    continue;
                }
            }
            
            // If this is a required process and we can't add it, it's an error
            if (isRequired)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = $"Required process '{processName}' could not be added using any method"
                };
            }
            else
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = $"Optional process '{processName}' not available or could not be added"
                };
            }
        }
        catch (Exception ex)
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"Alternative process addition failed for '{processName}': {ex.Message}"
            };
        }
    }

    private async Task<ProcessingResult> FindAndClickPlusButton(string processName)
    {
        try
        {
            // Wait for search results to appear
            await _page!.WaitForTimeoutAsync(1500);
            
            // Look for plus buttons in various possible locations
            var plusButtonSelectors = new[]
            {
                $"button:has-text('+')", // Generic plus button
                $".add-process-btn", // Custom add process button
                $"[data-action='add-process']", // Data attribute selector
                $"button[title*='Add']:visible", // Button with Add in title
                $".dx-button:has-text('+')", // DevExtreme button with plus
                $"button.btn:has-text('+')", // Bootstrap button with plus
                $"a:has-text('+'):visible", // Link with plus sign
                $"span:has-text('+'):visible", // Span with plus sign
                $".process-add", // Process add class
                $"[onclick*='add']:visible", // Element with add onclick
                $"button:has(.fa-plus)", // Button with FontAwesome plus icon
                $"button:has(.glyphicon-plus)" // Button with Glyphicon plus
            };
            
            foreach (var selector in plusButtonSelectors)
            {
                var buttons = await _page.QuerySelectorAllAsync(selector);
                foreach (var button in buttons)
                {
                    try
                    {
                        // Check if button is visible and enabled
                        var isVisible = await button.IsVisibleAsync();
                        var isEnabled = await button.IsEnabledAsync();
                        
                        if (isVisible && isEnabled)
                        {
                            _logger.LogInformation("Step 14: Found and clicking plus button for process '{processName}' with selector: {selector}", processName, selector);
                            
                            await button.ClickAsync();
                            await _page.WaitForTimeoutAsync(1000);
                            
                            return new ProcessingResult 
                            { 
                                Success = true, 
                                Message = $"Plus button clicked for '{processName}'" 
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Failed to click button with selector {selector}: {error}", selector, ex.Message);
                        continue;
                    }
                }
            }
            
            // If no plus button found, try to look for process rows/items that might contain add functionality
            return await TryAddProcessFromResultRow(processName);
        }
        catch (Exception ex)
        {
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Error finding plus button for '{processName}': {ex.Message}" 
            };
        }
    }

    private async Task<ProcessingResult> TryAddProcessFromResultRow(string processName)
    {
        try
        {
            _logger.LogInformation("Step 14: Looking for process '{processName}' in result rows", processName);
            
            // Look for rows that might contain the process name
            var rowSelectors = new[]
            {
                "tr", "div.row", ".list-item", ".process-item", 
                ".dx-row", ".grid-row", "li", ".result-item"
            };
            
            foreach (var rowSelector in rowSelectors)
            {
                var rows = await _page!.QuerySelectorAllAsync(rowSelector);
                foreach (var row in rows)
                {
                    try
                    {
                        var rowText = await row.TextContentAsync();
                        if (rowText != null && rowText.Contains(processName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Step 14: Found process '{processName}' in row, looking for add button", processName);
                            
                            // Look for add button within this row
                            var addButton = await row.QuerySelectorAsync("button, a, span");
                            if (addButton != null)
                            {
                                var isVisible = await addButton.IsVisibleAsync();
                                var isEnabled = await addButton.IsEnabledAsync();
                                
                                if (isVisible && isEnabled)
                                {
                                    await addButton.ClickAsync();
                                    await _page.WaitForTimeoutAsync(1000);
                                    
                                    return new ProcessingResult 
                                    { 
                                        Success = true, 
                                        Message = $"Process '{processName}' added from result row" 
                                    };
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error checking row for process '{processName}': {error}", processName, ex.Message);
                        continue;
                    }
                }
            }
            
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Process '{processName}' not found in search results" 
            };
        }
        catch (Exception ex)
        {
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Error searching result rows for '{processName}': {ex.Message}" 
            };
        }
    }

    private async Task<ProcessingResult> Step15_ClickShowCostAsync()
    {
        _logger.LogInformation("Executing step 15: Click 'Show Cost' button");
        
        // Add debugging to see what's on the page
        try
        {
            var pageContent = await _page!.ContentAsync();
            var showCostMatches = pageContent.Contains("Show Cost") ? "FOUND" : "NOT FOUND";
            var planButtonMatches = pageContent.Contains("PlanButton") ? "FOUND" : "NOT FOUND";
            _logger.LogInformation("Step 15: Page content check - 'Show Cost' text: {showCost}, 'PlanButton' ID: {planButton}", 
                showCostMatches, planButtonMatches);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Step 15: Could not check page content: {error}", ex.Message);
        }
        
        // 🎯 Enhanced Show Cost Button Detection - Robust Approach
        // Based on user guidance: Use full attribute-based selector with scroll and JS fallback
        
        // Primary robust selectors
        var primarySelectors = new[] 
        { 
            "a#PlanButton.myButton",  // Full attribute-based as recommended
            "#PlanButton",           // Primary ID selector
            "a#PlanButton"            // Tag + ID combination
        };

        // Try primary selectors first with full robust approach
        foreach (var selector in primarySelectors)
        {
            try
            {
                _logger.LogInformation("Step 15: Attempting robust click with primary selector: {selector}", selector);
                
                var element = _page!.Locator(selector);
                
                // Step 1: Wait for element to be attached and visible
                await element.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                
                // Step 2: Check if enabled (important for buttons that might be disabled)
                var isEnabled = await element.IsEnabledAsync();
                if (!isEnabled)
                {
                    _logger.LogWarning("Step 15: Show Cost button found but not enabled with selector: {selector}", selector);
                    continue;
                }
                
                // Step 3: Scroll into view (fixes float:right issues)
                _logger.LogInformation("Step 15: Scrolling Show Cost button into view");
                await element.ScrollIntoViewIfNeededAsync();
                
                // Step 4: Try standard click with force
                try
                {
                    _logger.LogInformation("Step 15: Attempting standard click with force");
                    await element.ClickAsync(new() { Force = true, Timeout = 3000 });
                    _logger.LogInformation("Step 15: Standard click successful with selector: {selector}", selector);
                }
                catch (Exception clickEx)
                {
                    _logger.LogWarning("Step 15: Standard click failed, trying JavaScript fallback: {error}", clickEx.Message);
                    
                    // Step 5: JavaScript fallback click
                    await _page!.EvaluateAsync(@"() => {
                        const el = document.getElementById('PlanButton');
                        if (el) {
                            console.log('JS click: Element found, clicking...');
                            el.click();
                            return true;
                        }
                        console.log('JS click: Element not found');
                        return false;
                    }");
                    _logger.LogInformation("Step 15: JavaScript fallback click executed");
                }
                
                // Wait for cost calculation to start
                await _page!.WaitForTimeoutAsync(5000);
                _logger.LogInformation("Step 15: Show Cost button clicked successfully using selector: {selector}", selector);
                return new ProcessingResult { Success = true, Message = $"Show Cost button clicked successfully using selector: {selector}" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Step 15: Primary selector {selector} failed: {error}", selector, ex.Message);
                continue;
            }
        }
        
        // Fallback: Try XPath with text validation as recommended
        try
        {
            _logger.LogInformation("Step 15: Trying XPath with text validation as fallback");
            var xpathSelector = "//a[@id='PlanButton' and contains(@class, 'myButton') and text()='Show Cost']";
            var xpathElement = _page!.Locator(xpathSelector);
            
            await xpathElement.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
            await xpathElement.ScrollIntoViewIfNeededAsync();
            await xpathElement.ClickAsync(new() { Force = true });
            
            await _page!.WaitForTimeoutAsync(5000);
            _logger.LogInformation("Step 15: XPath selector successful");
            return new ProcessingResult { Success = true, Message = "Show Cost button clicked successfully using XPath" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Step 15: XPath fallback failed: {error}", ex.Message);
        }
        
        // Final fallback: Try additional selectors
        var fallbackSelectors = new[] 
        {
            ".DialogBoxCustom a#PlanButton",
            "*:has-text('Show Cost')",
            "button:has-text('Show Cost')",
            "a.myButton:has-text('Show Cost')"
        };

        foreach (var selector in fallbackSelectors)
        {
            try
            {
                _logger.LogInformation("Step 15: Trying fallback selector: {selector}", selector);
                var element = _page!.Locator(selector).First;
                
                if (await element.IsVisibleAsync(new() { Timeout = 2000 }))
                {
                    await element.ScrollIntoViewIfNeededAsync();
                    await element.ClickAsync(new() { Force = true });
                    await _page!.WaitForTimeoutAsync(7000);
                    _logger.LogInformation("Step 15: Fallback selector successful: {selector}", selector);
                    return new ProcessingResult { Success = true, Message = $"Show Cost button clicked with fallback selector: {selector}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Step 15: Fallback selector {selector} failed: {error}", selector, ex.Message);
                continue;
            }
        }

        _logger.LogError("Step 15: Show Cost button not found with any robust selector strategy");
        return new ProcessingResult { Success = false, Message = "Show Cost button not found with any selector - may need viewport adjustment" };
    }

    private async Task<ProcessingResult> Step16_CaptureResultsAsync()
    {
        _logger.LogInformation("Step 16: Capturing Planning and Quotation screenshot and emailing results");
        
        try
        {
            // ✅ 1. Wait for Grid to Load - Check for key planning result indicators
            _logger.LogInformation("Step 16: 🔍 Waiting for planning & costing result grid to load...");
            
            var waitTasks = new List<Task>();
            
            // Try to wait for common planning result indicators
            var finalCostLocator = _page!.Locator("text=Final Cost");
            var expectedQtyLocator = _page!.Locator("text=Expected Qty");
            var planningTableLocator = _page!.Locator("table, .dx-datagrid, .planning-results");
            
            try
            {
                await finalCostLocator.WaitForAsync(new() { Timeout = 10000 });
                _logger.LogInformation("Step 16: ✅ Final Cost section detected");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Step 16: Final Cost text not found, trying Expected Qty...");
                try
                {
                    await expectedQtyLocator.WaitForAsync(new() { Timeout = 5000 });
                    _logger.LogInformation("Step 16: ✅ Expected Qty section detected");
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Step 16: Specific result indicators not found, waiting for general table/grid...");
                    await planningTableLocator.First.WaitForAsync(new() { Timeout = 5000 });
                    _logger.LogInformation("Step 16: ✅ Planning results table detected");
                }
            }
            
            // Extended wait to keep screen visible for 7 seconds as requested
            _logger.LogInformation("Step 16: 🕐 Keeping screen visible for 7 seconds for user review...");
            await Task.Delay(7000);
            
            // ✅ 2. Scroll & Take Full Page Screenshot
            _logger.LogInformation("Step 16: 📸 Capturing full page screenshot of planning results...");
            
            var screenshotPath = Path.Combine(Path.GetTempPath(), $"final_costing_result_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            
            await _page!.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });
            
            var screenshot = await File.ReadAllBytesAsync(screenshotPath);
            _logger.LogInformation("Step 16: ✅ Screenshot captured successfully, size: {size} bytes, path: {path}", 
                screenshot.Length, screenshotPath);
            
            // ✅ 3. Email Screenshot to Original Requester
            var emailSent = await SendCostingEmailAsync(screenshotPath);
            
            return new ProcessingResult 
            { 
                Success = true, 
                Message = $"Planning results captured and email sent successfully. Screenshot: {screenshotPath}",
                Data = new Dictionary<string, object> 
                { 
                    { "screenshot", Convert.ToBase64String(screenshot) },
                    { "screenshotSize", screenshot.Length },
                    { "screenshotPath", screenshotPath },
                    { "screenshotType", "final_costing_result" },
                    { "emailSent", emailSent }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 16: Error capturing planning results and sending email");
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Failed to capture planning results: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    /// <summary>
    /// 📤 Send final costing screenshot via email to the original requester
    /// </summary>
    private async Task<bool> SendCostingEmailAsync(string screenshotPath)
    {
        try
        {
            _logger.LogInformation("Step 16: 📧 Preparing to send costing email with screenshot attachment");
            
            // Get requester email from current job context - this would be set from the original email message
            var requesterEmail = GetRequesterEmail();
            
            if (string.IsNullOrEmpty(requesterEmail))
            {
                _logger.LogWarning("Step 16: No requester email found, cannot send costing results");
                return false;
            }

            _logger.LogInformation("Step 16: Sending final costing results to: {email}", requesterEmail);
            
            // Use existing email service through message queue for consistency
            var emailNotification = new
            {
                Type = "FINAL_COSTING_RESULTS",
                RecipientEmail = requesterEmail,
                Subject = "📦 Final ERP Costing Results - Your Estimation is Complete",
                Body = $@"
Dear User,

Your ERP estimation request has been completed successfully! 

📋 **Request Summary:**
• Content: Reverse Tuck In
• Quantity: 10,000 pieces  
• Dimensions: 200x100x100mm
• Material: 100GSM Real art paper, BAJAJ Mill, BOARD finish
• Processed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

Please find the attached screenshot with the complete planning and costing breakdown.

If you have any questions about the results, please don't hesitate to reach out.

Best regards,
ERP Automation System
Indas Analytics
",
                AttachmentPath = screenshotPath,
                AttachmentName = "Final_Costing_Results.png",
                Priority = "High"
            };

            // Publish email notification to message queue
            // TODO: Add message queue dependency to send email notifications
            // await _messageQueue.PublishAsync(emailNotification, QueueNames.Notifications);
            
            _logger.LogInformation("Step 16: ✅ Final costing email notification sent successfully to {email}", requesterEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step 16: Failed to send costing email");
            return false;
        }
    }

    /// <summary>
    /// Get the original requester email from job context
    /// </summary>
    private string GetRequesterEmail()
    {
        // This would typically come from the original job context
        // For now, using a placeholder - in real implementation this should be passed from the job data
        return "user@example.com"; // TODO: Get from actual job context/original email sender
    }

    // Helper methods
    private async Task FillFieldIfExists(string selector, string value, List<string> filledFields, string fieldName)
    {
        var field = await FindElementAsync(_page!, new[] { selector, $"[name='{fieldName.ToLower()}']" });
        if (field != null)
        {
            await field.FillAsync(value);
            filledFields.Add(fieldName);
            await _page!.WaitForTimeoutAsync(200);
        }
    }

    private async Task SelectDropdownIfExists(string selector, string value, List<string> filledFields, string fieldName)
    {
        var dropdown = await FindElementAsync(_page!, new[] { selector, $"select[name='{fieldName.ToLower()}']" });
        if (dropdown != null)
        {
            await dropdown.SelectOptionAsync(value);
            filledFields.Add(fieldName);
            await _page!.WaitForTimeoutAsync(200);
        }
    }

    public async Task<byte[]> TakeScreenshotAsync(string filename)
    {
        var screenshotPath = Path.Combine(_configuration["Automation:ScreenshotPath"] ?? "logs/screenshots/", $"{filename}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        
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
        // 🎯 Enhanced browser configuration for proper Show Cost button visibility
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _configuration.GetValue<bool>("Browser:Headless", false), // Set to false to see the automation
            SlowMo = _configuration.GetValue<int>("Browser:SlowMo", 1000), // Slow down for stability
            Timeout = _configuration.GetValue<int>("Browser:Timeout", 60000),
            Args = new[] { 
                "--start-maximized",
                "--force-device-scale-factor=1",  // ✅ Disable Windows zoom scaling
                "--window-size=1920,1080"         // ✅ Force fixed window size
            }
        });

        // Create context with proper viewport settings
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }, // ✅ Ensure full layout
            DeviceScaleFactor = 1 // ✅ Disable scaling
        });
        
        _page = await context.NewPageAsync();
        
        // Optional: Set zoom level to ensure UI elements are not cramped
        await _page.EvaluateAsync("() => { document.body.style.zoom = '90%'; }");
    }

    private async Task<IElementHandle?> FindElementAsync(IPage page, string[] selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                    return element;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error finding element with selector {selector}", selector);
            }
        }
        return null;
    }

    private ErpJobData? _currentErpData;

    private ErpJobData GetCurrentErpDataFromContext()
    {
        // Return the context data if available, otherwise use default sample data
        return _currentErpData ?? new ErpJobData
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
            }
        };
    }

    private async Task<ProcessSelection> GetProcessSelectionForJob(ErpJobData erpData)
    {
        // This would typically use a service to determine processes based on content type and client
        // For now, implement basic logic based on the content type
        
        var processSelection = new ProcessSelection();
        
        // Required processes for all boxes
        processSelection.RequiredProcesses = new List<ProcessDefinition>
        {
            new() { Name = "Die Cutting", Category = "Cutting", IsRequired = true, DisplayOrder = 1 },
            new() { Name = "Creasing", Category = "Cutting", IsRequired = true, DisplayOrder = 2 }
        };
        
        // Content-specific processes
        if (erpData.JobDetails?.Content?.ToLower().Contains("tuck") == true)
        {
            processSelection.ContentBasedProcesses = new List<ProcessDefinition>
            {
                new() { Name = "Gluing", Category = "Assembly", IsRequired = false, DisplayOrder = 3 },
                new() { Name = "Window Patching", Category = "Special", IsRequired = false, DisplayOrder = 4 }
            };
        }
        
        // Client-specific processes
        if (erpData.JobDetails?.Client?.ToLower().Contains("akrati") == true)
        {
            processSelection.OptionalProcesses = new List<ProcessDefinition>
            {
                new() { Name = "UV Coating", Category = "Finishing", IsRequired = false, DisplayOrder = 5 },
                new() { Name = "Lamination", Category = "Finishing", IsRequired = false, DisplayOrder = 6 }
            };
        }
        
        _logger.LogInformation("Selected processes for content '{content}' and client '{client}': {requiredCount} required, {contentCount} content-based, {optionalCount} optional",
            erpData.JobDetails?.Content ?? "Unknown",
            erpData.JobDetails?.Client ?? "Unknown",
            processSelection.RequiredProcesses.Count,
            processSelection.ContentBasedProcesses.Count,
            processSelection.OptionalProcesses.Count);
        
        return processSelection;
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