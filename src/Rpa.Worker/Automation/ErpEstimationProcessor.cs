using Microsoft.Playwright;
using Rpa.Core.Models;
using System.Text.Json;

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
                    return await Step12_NavigateToPlanningScreenAsync();
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
            new() { StepNumber = 12, Description = "Navigate to planning screen", Action = "planning_screen" },
            new() { StepNumber = 13, Description = "Fill job size, material, and printing details", Action = "fill_details" },
            new() { StepNumber = 14, Description = "Search and add processes", Action = "add_processes" },
            new() { StepNumber = 15, Description = "Click 'Show Cost' button", Action = "show_cost" },
            new() { StepNumber = 16, Description = "Take screenshot of costing results", Action = "capture_results" }
        };
    }

    // Step implementations
    private async Task<ProcessingResult> Step1_NavigateToErpAsync()
    {
        var erpUrl = _configuration["ERP:BaseUrl"] ?? "http://13.200.122.70/";
        await _page!.GotoAsync(erpUrl);
        await _page.WaitForTimeoutAsync(3000);
        
        return new ProcessingResult { Success = true, Message = $"Navigated to {erpUrl}" };
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

    private async Task<ProcessingResult> Step6_CloseQuotationPopupAsync()
    {
        // Look for the left arrow close button using the correct selector
        var closeButton = await FindElementAsync(_page!, new[] { 
            "span[onclick='closeNavLeft()']",
            ".fa-arrow-left",
            "i.fa.fa-arrow-left",
            "[onclick*='closeNavLeft']"
        });

        if (closeButton != null)
        {
            await closeButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(2000); // Wait for popup to close
            return new ProcessingResult { Success = true, Message = "Quotation Finalize popup closed" };
        }
        
        // If close button not found, try clicking the parent span
        var parentSpan = await FindElementAsync(_page!, new[] { 
            "span:has(.fa-arrow-left)",
            "span:has(i.fa.fa-arrow-left)"
        });
        
        if (parentSpan != null)
        {
            await parentSpan.ClickAsync();
            await _page!.WaitForTimeoutAsync(2000);
            return new ProcessingResult { Success = true, Message = "Quotation Finalize popup closed via parent span" };
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
                return new ProcessingResult { Success = false, Message = $"Browser context closed during content selection (attempt {attempt}): {ex.Message}" };
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
        
        // Direct selection strategies without search
        var directSelectors = new[]
        {
            $"#{content.Replace(" ", "")}",                               // #ReverseTuckIn
            $"[title='{content}']",                                       // [title='Reverse Tuck In']
            $"div.addcontentsize[title='{content}']",                     // div.addcontentsize[title='Reverse Tuck In']
            $"div.addcontentsize[id='{content.Replace(" ", "")}']",       // div.addcontentsize[id='ReverseTuckIn']
            $"#AllContents div[title='{content}']",                       // Within AllContents container
            $"#AllContents div[id='{content.Replace(" ", "")}']"          // Within AllContents container by ID
        };
        
        _logger.LogInformation("Step 10: Trying {count} direct selectors", directSelectors.Length);
        
        // Try each direct selector
        foreach (var selector in directSelectors)
        {
            var element = await FindElementWithRetryAsync(_page!, new[] { selector }, retryCount: 2, delayMs: 1000);
            if (element != null)
            {
                var result = await TrySelectAndConfirmElement(element, $"'{content}' using direct selector: {selector}");
                if (result.Success) return result;
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
            
            // Check ID attribute
            var idAttr = await item.GetAttributeAsync("id");
            if (idAttr != null && idAttr.Equals(content.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                var result = await TrySelectAndConfirmElement(item, $"'{content}' by ID attribute match");
                if (result.Success) return result;
            }
            
            // Check text content as last resort
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
            // Double-click to select content (this might close the modal automatically)
            await element.DblClickAsync();
            await _page!.WaitForTimeoutAsync(3000);
            
            // Check if modal closed automatically (which might mean success)
            var modalStillOpen = await _page.QuerySelectorAsync("#AllContents");
            if (modalStillOpen == null)
            {
                // Modal closed - check if content was added to the main page
                var contentAdded = await _page.QuerySelectorAsync("#Plan21, .planWindow, td[id*='ConRecord']");
                if (contentAdded != null)
                {
                    return new ProcessingResult { Success = true, Message = $"Content {description} selected successfully (modal auto-closed)" };
                }
            }
            
            // Verify page is still active
            if (_page.IsClosed)
            {
                return new ProcessingResult { Success = false, Message = "Page closed after double-click" };
            }
            
            // If modal is still open, try to find Select button
            if (modalStillOpen != null)
            {
                var selectButton = await FindElementWithRetryAsync(_page, new[] { 
                    "#Btn_Select_Content",
                    "a.myButton:has-text('Select')",
                    ".myButton:has-text('Select')",
                    "button:has-text('Select')"
                }, retryCount: 2, delayMs: 1000);
                
                if (selectButton != null)
                {
                    await selectButton.ClickAsync();
                    await _page.WaitForTimeoutAsync(3000);
                    return new ProcessingResult { Success = true, Message = $"Content {description} selected and confirmed" };
                }
                
                return new ProcessingResult { Success = false, Message = $"Content {description} double-clicked but Select button not found" };
            }
            
            // Modal closed but no content detected on main page
            return new ProcessingResult { Success = false, Message = $"Modal closed but content selection unclear for {description}" };
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

    private async Task<ProcessingResult> Step12_NavigateToPlanningScreenAsync()
    {
        // Wait for planning screen to load
        await _page!.WaitForTimeoutAsync(2000);
        
        // Verify we're on planning screen
        var planningElements = await _page.QuerySelectorAllAsync(".planning-screen, #planning, .plan-form");
        
        if (planningElements.Count > 0)
        {
            return new ProcessingResult { Success = true, Message = "Planning screen loaded successfully" };
        }

        return new ProcessingResult { Success = false, Message = "Planning screen not accessible" };
    }

    private async Task<ProcessingResult> Step13_FillJobDetailsAsync(ErpJobData erpData)
    {
        var filledFields = new List<string>();

        try
        {
            // Fill Job Size fields
            if (erpData.JobSize != null)
            {
                await FillFieldIfExists("#height", erpData.JobSize.Height.ToString(), filledFields, "Height");
                await FillFieldIfExists("#length", erpData.JobSize.Length.ToString(), filledFields, "Length");
                await FillFieldIfExists("#width", erpData.JobSize.Width.ToString(), filledFields, "Width");
                await FillFieldIfExists("#oflap", erpData.JobSize.OFlap.ToString(), filledFields, "O.flap");
                await FillFieldIfExists("#pflap", erpData.JobSize.PFlap.ToString(), filledFields, "P.flap");
            }

            // Fill Material fields
            if (erpData.Material != null)
            {
                await SelectDropdownIfExists("#quality", erpData.Material.Quality, filledFields, "Quality");
                await FillFieldIfExists("#gsm", erpData.Material.Gsm.ToString(), filledFields, "GSM");
                await SelectDropdownIfExists("#mill", erpData.Material.Mill, filledFields, "Mill");
                await SelectDropdownIfExists("#finish", erpData.Material.Finish, filledFields, "Finish");
            }

            // Fill Printing Details
            if (erpData.PrintingDetails != null)
            {
                await FillFieldIfExists("#frontColors", erpData.PrintingDetails.FrontColors.ToString(), filledFields, "Front Colors");
                await FillFieldIfExists("#backColors", erpData.PrintingDetails.BackColors.ToString(), filledFields, "Back Colors");
                await FillFieldIfExists("#specialFront", erpData.PrintingDetails.SpecialFront.ToString(), filledFields, "Special Front");
                await FillFieldIfExists("#specialBack", erpData.PrintingDetails.SpecialBack.ToString(), filledFields, "Special Back");
                await SelectDropdownIfExists("#style", erpData.PrintingDetails.Style, filledFields, "Style");
                await SelectDropdownIfExists("#plate", erpData.PrintingDetails.Plate, filledFields, "Plate");
            }

            return new ProcessingResult 
            { 
                Success = true, 
                Message = $"Filled {filledFields.Count} job detail fields",
                Data = new Dictionary<string, object> { { "filledFields", filledFields } }
            };
        }
        catch (Exception ex)
        {
            return new ProcessingResult 
            { 
                Success = false, 
                Message = $"Error filling job details: {ex.Message}",
                Data = new Dictionary<string, object> { { "filledFields", filledFields } }
            };
        }
    }

    private async Task<ProcessingResult> Step14_AddProcessesAsync()
    {
        // This step would involve searching for specific processes and adding them
        // For now, implement a basic version that looks for common process elements
        
        var processButtons = await _page!.QuerySelectorAllAsync(".process-add, .add-process, button:has-text('+')");
        var addedProcesses = 0;

        foreach (var button in processButtons.Take(5)) // Limit to 5 processes
        {
            try
            {
                await button.ClickAsync();
                await _page.WaitForTimeoutAsync(500);
                addedProcesses++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not click process button: {error}", ex.Message);
            }
        }

        return new ProcessingResult 
        { 
            Success = addedProcesses > 0, 
            Message = $"Added {addedProcesses} processes",
            Data = new Dictionary<string, object> { { "processesAdded", addedProcesses } }
        };
    }

    private async Task<ProcessingResult> Step15_ClickShowCostAsync()
    {
        var showCostButton = await FindElementAsync(_page!, new[] { 
            "button:has-text('Show Cost')", 
            "#showCost", 
            ".show-cost-button",
            "[data-action='show-cost']"
        });

        if (showCostButton != null)
        {
            await showCostButton.ClickAsync();
            await _page!.WaitForTimeoutAsync(5000); // Wait for cost calculation
            return new ProcessingResult { Success = true, Message = "Show Cost button clicked" };
        }

        return new ProcessingResult { Success = false, Message = "Show Cost button not found" };
    }

    private async Task<ProcessingResult> Step16_CaptureResultsAsync()
    {
        var screenshot = await TakeScreenshotAsync("costing_results");
        
        return new ProcessingResult 
        { 
            Success = true, 
            Message = "Costing results captured",
            Data = new Dictionary<string, object> 
            { 
                { "screenshot", Convert.ToBase64String(screenshot) },
                { "screenshotSize", screenshot.Length }
            }
        };
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
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _configuration.GetValue<bool>("Browser:Headless", false), // Set to false to see the automation
            SlowMo = _configuration.GetValue<int>("Browser:SlowMo", 1000), // Slow down for stability
            Timeout = _configuration.GetValue<int>("Browser:Timeout", 60000)
        });

        _page = await _browser.NewPageAsync();
        await _page.SetViewportSizeAsync(1920, 1080);
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