using Microsoft.Playwright;
using Rpa.Core.Models;
using System.Text.Json;

namespace Rpa.Worker.Automation;

public interface IWebsiteProcessor
{
    Task<ProcessingResult> ProcessJobCardEntryAsync(ExtractedCredentials credentials, JobCardInfo jobCardInfo);
    Task<ProcessingResult> LoginToSystemAsync(ExtractedCredentials credentials);
    Task<ProcessingResult> NavigateToModuleAsync(string moduleName);
    Task<ProcessingResult> FillFormAsync(Dictionary<string, object> formData);
    Task<ProcessingResult> ValidateEntryAsync();
}

public interface IAnomalyDetector
{
    Task<AnomalyDetectionResult> DetectAnomaliesAsync(Job job, ProcessingResult result);
}

public class WebsiteProcessor : IWebsiteProcessor, IDisposable
{
    private readonly ILogger<WebsiteProcessor> _logger;
    private readonly IConfiguration _configuration;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public WebsiteProcessor(ILogger<WebsiteProcessor> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ProcessingResult> ProcessJobCardEntryAsync(ExtractedCredentials credentials, JobCardInfo jobCardInfo)
    {
        try
        {
            await InitializeBrowserAsync();

            var loginResult = await LoginToSystemAsync(credentials);
            if (!loginResult.Success)
            {
                return loginResult;
            }

            var navigateResult = await NavigateToModuleAsync("job-card");
            if (!navigateResult.Success)
            {
                return navigateResult;
            }

            var formData = new Dictionary<string, object>
            {
                ["jobNumber"] = jobCardInfo.JobNumber ?? "",
                ["description"] = jobCardInfo.Description ?? "",
                ["customerName"] = jobCardInfo.CustomerName ?? "",
                ["estimatedCost"] = jobCardInfo.EstimatedCost ?? 0m
            };

            var fillResult = await FillFormAsync(formData);
            if (!fillResult.Success)
            {
                return fillResult;
            }

            var validateResult = await ValidateEntryAsync();
            return validateResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessJobCardEntryAsync");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Error processing job card entry: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
        finally
        {
            await CleanupAsync();
        }
    }

    public async Task<ProcessingResult> LoginToSystemAsync(ExtractedCredentials credentials)
    {
        try
        {
            if (_page == null || credentials.SystemUrl == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page not initialized or system URL is null"
                };
            }

            _logger.LogInformation("Navigating to login page: {url}", credentials.SystemUrl);
            await _page.GotoAsync(credentials.SystemUrl);

            await _page.WaitForTimeoutAsync(2000);

            var loginSelectors = new[]
            {
                "#username", "[name='username']", "[name='user']", "#user",
                "[placeholder*='username']", "[placeholder*='user']"
            };

            var passwordSelectors = new[]
            {
                "#password", "[name='password']", "[name='pass']", "#pass",
                "[placeholder*='password']", "[type='password']"
            };

            var loginButtonSelectors = new[]
            {
                "#login", "[type='submit']", "button[type='submit']",
                ".login-button", "#loginBtn", "[value='Login']"
            };

            var usernameField = await FindElementAsync(_page, loginSelectors);
            var passwordField = await FindElementAsync(_page, passwordSelectors);
            var loginButton = await FindElementAsync(_page, loginButtonSelectors);

            if (usernameField == null || passwordField == null || loginButton == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Could not find login form elements",
                    Errors = new List<string> { "Username field, password field, or login button not found" }
                };
            }

            await usernameField.FillAsync(credentials.Username ?? "");
            await passwordField.FillAsync(credentials.Password ?? "");
            
            await Task.Delay(500);
            
            await loginButton.ClickAsync();

            await _page.WaitForTimeoutAsync(3000);

            var currentUrl = _page.Url;
            if (currentUrl != credentials.SystemUrl && !currentUrl.Contains("login"))
            {
                _logger.LogInformation("Login successful, redirected to: {url}", currentUrl);
                return new ProcessingResult
                {
                    Success = true,
                    Message = "Login successful",
                    Data = new Dictionary<string, object> { { "redirectUrl", currentUrl } }
                };
            }
            else
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Login failed - still on login page or error occurred",
                    Errors = new List<string> { $"Current URL: {currentUrl}" }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login process");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Login error: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    public async Task<ProcessingResult> NavigateToModuleAsync(string moduleName)
    {
        try
        {
            if (_page == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page not initialized"
                };
            }

            var moduleSelectors = moduleName.ToLowerInvariant() switch
            {
                "job-card" => new[]
                {
                    "a[href*='job']", "a[href*='card']", ".job-card-link", 
                    "#jobCard", "[data-module='job-card']", "text=Job Card"
                },
                "costing" => new[]
                {
                    "a[href*='cost']", "a[href*='pricing']", ".costing-link",
                    "#costing", "[data-module='costing']", "text=Costing"
                },
                _ => new[]
                {
                    $"a[href*='{moduleName}']", $"#{moduleName}", $".{moduleName}-link"
                }
            };

            var moduleLink = await FindElementAsync(_page, moduleSelectors);
            if (moduleLink == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = $"Could not find {moduleName} module link",
                    Errors = new List<string> { $"Searched for selectors: {string.Join(", ", moduleSelectors)}" }
                };
            }

            await moduleLink.ClickAsync();
            await _page.WaitForTimeoutAsync(2000);

            return new ProcessingResult
            {
                Success = true,
                Message = $"Successfully navigated to {moduleName} module",
                Data = new Dictionary<string, object> { { "currentUrl", _page.Url } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to module {moduleName}", moduleName);
            return new ProcessingResult
            {
                Success = false,
                Message = $"Navigation error: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    public async Task<ProcessingResult> FillFormAsync(Dictionary<string, object> formData)
    {
        try
        {
            if (_page == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page not initialized"
                };
            }

            var filledFields = new List<string>();

            foreach (var (fieldName, value) in formData)
            {
                var fieldSelectors = new[]
                {
                    $"#{fieldName}", $"[name='{fieldName}']", $"[id*='{fieldName}']",
                    $"[name*='{fieldName}']", $"[placeholder*='{fieldName}']"
                };

                var field = await FindElementAsync(_page, fieldSelectors);
                if (field != null)
                {
                    var valueStr = value?.ToString() ?? "";
                    await field.FillAsync(valueStr);
                    filledFields.Add(fieldName);
                    _logger.LogDebug("Filled field {fieldName} with value {value}", fieldName, valueStr);
                }
                else
                {
                    _logger.LogWarning("Could not find field {fieldName}", fieldName);
                }

                await Task.Delay(200);
            }

            return new ProcessingResult
            {
                Success = filledFields.Count > 0,
                Message = $"Filled {filledFields.Count} out of {formData.Count} fields",
                Data = new Dictionary<string, object>
                {
                    { "filledFields", filledFields },
                    { "totalFields", formData.Count }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filling form");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Form filling error: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    public async Task<ProcessingResult> ValidateEntryAsync()
    {
        try
        {
            if (_page == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page not initialized"
                };
            }

            var saveButtonSelectors = new[]
            {
                "#save", "[type='submit']", ".save-button", "#saveBtn",
                "[value='Save']", "button:has-text('Save')", "text=Submit"
            };

            var saveButton = await FindElementAsync(_page, saveButtonSelectors);
            if (saveButton != null)
            {
                await saveButton.ClickAsync();
                await _page.WaitForTimeoutAsync(3000);

                var successIndicators = new[]
                {
                    ".success", ".alert-success", "#success-message",
                    "text=Success", "text=Saved", "text=Created"
                };

                var errorIndicators = new[]
                {
                    ".error", ".alert-error", "#error-message",
                    "text=Error", "text=Failed", ".validation-error"
                };

                var hasSuccess = await HasElementAsync(_page, successIndicators);
                var hasError = await HasElementAsync(_page, errorIndicators);

                if (hasSuccess)
                {
                    return new ProcessingResult
                    {
                        Success = true,
                        Message = "Entry validated and saved successfully"
                    };
                }
                else if (hasError)
                {
                    var errorText = await GetElementTextAsync(_page, errorIndicators);
                    return new ProcessingResult
                    {
                        Success = false,
                        Message = "Validation failed",
                        Errors = new List<string> { errorText ?? "Unknown validation error" }
                    };
                }
                else
                {
                    return new ProcessingResult
                    {
                        Success = true,
                        Message = "Entry submitted (no explicit validation feedback)"
                    };
                }
            }
            else
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Could not find save/submit button for validation"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during validation");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Validation error: {ex.Message}",
                Errors = new List<string> { ex.ToString() }
            };
        }
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            _playwright = await Playwright.CreateAsync();
            
            var browserType = _configuration["Browser:Type"]?.ToLowerInvariant() switch
            {
                "firefox" => _playwright.Firefox,
                "webkit" => _playwright.Webkit,
                _ => _playwright.Chromium
            };

            _browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _configuration.GetValue<bool>("Browser:Headless", true),
                SlowMo = _configuration.GetValue<int>("Browser:SlowMo", 0),
                Timeout = _configuration.GetValue<int>("Browser:Timeout", 30000)
            });

            _page = await _browser.NewPageAsync();
            await _page.SetViewportSizeAsync(1920, 1080);

            _logger.LogInformation("Browser initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing browser");
            throw;
        }
    }

    private async Task<IElementHandle?> FindElementAsync(IPage page, string[] selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    return element;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error finding element with selector {selector}", selector);
            }
        }
        return null;
    }

    private async Task<bool> HasElementAsync(IPage page, string[] selectors)
    {
        var element = await FindElementAsync(page, selectors);
        return element != null;
    }

    private async Task<string?> GetElementTextAsync(IPage page, string[] selectors)
    {
        var element = await FindElementAsync(page, selectors);
        return element != null ? await element.InnerTextAsync() : null;
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

public class AnomalyDetectorService : IAnomalyDetector
{
    private readonly ILogger<AnomalyDetectorService> _logger;

    public AnomalyDetectorService(ILogger<AnomalyDetectorService> logger)
    {
        _logger = logger;
    }

    public async Task<AnomalyDetectionResult> DetectAnomaliesAsync(Job job, ProcessingResult result)
    {
        var anomalies = new List<string>();

        if (job.CreatedAt < DateTime.UtcNow.AddDays(-7))
        {
            anomalies.Add("Job is older than 7 days");
        }

        if (result.Success && (result.Data?.Count ?? 0) == 0)
        {
            anomalies.Add("Successful result but no data returned");
        }

        if (!result.Success && (result.Errors?.Count ?? 0) == 0)
        {
            anomalies.Add("Failed result but no error details provided");
        }

        if (job.RetryCount > 2)
        {
            anomalies.Add($"High retry count: {job.RetryCount}");
        }

        return await Task.FromResult(new AnomalyDetectionResult
        {
            HasAnomalies = anomalies.Count > 0,
            Anomalies = anomalies,
            DetectedAt = DateTime.UtcNow
        });
    }
}

public class AnomalyDetectionResult
{
    public bool HasAnomalies { get; set; }
    public List<string> Anomalies { get; set; } = new();
    public DateTime DetectedAt { get; set; }
}