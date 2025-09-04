using Microsoft.Playwright;
using Rpa.Core.Models;
using System.Text.Json;

namespace Rpa.Worker.Automation;

public class PlanningSheetProcessor
{
    private readonly ILogger<PlanningSheetProcessor> _logger;
    private readonly IPage _page;
    private readonly string _screenshotDirectory;

    // Field mapping with primary ID, fallback selectors, and validation
    private readonly Dictionary<string, PlanningField> _fieldMappings = new()
    {
        // Job Size (Segment 1) - Always visible fields
        ["Height"] = new PlanningField
        {
            PrimarySelector = "#SizeHeight",
            FallbackSelectors = new[] { "[name='H']", "[placeholder*='Height']", "[title*='Height']" },
            ValidationPattern = @"^\d+$",
            IsRequired = true,
            Segment = "JobSize"
        },
        ["Length"] = new PlanningField
        {
            PrimarySelector = "#SizeLength", 
            FallbackSelectors = new[] { "[name='L']", "[placeholder*='Length']", "[title*='Length']" },
            ValidationPattern = @"^\d+$",
            IsRequired = true,
            Segment = "JobSize"
        },
        ["Width"] = new PlanningField
        {
            PrimarySelector = "#SizeWidth",
            FallbackSelectors = new[] { "[name='W']", "[placeholder*='Width']", "[title*='Width']" },
            ValidationPattern = @"^\d+$", 
            IsRequired = true,
            Segment = "JobSize"
        },
        ["OFlap"] = new PlanningField
        {
            PrimarySelector = "#SizeOpenflap",
            FallbackSelectors = new[] { "[name='OF']", "[placeholder*='Openflap']", "[title*='Openflap']" },
            ValidationPattern = @"^\d+$",
            IsRequired = true,
            Segment = "JobSize"
        },
        ["PFlap"] = new PlanningField
        {
            PrimarySelector = "#SizePastingflap", 
            FallbackSelectors = new[] { "[name='PF']", "[placeholder*='Pastingflap']", "[title*='Pastingflap']" },
            ValidationPattern = @"^\d+$",
            IsRequired = true,
            Segment = "JobSize"
        },
        
        // Dynamic fields (may be hidden based on content type)
        ["FoldedH"] = new PlanningField
        {
            PrimarySelector = "#JobFoldedH",
            FallbackSelectors = new[] { "[name='FH']", "[placeholder*='Folded H']" },
            IsConditional = true,
            Segment = "JobSize"
        },
        ["FoldedL"] = new PlanningField
        {
            PrimarySelector = "#JobFoldedL", 
            FallbackSelectors = new[] { "[name='FL']", "[placeholder*='Folded L']" },
            IsConditional = true,
            Segment = "JobSize"
        },
        
        // Printing Details (Segment 3) - Future enhancement fields
        ["PrintingStyle"] = new PlanningField
        {
            PrimarySelector = ".dx-texteditor-input[aria-haspopup='listbox'][data-dx_placeholder*='Printing Style']",
            FallbackSelectors = new[] { "[placeholder*='Printing Style']", ".dx-texteditor-input[role='combobox'][aria-expanded='false']" },
            IsDevExtremeDropdown = true,
            IsRequired = false,
            Segment = "PrintingDetails"
        },
        ["PlateType"] = new PlanningField
        {
            PrimarySelector = ".dx-texteditor-input[aria-haspopup='listbox'][data-dx_placeholder*='Select Plate Type']",
            FallbackSelectors = new[] { "[placeholder*='Plate Type']", ".dx-texteditor-input[aria-expanded='true'][aria-controls*='dx-']" },
            IsDevExtremeDropdown = true,
            IsRequired = false,
            Segment = "PrintingDetails"
        },
        
        // Machine Wastage (Segment 4) - Future enhancement fields  
        ["WastageType"] = new PlanningField
        {
            PrimarySelector = ".dx-texteditor-input[aria-haspopup='listbox'][data-dx_placeholder*='Select Type']",
            FallbackSelectors = new[] { "[placeholder*='Select Type']", ".dx-texteditor-input[aria-expanded='true'][aria-activedescendant*='dx-a79482a1']" },
            IsDevExtremeDropdown = true,
            IsRequired = false,
            Segment = "MachineWastage"
        },
        ["MakeReadyWastage"] = new PlanningField
        {
            PrimarySelector = "#PlanMakeReadyWastage",
            FallbackSelectors = new[] { "[placeholder*='Enter Qty']", "input[title*='Make Ready Wastage']" },
            ValidationPattern = @"^\d+$",
            IsRequired = false,
            Segment = "MachineWastage"
        },
        ["GrainDirection"] = new PlanningField
        {
            PrimarySelector = ".dx-texteditor-input[aria-haspopup='listbox'][data-dx_placeholder*='Grain Direction']",
            FallbackSelectors = new[] { "[placeholder*='Grain Direction']", ".dx-texteditor-input[aria-expanded='true'][aria-activedescendant*='dx-e75b949b']" },
            IsDevExtremeDropdown = true,
            IsRequired = false,
            Segment = "MachineWastage"
        },
        ["OnlineCoating"] = new PlanningField
        {
            PrimarySelector = ".dx-texteditor-input[aria-haspopup='listbox'][data-dx_placeholder*='Select coating']",
            FallbackSelectors = new[] { "[placeholder*='coating']", ".dx-texteditor-input[aria-expanded='true'][aria-activedescendant*='dx-3ca649b7']" },
            IsDevExtremeDropdown = true,
            IsRequired = false,
            Segment = "MachineWastage"
        }
    };

    public PlanningSheetProcessor(ILogger<PlanningSheetProcessor> logger, IPage page, string? screenshotDirectory = null)
    {
        _logger = logger;
        _page = page;
        _screenshotDirectory = screenshotDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "logs", "screenshots");
    }

    public async Task<ProcessingResult> FillPlanningSheetAsync(ErpJobData erpData)
    {
        var result = new ProcessingResult { Success = true };
        var filledFields = new List<string>();
        var errors = new List<string>();

        try
        {
            _logger.LogInformation("Starting Planning Sheet form filling process");

            // Check if page is still active before proceeding
            if (_page == null || _page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Page context is closed or invalid",
                    Errors = new List<string> { "Browser page is no longer active" }
                };
            }

            // Wait for planning sheet to load completely with better error handling
            try
            {
                await _page.WaitForSelectorAsync("#planJob_Size1", new PageWaitForSelectorOptions 
                { 
                    Timeout = 15000 // Increased timeout
                });
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Planning sheet selector #planJob_Size1 not found, trying alternative selectors");
                
                // Try alternative selectors for planning sheet
                var alternativeSelectors = new[] { "#planJob_Size", ".planning-form", "#PlanningForm", ".dx-form" };
                var found = false;
                
                foreach (var selector in alternativeSelectors)
                {
                    try
                    {
                        await _page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = 3000 });
                        _logger.LogInformation("Found planning sheet using alternative selector: {selector}", selector);
                        found = true;
                        break;
                    }
                    catch
                    {
                        continue;
                    }
                }
                
                if (!found)
                {
                    return new ProcessingResult
                    {
                        Success = false,
                        Message = "Planning sheet form not found - page may not have loaded correctly",
                        Errors = new List<string> { "Unable to locate planning sheet form elements" }
                    };
                }
            }

            // Segment 1: Job Size (Critical - Always Required)
            _logger.LogInformation("=== Starting Segment 1: Job Size ===");
            try
            {
                await FillJobSizeSegment(erpData, filledFields, errors);
                _logger.LogInformation("✅ Segment 1 (Job Size) completed successfully. Fields filled: {count}", filledFields.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in Job Size segment");
                errors.Add($"Job Size segment failed: {ex.Message}");
            }

            // Check page is still active before continuing
            if (_page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Browser closed unexpectedly during Job Size processing",
                    Errors = errors,
                    Data = new Dictionary<string, object> { { "filledFields", filledFields } }
                };
            }

            // Segment 2: Raw Material (if data available)  
            _logger.LogInformation("=== Starting Segment 2: Raw Material ===");
            if (erpData.Material != null)
            {
                try
                {
                    // Check if browser is still active before starting Raw Material processing
                    if (_page.IsClosed)
                    {
                        errors.Add("Browser closed before Raw Material segment");
                        _logger.LogWarning("❌ Browser closed before Raw Material segment");
                    }
                    else
                    {
                        _logger.LogInformation("Processing Material: Quality={quality}, GSM={gsm}, Mill={mill}, Finish={finish}", 
                            erpData.Material.Quality, erpData.Material.Gsm, erpData.Material.Mill, erpData.Material.Finish);
                        await FillRawMaterialSegment(erpData.Material, filledFields, errors);
                        _logger.LogInformation("✅ Segment 2 (Raw Material) completed successfully. Total fields: {count}", filledFields.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in Raw Material segment - continuing with fallback");
                    errors.Add($"Raw Material segment failed: {ex.Message}");
                    
                    // Don't fail the entire process, just log and continue
                    _logger.LogInformation("➡️ Continuing workflow despite Raw Material segment issues");
                }
            }
            else
            {
                _logger.LogInformation("⚠️ No material data available, skipping Raw Material segment");
            }

            // Check page is still active
            if (_page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Browser closed unexpectedly during Raw Material processing",
                    Errors = errors,
                    Data = new Dictionary<string, object> { { "filledFields", filledFields } }
                };
            }

            // Segment 3: Printing Details (if data available)
            if (erpData.PrintingDetails != null) 
            {
                try
                {
                    await FillPrintingDetailsSegment(erpData.PrintingDetails, filledFields, errors);
                    _logger.LogInformation("Printing Details segment completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Printing Details segment");
                    errors.Add($"Printing Details segment failed: {ex.Message}");
                }
            }

            // Check page is still active
            if (_page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Browser closed unexpectedly during Printing Details processing",
                    Errors = errors,
                    Data = new Dictionary<string, object> { { "filledFields", filledFields } }
                };
            }

            // Segment 4: Wastage & Finishing (if data available)
            if (erpData.WastageFinishing != null)
            {
                try
                {
                    await FillWastageFinishingSegment(erpData.WastageFinishing, filledFields, errors);
                    _logger.LogInformation("Wastage & Finishing segment completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Wastage & Finishing segment");
                    errors.Add($"Wastage & Finishing segment failed: {ex.Message}");
                }
            }

            // Segment 4B: Finishing Fields (Trimming, Gripper, etc.)
            if (erpData.FinishingFields != null)
            {
                try
                {
                    await FillFinishingFieldsSegment(erpData.FinishingFields, filledFields, errors);
                    _logger.LogInformation("Finishing Fields segment completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Finishing Fields segment");
                    errors.Add($"Finishing Fields segment failed: {ex.Message}");
                }
            }

            // Check page is still active
            if (_page.IsClosed)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = "Browser closed unexpectedly during Wastage & Finishing processing",
                    Errors = errors,
                    Data = new Dictionary<string, object> { { "filledFields", filledFields } }
                };
            }

            // Segment 5: Process Details (Dynamic - Add processes)
            _logger.LogInformation("=== Starting Segment 5: Process Details ===");
            try
            {
                await FillProcessDetailsSegment(filledFields, errors);
                _logger.LogInformation("✅ Segment 5 (Process Details) completed successfully. Total fields: {count}", filledFields.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in Process Details segment");
                errors.Add($"Process Details segment failed: {ex.Message}");
            }

            // Final result compilation
            _logger.LogInformation("Planning Sheet processing completed. Fields filled: {filledCount}, Errors: {errorCount}", 
                filledFields.Count, errors.Count);

            var success = filledFields.Count > 0; // Success if at least some fields were filled
            result.Success = success;
            result.Message = success 
                ? $"Planning sheet completed successfully. {filledFields.Count} fields filled, {errors.Count} errors."
                : $"Planning sheet failed. {errors.Count} errors occurred.";
                
            result.Data = new Dictionary<string, object>
            {
                { "filledFields", filledFields },
                { "errors", errors },
                { "totalFields", filledFields.Count },
                { "segmentsProcessed", new[] { "JobSize", "RawMaterial", "Printing", "WastageFinishing", "ProcessDetails" } }
            };

            if (errors.Any())
            {
                result.Errors = errors;
                _logger.LogWarning("Planning sheet completed with errors: {errors}", string.Join("; ", errors));
            }
            else
            {
                _logger.LogInformation("Planning sheet completed successfully with no errors");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in Planning Sheet processing");
            return new ProcessingResult
            {
                Success = false,
                Message = $"Planning Sheet processing failed: {ex.Message}",
                Errors = new List<string> { ex.ToString() },
                Data = new Dictionary<string, object> { { "filledFields", filledFields } }
            };
        }
    }

    private async Task FillJobSizeSegment(ErpJobData erpData, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("Filling Job Size segment (Segment 1)");

        if (erpData.JobSize == null)
        {
            errors.Add("Job Size data is missing from email");
            return;
        }

        var jobSize = erpData.JobSize;
        
        // Fill required Job Size fields with robust error handling
        await FillFieldWithValidation("Height", jobSize.Height.ToString(), filledFields, errors);
        await FillFieldWithValidation("Length", jobSize.Length.ToString(), filledFields, errors);
        await FillFieldWithValidation("Width", jobSize.Width.ToString(), filledFields, errors);
        await FillFieldWithValidation("OFlap", jobSize.OFlap.ToString(), filledFields, errors);
        await FillFieldWithValidation("PFlap", jobSize.PFlap.ToString(), filledFields, errors);

        // Check for conditional fields (depends on content type)
        await FillConditionalFields(jobSize, filledFields, errors);

        // Fill Job Size textarea summary
        var jobSizeSummary = $"H:{jobSize.Height}mm, L:{jobSize.Length}mm, W:{jobSize.Width}mm, OF:{jobSize.OFlap}mm, PF:{jobSize.PFlap}mm";
        await FillTextArea("#JobPrePlan", jobSizeSummary, "Job Size Summary", filledFields, errors);
    }

    private async Task FillFieldWithValidation(string fieldName, string value, List<string> filledFields, List<string> errors)
    {
        if (!_fieldMappings.TryGetValue(fieldName, out var fieldConfig))
        {
            errors.Add($"Field configuration not found for: {fieldName}");
            return;
        }

        try
        {
            // Primary selector attempt
            var element = await _page.QuerySelectorAsync(fieldConfig.PrimarySelector);
            
            // Fallback selector attempts
            if (element == null)
            {
                foreach (var fallbackSelector in fieldConfig.FallbackSelectors)
                {
                    element = await _page.QuerySelectorAsync(fallbackSelector);
                    if (element != null)
                    {
                        _logger.LogInformation("Found {fieldName} using fallback selector: {selector}", fieldName, fallbackSelector);
                        break;
                    }
                }
            }

            if (element == null)
            {
                var errorMsg = $"Field not found: {fieldName} (tried {fieldConfig.FallbackSelectors.Length + 1} selectors)";
                if (fieldConfig.IsRequired)
                {
                    errors.Add(errorMsg);
                }
                else
                {
                    _logger.LogWarning(errorMsg);
                }
                return;
            }

            // Check if field is visible and enabled
            var isVisible = await element.IsVisibleAsync();
            var isEnabled = await element.IsEnabledAsync();

            if (!isVisible)
            {
                _logger.LogWarning("Field {fieldName} is not visible, skipping", fieldName);
                return;
            }

            if (!isEnabled)
            {
                errors.Add($"Field {fieldName} is disabled and cannot be filled");
                return;
            }

            // Validate value format if pattern is specified
            if (!string.IsNullOrEmpty(fieldConfig.ValidationPattern))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, fieldConfig.ValidationPattern))
                {
                    errors.Add($"Invalid value format for {fieldName}: {value}");
                    return;
                }
            }

            // Clear and fill field
            await element.FillAsync(value);
            
            // Verify the value was set correctly
            var filledValue = await element.GetAttributeAsync("value") ?? await element.InputValueAsync();
            if (filledValue == value)
            {
                filledFields.Add($"{fieldName} = {value}");
                _logger.LogInformation("Successfully filled {fieldName} = {value}", fieldName, value);
            }
            else
            {
                errors.Add($"Field {fieldName} value verification failed. Expected: {value}, Got: {filledValue}");
            }

            // Add small delay for UI stability
            await _page.WaitForTimeoutAsync(200);
        }
        catch (Exception ex)
        {
            errors.Add($"Error filling {fieldName}: {ex.Message}");
            _logger.LogError(ex, "Error filling field {fieldName}", fieldName);
        }
    }

    private async Task FillConditionalFields(JobSize jobSize, List<string> filledFields, List<string> errors)
    {
        // Check if folded fields are visible (content-type dependent)
        var foldedHField = await _page.QuerySelectorAsync("#JobFoldedH");
        if (foldedHField != null && await foldedHField.IsVisibleAsync())
        {
            await FillFieldWithValidation("FoldedH", jobSize.Height.ToString(), filledFields, errors);
            await FillFieldWithValidation("FoldedL", jobSize.Length.ToString(), filledFields, errors);
        }

        // Check for other conditional fields based on content type
        // This would expand based on your specific content types
        await CheckAndFillConditionalField("#SizeCenterSeal", "0", "Center Seal", filledFields, errors);
        await CheckAndFillConditionalField("#SizeSideSeal", "0", "Side Seal", filledFields, errors);
    }

    private async Task CheckAndFillConditionalField(string selector, string defaultValue, string fieldName, List<string> filledFields, List<string> errors)
    {
        var element = await _page.QuerySelectorAsync(selector);
        if (element != null && await element.IsVisibleAsync())
        {
            try
            {
                await element.FillAsync(defaultValue);
                filledFields.Add($"{fieldName} = {defaultValue}");
            }
            catch (Exception ex)
            {
                errors.Add($"Error filling conditional field {fieldName}: {ex.Message}");
            }
        }
    }

    private async Task FillTextArea(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            var element = await _page.QuerySelectorAsync(selector);
            if (element != null && await element.IsVisibleAsync())
            {
                await element.FillAsync(value);
                filledFields.Add($"{fieldName} = {value}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error filling {fieldName}: {ex.Message}");
        }
    }

    private async Task FillRawMaterialSegment(Material material, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("Filling Raw Material segment (Segment 2) with conservative approach");
        
        try 
        {
            // Process each field individually with page state checks
            var materialFields = new[]
            {
                new { Selector = "#ItemPlanQuality", Value = material.Quality, Name = "Item Quality", IsDropdown = true },
                new { Selector = "#ItemPlanGsm", Value = material.Gsm.ToString(), Name = "GSM", IsDropdown = true },
                new { Selector = "#ItemPlanMill", Value = material.Mill, Name = "Mill", IsDropdown = true },
                new { Selector = "#ItemPlanFinish", Value = material.Finish, Name = "Finish", IsDropdown = true }
            };

            foreach (var field in materialFields)
            {
                try
                {
                    // Check if page is still active before each field
                    if (_page.IsClosed)
                    {
                        _logger.LogWarning("Page closed during material field processing at {fieldName}", field.Name);
                        errors.Add($"Page closed during {field.Name} processing");
                        break;
                    }

                    _logger.LogInformation("🔍 Processing material field: {fieldName} = {value} (Dropdown: {isDropdown})", field.Name, field.Value, field.IsDropdown);
                    
                    if (field.IsDropdown)
                    {
                        await FillDevExtremeDropdown(field.Selector, field.Value, field.Name, filledFields, errors);
                    }
                    else
                    {
                        await FillInputField(field.Selector, field.Value, field.Name, filledFields, errors);
                    }
                    
                    _logger.LogInformation("✅ Completed processing {fieldName}. Current filled fields: {count}", field.Name, filledFields.Count);
                    
                    // Small delay between fields to prevent overwhelming
                    await _page.WaitForTimeoutAsync(500);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error processing material field {fieldName}: {error}", field.Name, ex.Message);
                    errors.Add($"Material field {field.Name} failed: {ex.Message}");
                    // Continue with next field instead of failing entire segment
                }
            }

            // Paper Trimming fields (simpler approach)
            if (!_page.IsClosed && !string.IsNullOrEmpty(material.Quality))
            {
                try
                {
                    _logger.LogInformation("Processing paper trimming fields");
                    var trimmingParts = material.Quality.ToLower().Contains("trim") ? new[] {"3", "3", "3", "3"} : new[] {"0", "0", "0", "0"};
                    
                    var trimmingFields = new[]
                    {
                        new { Selector = "#PaperTrimtop", Value = trimmingParts[0], Name = "Paper Trim Top" },
                        new { Selector = "#PaperTrimbottom", Value = trimmingParts[1], Name = "Paper Trim Bottom" },
                        new { Selector = "#PaperTrimleft", Value = trimmingParts[2], Name = "Paper Trim Left" },
                        new { Selector = "#PaperTrimright", Value = trimmingParts[3], Name = "Paper Trim Right" }
                    };

                    foreach (var trimmingField in trimmingFields)
                    {
                        if (_page.IsClosed) break;
                        
                        try
                        {
                            await FillInputField(trimmingField.Selector, trimmingField.Value, trimmingField.Name, filledFields, errors);
                            await _page.WaitForTimeoutAsync(200);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Error filling trimming field {fieldName}: {error}", trimmingField.Name, ex.Message);
                            errors.Add($"Trimming field {trimmingField.Name} failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error processing trimming fields: {error}", ex.Message);
                    errors.Add($"Trimming fields failed: {ex.Message}");
                }
            }
            
        }
        catch (Exception ex)
        {
            errors.Add($"Critical error in Raw Material segment: {ex.Message}");
            _logger.LogError(ex, "Critical error filling Raw Material segment");
            throw; // Re-throw critical errors
        }
    }

    private async Task FillPrintingDetailsSegment(PrintingDetails printing, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("Filling Printing Details segment (Segment 3)");
        
        try 
        {
            // Printing Colors - Regular input fields
            await FillInputField("#PlanFColor", printing.FrontColors.ToString(), "Front Colors", filledFields, errors);
            await FillInputField("#PlanBColor", printing.BackColors.ToString(), "Back Colors", filledFields, errors);
            await FillInputField("#PlanSpeFColor", printing.SpecialFront.ToString(), "Special Front Colors", filledFields, errors);
            await FillInputField("#PlanSpeBColor", printing.SpecialBack.ToString(), "Special Back Colors", filledFields, errors);
            
            // Printing Style - DevExtreme dropdown
            await FillDevExtremeDropdown("#PlanPrintingStyle", printing.Style, "Printing Style", filledFields, errors);
            
            // Plate Type - DevExtreme dropdown (already has CTP Plate selected)
            await FillDevExtremeDropdown("#PlanPlateType", printing.Plate, "Plate Type", filledFields, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Printing Details segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Printing Details segment");
        }
    }

    private async Task FillWastageFinishingSegment(WastageFinishing wastage, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("Filling Wastage & Finishing segment (Segment 4) - Machine Wastage");
        
        try
        {
            // Make Ready Wastage - Regular input field
            await FillInputField("#PlanMakeReadyWastage", wastage.MakeReadySheets.ToString(), "Make Ready Waste", filledFields, errors);
            
            // Wastage Type - DevExtreme dropdown
            await FillDevExtremeDropdown("#PlanWastageType", wastage.WastageType, "Wastage Type", filledFields, errors);
            
            // Grain Direction - DevExtreme dropdown
            await FillDevExtremeDropdown("#PlanPrintingGrain", wastage.GrainDirection, "Grain Direction", filledFields, errors);
            
            // Online Coating - DevExtreme dropdown
            await FillDevExtremeDropdown("#PlanOnlineCoating", wastage.OnlineCoating, "Online Coating", filledFields, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Wastage & Finishing segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Wastage & Finishing segment");
        }
    }

    private async Task FillTrimmingFields(string trimmingValue, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("🔧 === FILLING TRIMMING FIELDS (T/B/L/R) ===");
        
        // Add a pause for visibility
        await _page.WaitForTimeoutAsync(1000);
        
        // Parse T/B/L/R format or use default "0/0/0/0"
        var values = string.IsNullOrEmpty(trimmingValue) ? new[] {"0", "0", "0", "0"} : trimmingValue.Split('/');
        if (values.Length != 4) values = new[] {"0", "0", "0", "0"};
        
        _logger.LogInformation("📋 Trimming values to fill: Top={top}, Bottom={bottom}, Left={left}, Right={right}", 
            values[0], values[1], values[2], values[3]);
        
        // Enhanced Trimming fields with scroll and visibility
        _logger.LogInformation("🎯 WATCH BROWSER! Filling Trimming Top with value: {value}", values[0]);
        await ScrollToElementAndFill("#Trimmingtop", values[0], "Trimming Top", filledFields, errors);
        await _page.WaitForTimeoutAsync(5000); // 5-second pause for visibility
        
        _logger.LogInformation("🎯 WATCH BROWSER! Filling Trimming Bottom with value: {value}", values[1]);
        await ScrollToElementAndFill("#Trimmingbottom", values[1], "Trimming Bottom", filledFields, errors);
        await _page.WaitForTimeoutAsync(5000); // 5-second pause for visibility
        
        _logger.LogInformation("🎯 WATCH BROWSER! Filling Trimming Left with value: {value}", values[2]);
        await ScrollToElementAndFill("#Trimmingleft", values[2], "Trimming Left", filledFields, errors);
        await _page.WaitForTimeoutAsync(5000); // 5-second pause for visibility
        
        _logger.LogInformation("🎯 WATCH BROWSER! Filling Trimming Right with value: {value}", values[3]);
        await ScrollToElementAndFill("#Trimmingright", values[3], "Trimming Right", filledFields, errors);
        await _page.WaitForTimeoutAsync(5000); // 5-second pause for visibility
        
        _logger.LogInformation("✅ Completed filling all Trimming fields (T/B/L/R)");
        
        // Add a pause so user can see the trimming fields in the visible browser
        await _page.WaitForTimeoutAsync(3000);
    }

    private async Task FillFinishingFieldsSegment(FinishingFields finishing, List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("🎨 === FILLING FINISHING FIELDS SEGMENT ===");
        
        try
        {
            // Trimming fields (T/B/L/R)
            _logger.LogInformation("Processing Trimming fields (T/B/L/R)");
            await FillTrimmingFields(finishing.Trimming, filledFields, errors);
            
            // Striping fields (T/B/L/R)
            _logger.LogInformation("Processing Striping fields (T/B/L/R)");
            await FillStripingFields(finishing.Striping, filledFields, errors);
            
            // Gripper field
            _logger.LogInformation("Processing Gripper field: {value}", finishing.Gripper);
            await FillInputField("#PlanGripper", finishing.Gripper, "Gripper", filledFields, errors);
            
            // Color Strip field
            _logger.LogInformation("Processing Color Strip field: {value}", finishing.ColorStrip);
            await FillInputField("#PlanColorStrip", finishing.ColorStrip, "Color Strip", filledFields, errors);
            
            // Finished Format dropdown - Enhanced with error handling
            _logger.LogInformation("Processing Finished Format dropdown: {value}", finishing.FinishedFormat);
            await FillFinishedFormatDropdown(finishing.FinishedFormat, filledFields, errors);
            
            _logger.LogInformation("✅ Completed Finishing Fields segment");
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Finishing Fields segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Finishing Fields segment");
        }
    }

    private async Task FillStripingFields(string stripingValue, List<string> filledFields, List<string> errors)
    {
        // Parse T/B/L/R format or use default "0/0/0/0"  
        var values = string.IsNullOrEmpty(stripingValue) ? new[] {"0", "0", "0", "0"} : stripingValue.Split('/');
        if (values.Length != 4) values = new[] {"0", "0", "0", "0"};
        
        await FillInputField("#Stripingtop", values[0], "Striping Top", filledFields, errors);
        await FillInputField("#Stripingbottom", values[1], "Striping Bottom", filledFields, errors);
        await FillInputField("#Stripingleft", values[2], "Striping Left", filledFields, errors);
        await FillInputField("#Stripingright", values[3], "Striping Right", filledFields, errors);
    }

    private async Task FillProcessDetailsSegment(List<string> filledFields, List<string> errors)
    {
        _logger.LogInformation("Validating Process Details segment (Segment 5) - DevExtreme DataGrid ready for Step 14");
        
        try
        {
            // Wait for process grid to be fully loaded and ready for Step 14
            await _page.WaitForSelectorAsync("#GridOperation", new PageWaitForSelectorOptions { Timeout = 5000 });
            
            // Validate the planning panel is ready for process addition in Step 14
            var planningPanelReady = await _page.QuerySelectorAsync("button:has-text('Show Cost'), #btnShowCost");
            if (planningPanelReady != null)
            {
                _logger.LogInformation("✅ Planning panel with process grids is ready for Step 14 process addition");
                filledFields.Add("Process Grid: Planning panel ready for Step 14");
            }
            else
            {
                _logger.LogWarning("⚠️ Planning panel not fully ready - Step 14 may need to wait");
                filledFields.Add("Process Grid: Panel state checked");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error validating Process Details segment: {ex.Message}");
            _logger.LogError(ex, "Error validating Process Details segment");
        }
    }




    private async Task FillInputField(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            var element = await _page.QuerySelectorAsync(selector);
            if (element != null && await element.IsVisibleAsync() && await element.IsEnabledAsync())
            {
                await element.FillAsync(value);
                filledFields.Add($"{fieldName} = {value}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error filling {fieldName}: {ex.Message}");
        }
    }

    private async Task FillDropdownField(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            var element = await _page.QuerySelectorAsync(selector);
            if (element != null && await element.IsVisibleAsync())
            {
                await element.SelectOptionAsync(new[] { value });
                filledFields.Add($"{fieldName} = {value}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error filling dropdown {fieldName}: {ex.Message}");
        }
    }

    private async Task FillDevExtremeDropdown(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("Filling DevExtreme dropdown {fieldName} with value: {value}", fieldName, value);
            
            // First, close any existing open dropdowns to prevent conflicts
            await CloseExistingDropdowns();
            
            // Special handling for Raw Material dropdowns with dynamic database search
            if (fieldName == "Item Quality")
            {
                var qualityResult = await FillQualityDropdownWithSearch(selector, value, fieldName, filledFields, errors);
                if (qualityResult) return;
            }
            else if (fieldName == "GSM" || fieldName == "Mill" || fieldName == "Finish")
            {
                var materialResult = await FillMaterialDropdownWithSearch(selector, value, fieldName, filledFields, errors);
                if (materialResult) return;
            }
            
            // Enhanced approach: Direct Playwright interaction with Enter key
            try
            {
                _logger.LogInformation("Using direct Playwright interaction for {fieldName}", fieldName);
                
                // Find the input element within the dropdown - try multiple strategies
                var inputSelectors = new[]
                {
                    $"{selector} .dx-texteditor-input",
                    $"{selector} input[type='text']",
                    $"{selector} .dx-selectbox-field"
                };
                
                IElementHandle? inputElement = null;
                foreach (var inputSelector in inputSelectors)
                {
                    inputElement = await _page.QuerySelectorAsync(inputSelector);
                    if (inputElement != null && await inputElement.IsVisibleAsync())
                    {
                        _logger.LogInformation("Found input element for {fieldName} using selector: {selector}", fieldName, inputSelector);
                        break;
                    }
                }
                
                if (inputElement != null)
                {
                    // Enhanced interaction with better error handling
                    await inputElement.FocusAsync();
                    await _page.WaitForTimeoutAsync(200);
                    
                    // Clear and set the value with retry logic
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        try
                        {
                            await inputElement.FillAsync("");
                            await _page.WaitForTimeoutAsync(100);
                            
                            await inputElement.FillAsync(value);
                            await _page.WaitForTimeoutAsync(300);
                            
                            // Press Enter to confirm selection
                            await inputElement.PressAsync("Enter");
                            await _page.WaitForTimeoutAsync(500);
                            
                            _logger.LogInformation("✅ Successfully filled {fieldName} using direct interaction + Enter (attempt {attempt})", fieldName, attempt);
                            filledFields.Add($"{fieldName} = {value}");
                            return;
                        }
                        catch (Exception retryEx)
                        {
                            _logger.LogWarning("Attempt {attempt} failed for {fieldName}: {error}", attempt, fieldName, retryEx.Message);
                            if (attempt == 2) throw;
                            await _page.WaitForTimeoutAsync(1000);
                        }
                    }
                }
            }
            catch (Exception directEx)
            {
                _logger.LogWarning("Direct interaction failed for {fieldName}: {error}", fieldName, directEx.Message);
            }
            
            // Fallback method: JavaScript widget API
            var safeValue = value.Replace("'", "\\'").Replace("\"", "\\\"");
            var jsCode = $@"
                (function() {{
                    try {{
                        const dropdown = document.querySelector('{selector}');
                        if (!dropdown) return false;

                        // Try DevExtreme widget API
                        let widget = null;
                        if (window.DevExpress) {{
                            widget = window.DevExpress.ui.dxSelectBox.getInstance(dropdown) ||
                                    window.DevExpress.ui.dxDropDownBox.getInstance(dropdown) ||
                                    dropdown._component;
                        }}
                        
                        if (widget && typeof widget.option === 'function') {{
                            widget.option('value', '{safeValue}');
                            console.log('Set DevExtreme value: {safeValue}');
                            return true;
                        }}

                        // Fallback: direct input manipulation
                        const input = dropdown.querySelector('.dx-texteditor-input');
                        if (input) {{
                            input.value = '{safeValue}';
                            input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                            input.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            return true;
                        }}
                        
                        return false;
                    }} catch (e) {{
                        console.error('DevExtreme dropdown error:', e);
                        return false;
                    }}
                }})();
            ";

            var result = await _page.EvaluateAsync<bool>(jsCode);

            if (result)
            {
                filledFields.Add($"{fieldName} = {value}");
                await _page.WaitForTimeoutAsync(500);
            }
            else
            {
                // Final fallback method: Physical interaction
                await TryPhysicalDropdownInteraction(selector, value, fieldName, filledFields, errors);
            }
            
        }
        catch (Exception ex)
        {
            errors.Add($"Error filling DevExtreme dropdown {fieldName}: {ex.Message}");
            _logger.LogError(ex, "Error filling DevExtreme dropdown {fieldName}", fieldName);
        }
    }

    private async Task TryPhysicalDropdownInteraction(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            var dropdownContainer = await _page.QuerySelectorAsync(selector);
            if (dropdownContainer == null) return;

            // Click on the dropdown to open it
            var clickTarget = await dropdownContainer.QuerySelectorAsync(".dx-dropdowneditor-button") ?? 
                            await dropdownContainer.QuerySelectorAsync(".dx-texteditor-input") ??
                            dropdownContainer;
            
            await clickTarget.ClickAsync();
            await _page.WaitForTimeoutAsync(1500); // Wait for dropdown to fully open

            // Look for dropdown content in popup or overlay
            var dropdownContent = await _page.QuerySelectorAsync(".dx-popup-content .dx-list, .dx-selectbox-popup .dx-list, .dx-overlay .dx-list");
            
            if (dropdownContent != null)
            {
                // Search for matching option
                var options = await dropdownContent.QuerySelectorAllAsync(".dx-list-item, .dx-item");
                foreach (var option in options)
                {
                    var optionText = await option.TextContentAsync();
                    if (optionText?.Contains(value, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await option.ClickAsync();
                        await _page.WaitForTimeoutAsync(500);
                        filledFields.Add($"{fieldName} = {value} (physical interaction)");
                        return;
                    }
                }
                
                // If no exact match, try typing to filter
                var searchInput = await dropdownContent.QuerySelectorAsync("input[type='text'], .dx-texteditor-input");
                if (searchInput != null)
                {
                    await searchInput.FillAsync(value);
                    await _page.WaitForTimeoutAsync(1000);
                    
                    // Try to find the filtered result
                    var filteredOption = await dropdownContent.QuerySelectorAsync(".dx-list-item:first-child, .dx-item:first-child");
                    if (filteredOption != null)
                    {
                        await filteredOption.ClickAsync();
                        filledFields.Add($"{fieldName} = {value} (filtered selection)");
                        return;
                    }
                }
            }
            
            // If dropdown is still open, press Escape to close
            await _page.Keyboard.PressAsync("Escape");
            errors.Add($"Could not find matching option for {fieldName}: {value}");
        }
        catch (Exception ex)
        {
            errors.Add($"Physical interaction failed for {fieldName}: {ex.Message}");
        }
    }

    private async Task<bool> FillQualityDropdownWithSearch(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("Using specialized Quality dropdown search for: {value}", value);
            
            // Find the dropdown container
            var dropdownContainer = await _page.QuerySelectorAsync(selector);
            if (dropdownContainer == null)
            {
                _logger.LogWarning("Quality dropdown container not found: {selector}", selector);
                return false;
            }

            // Click to open dropdown
            var dropdownButton = await dropdownContainer.QuerySelectorAsync(".dx-dropdowneditor-button") ??
                               await dropdownContainer.QuerySelectorAsync(".dx-texteditor-input");
            
            if (dropdownButton != null)
            {
                await dropdownButton.ClickAsync();
                await _page.WaitForTimeoutAsync(1000); // Wait for dropdown to open
            }

            // Find the input field for searching
            var searchInput = await dropdownContainer.QuerySelectorAsync(".dx-texteditor-input");
            if (searchInput != null)
            {
                _logger.LogInformation("Found search input, typing quality: {value}", value);
                
                // Clear and type search term
                await searchInput.FillAsync("");
                await _page.WaitForTimeoutAsync(200);
                
                // Use modern typing approach instead of obsolete TypeAsync
                await searchInput.FillAsync(value);
                await _page.WaitForTimeoutAsync(1500); // Wait for database search results
                
                // Wait for dropdown list to appear with search results - use more specific approach
                var dropdownList = await _page.WaitForSelectorAsync(".dx-list:visible, .dx-popup-content .dx-selectbox-popup:visible", 
                    new PageWaitForSelectorOptions { Timeout = 3000 });
                
                if (dropdownList != null)
                {
                    // Look for matching items in the search results
                    var items = await dropdownList.QuerySelectorAllAsync(".dx-list-item, .dx-item");
                    var candidateMatches = new List<(IElementHandle Element, string Text, int Score)>();
                    
                    // First, collect all candidates and score them
                    foreach (var item in items)
                    {
                        var itemText = await item.TextContentAsync();
                        if (itemText != null)
                        {
                            _logger.LogInformation("Found dropdown item: {itemText}", itemText);
                            var score = CalculateQualityMatchScore(value, itemText);
                            if (score > 0)
                            {
                                candidateMatches.Add((item, itemText, score));
                            }
                        }
                    }
                    
                    // Sort by score (highest first) and select the best match
                    if (candidateMatches.Any())
                    {
                        var bestMatch = candidateMatches.OrderByDescending(m => m.Score).First();
                        _logger.LogInformation("Best match for '{value}': '{bestMatchText}' (Score: {score})", 
                            value, bestMatch.Text, bestMatch.Score);
                        
                        await bestMatch.Element.ClickAsync();
                        await _page.WaitForTimeoutAsync(1000);
                        
                        filledFields.Add($"{fieldName} = {bestMatch.Text} (smart match, score: {bestMatch.Score})");
                        return true;
                    }
                    
                    // If no scored matches, try the original simple approach
                    foreach (var item in items)
                    {
                        var itemText = await item.TextContentAsync();
                        if (itemText != null && itemText.Contains(value, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Fallback: Clicking first matching quality item: {itemText}", itemText);
                            await item.ClickAsync();
                            await _page.WaitForTimeoutAsync(1000);
                            
                            filledFields.Add($"{fieldName} = {value} (fallback match)");
                            return true;
                        }
                    }
                }
                
                // If no matches found, press Enter to accept typed value
                await _page.Keyboard.PressAsync("Enter");
                await _page.WaitForTimeoutAsync(500);
                filledFields.Add($"{fieldName} = {value} (manual entry)");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in specialized Quality dropdown handling");
            errors.Add($"Error in Quality dropdown search: {ex.Message}");
            return false;
        }
    }

    private int CalculateQualityMatchScore(string searchTerm, string candidateText)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || string.IsNullOrWhiteSpace(candidateText))
            return 0;

        var searchLower = searchTerm.ToLower();
        var candidateLower = candidateText.ToLower();
        int score = 0;

        // Exact match gets highest score
        if (candidateLower == searchLower)
            return 1000;

        // Extract key terms from search
        var searchWords = searchLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateWords = candidateLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Score based on word matches
        foreach (var searchWord in searchWords)
        {
            if (candidateWords.Any(cw => cw.Contains(searchWord)))
            {
                // Higher score for exact word match
                if (candidateWords.Contains(searchWord))
                    score += 100;
                else
                    score += 50; // Partial word match
            }
        }

        // Bonus for containing all search words
        if (searchWords.All(sw => candidateWords.Any(cw => cw.Contains(sw))))
            score += 200;

        // Special scoring for paper quality terms
        if (searchLower.Contains("art") && candidateLower.Contains("art"))
            score += 150;
        
        if (searchLower.Contains("paper") && candidateLower.Contains("paper"))
            score += 100;

        // Prefer common/standard options for ambiguous matches
        if (candidateLower.Contains("gsm"))
        {
            // Extract GSM value and prefer mid-range (120-200 GSM for art paper)
            var gsmMatch = System.Text.RegularExpressions.Regex.Match(candidateLower, @"(\d+)\s*gsm");
            if (gsmMatch.Success && int.TryParse(gsmMatch.Groups[1].Value, out int gsm))
            {
                if (gsm >= 120 && gsm <= 200) // Good range for art paper
                    score += 75;
                else if (gsm >= 80 && gsm <= 300) // Acceptable range
                    score += 25;
            }
        }

        // Penalty for very long names (often indicates specialized variants)
        if (candidateText.Length > 50)
            score -= 20;

        // Bonus for shorter, cleaner names
        if (candidateText.Length < 25 && score > 50)
            score += 30;

        return Math.Max(0, score);
    }

    private async Task<IElementHandle?> FindDropdownSearchInput(string selector, string fieldName)
    {
        // Try multiple strategies to find the search input
        var searchStrategies = new[]
        {
            $"{selector} .dx-texteditor-input", // Direct child input
            $"{selector}.dx-texteditor-input", // Direct selector match
            $"{selector} input[type='text']", // Generic text input
            $"{selector} input:not([type='hidden'])", // Any visible input
            $"{selector} .dx-texteditor .dx-texteditor-input", // Nested structure
            $"#{selector.TrimStart('#')} .dx-texteditor-input", // Ensure # prefix
            $"[id*='{selector.TrimStart('#')}'] .dx-texteditor-input", // Partial ID match
            $".dx-texteditor-input[placeholder*='{fieldName}']" // Placeholder-based search
        };

        foreach (var strategy in searchStrategies)
        {
            try
            {
                var input = await _page.QuerySelectorAsync(strategy);
                if (input != null)
                {
                    var isVisible = await input.IsVisibleAsync();
                    var isEnabled = await input.IsEnabledAsync();
                    
                    if (isVisible && isEnabled)
                    {
                        _logger.LogInformation("Found search input for {fieldName} using strategy: {strategy}", fieldName, strategy);
                        return input;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Search strategy failed for {fieldName} with {strategy}: {error}", fieldName, strategy, ex.Message);
            }
        }
        
        _logger.LogWarning("Could not find search input for {fieldName} with any strategy", fieldName);
        return null;
    }

    private async Task<bool> FillMaterialDropdownWithSearch(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("Using enhanced dropdown search for {fieldName}: {value}", fieldName, value);
            
            // Enhanced search input detection with multiple strategies
            var searchInput = await FindDropdownSearchInput(selector, fieldName);
            if (searchInput != null)
            {
                _logger.LogInformation("Found search input for {fieldName}, typing: {value}", fieldName, value);
                
                try
                {
                    // Check if page is still active before proceeding
                    if (_page.IsClosed)
                    {
                        _logger.LogError("Page closed before filling {fieldName}", fieldName);
                        return false;
                    }
                    
                    // Enhanced but safer input interaction
                    await searchInput.ClickAsync(); // Focus the input first
                    await _page.WaitForTimeoutAsync(200);
                    
                    // Safer clearing approach - just use FillAsync which clears automatically
                    await searchInput.FillAsync("");
                    await _page.WaitForTimeoutAsync(300);
                    
                    // Use safer typing approach - FillAsync instead of character-by-character
                    await searchInput.FillAsync(value);
                    await _page.WaitForTimeoutAsync(1500); // Reduced wait time
                    
                    _logger.LogInformation("Completed typing '{value}' for {fieldName}, waiting for dropdown results", value, fieldName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error during enhanced search for {fieldName}: {error}", fieldName, ex.Message);
                    return false; // Continue to JavaScript fallback
                }
            }
            else
            {
                _logger.LogWarning("Could not find search input for {fieldName} with selector {selector}", fieldName, selector);
                return false; // Continue to JavaScript fallback
            }
            
            // Wait for dropdown list to appear with search results - use more specific approach  
            var dropdownList = await _page.WaitForSelectorAsync(".dx-list:visible, .dx-popup-content .dx-selectbox-popup:visible", 
                new PageWaitForSelectorOptions { Timeout = 3000 });
            
            if (dropdownList != null)
            {
                // Look for matching items in the search results (same as Quality dropdown)
                var items = await dropdownList.QuerySelectorAllAsync(".dx-list-item, .dx-item");
                var candidateMatches = new List<(IElementHandle Element, string Text, int Score)>();
                
                // First, collect all candidates and score them (same as Quality dropdown)
                foreach (var item in items)
                {
                    var itemText = await item.TextContentAsync();
                    if (itemText != null)
                    {
                        _logger.LogInformation("Found {fieldName} dropdown item: {itemText}", fieldName, itemText);
                        var score = CalculateMaterialMatchScore(value, itemText, fieldName);
                        if (score > 0)
                        {
                            candidateMatches.Add((item, itemText, score));
                        }
                    }
                }
                
                // Sort by score (highest first) and select the best match (same as Quality dropdown)
                if (candidateMatches.Any())
                {
                    var bestMatch = candidateMatches.OrderByDescending(m => m.Score).First();
                    _logger.LogInformation("Best match for {fieldName} '{value}': '{bestMatchText}' (Score: {score})", 
                        fieldName, value, bestMatch.Text, bestMatch.Score);
                    
                    await bestMatch.Element.ClickAsync();
                    await _page.WaitForTimeoutAsync(1000);
                    
                    // Ensure dropdown is closed after selection
                    await _page.Keyboard.PressAsync("Escape");
                    await _page.WaitForTimeoutAsync(300);
                    
                    filledFields.Add($"{fieldName} = {bestMatch.Text} (smart match, score: {bestMatch.Score})");
                    return true;
                }
                
                // If no scored matches, collect available options for error reporting
                var availableOptions = new List<string>();
                foreach (var item in items)
                {
                    var optText = await item.TextContentAsync();
                    if (!string.IsNullOrEmpty(optText))
                        availableOptions.Add(optText);
                }
                
                // No match found - handle error with screenshot and email notification
                var errorMessage = $"Parameter '{value}' not found in {fieldName} dropdown. Available options: {string.Join(", ", availableOptions)}";
                await HandleDropdownError(fieldName, value, errorMessage, errors, availableOptions);
                
                // Select first option as fallback (same as Quality dropdown)
                if (items.Count > 0)
                {
                    var firstItem = items[0];
                    var firstItemText = await firstItem.TextContentAsync();
                    _logger.LogWarning("No exact match found for {fieldName} '{value}', selecting first option as fallback: {optionText}", fieldName, value, firstItemText);
                    await firstItem.ClickAsync();
                    await _page.WaitForTimeoutAsync(1000);
                    filledFields.Add($"{fieldName} = {firstItemText} (fallback - '{value}' not found)");
                    return true;
                }
            }
            
            // If no dropdown appeared but we have search input, press Tab then Enter (accept typed value)
            if (searchInput != null)
            {
                _logger.LogInformation("No dropdown results appeared for {fieldName} '{value}', accepting typed value and closing dropdown", fieldName, value);
                
                // Press Escape to close any open dropdown first
                await _page.Keyboard.PressAsync("Escape");
                await _page.WaitForTimeoutAsync(300);
                
                // Re-focus on the input and accept the typed value
                await searchInput.ClickAsync();
                await _page.WaitForTimeoutAsync(200);
                await _page.Keyboard.PressAsync("Tab");
                await _page.WaitForTimeoutAsync(300);
                await _page.Keyboard.PressAsync("Enter");
                await _page.WaitForTimeoutAsync(500);
                
                // Final escape to ensure all dropdowns are closed
                await _page.Keyboard.PressAsync("Escape");
                await _page.WaitForTimeoutAsync(300);
                
                filledFields.Add($"{fieldName} = {value} (typed value accepted - no dropdown results)");
                return true;
            }
            
            // Ensure dropdown is closed even if we couldn't process it
            await _page.Keyboard.PressAsync("Escape");
            await _page.WaitForTimeoutAsync(300);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {fieldName} dropdown handling", fieldName);
            await HandleDropdownError(fieldName, value, $"Exception in {fieldName} dropdown: {ex.Message}", errors);
            return false;
        }
    }

    private int CalculateMaterialMatchScore(string searchValue, string itemText, string fieldName)
    {
        if (string.IsNullOrEmpty(searchValue) || string.IsNullOrEmpty(itemText))
            return 0;
        
        int score = 0;
        var lowerSearch = searchValue.ToLower();
        var lowerItem = itemText.ToLower();
        
        // Exact match gets highest score (same as Quality dropdown)
        if (lowerItem == lowerSearch)
            return 1000;
        
        // Field-specific scoring
        switch (fieldName)
        {
            case "GSM":
                // For GSM, look for numeric matches
                if (int.TryParse(searchValue, out int targetGsm))
                {
                    var numbers = System.Text.RegularExpressions.Regex.Matches(itemText, @"\d+");
                    foreach (System.Text.RegularExpressions.Match match in numbers)
                    {
                        if (int.TryParse(match.Value, out int itemGsm) && itemGsm == targetGsm)
                            return 800; // High score for exact GSM match
                    }
                }
                // Substring match for GSM
                if (lowerItem.Contains(lowerSearch))
                    return 600;
                break;
                
            case "Mill":
            case "Finish":
                // For Mill and Finish, prioritize exact word matches
                if (lowerItem.Contains(lowerSearch))
                    return 700;
                if (lowerSearch.Contains(lowerItem))
                    return 600;
                // Check for word boundary matches
                if (System.Text.RegularExpressions.Regex.IsMatch(lowerItem, $@"\b{System.Text.RegularExpressions.Regex.Escape(lowerSearch)}\b"))
                    return 750;
                break;
        }
        
        return 0; // No match
    }

    private async Task HandleDropdownError(string fieldName, string requestedValue, string errorMessage, List<string> errors, List<string>? availableOptions = null)
    {
        _logger.LogError("Dropdown Error - Field: {fieldName}, Requested: {requestedValue}, Error: {errorMessage}", fieldName, requestedValue, errorMessage);
        errors.Add(errorMessage);
        
        try
        {
            // Take screenshot for error documentation
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var screenshotPath = Path.Combine(_screenshotDirectory, $"dropdown_error_{fieldName}_{timestamp}.png");
            
            // Ensure screenshot directory exists
            Directory.CreateDirectory(_screenshotDirectory);
            
            await _page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });
            
            _logger.LogInformation("Screenshot saved for {fieldName} dropdown error: {screenshotPath}", fieldName, screenshotPath);
            
            // Prepare error notification data for email service
            var errorNotification = new
            {
                FieldName = fieldName,
                RequestedValue = requestedValue,
                ErrorMessage = errorMessage,
                ScreenshotPath = screenshotPath,
                Timestamp = DateTime.Now,
                AvailableOptions = availableOptions ?? new List<string>(),
                PageUrl = _page.Url,
                PageTitle = await _page.TitleAsync()
            };
            
            // Log error notification data (this would be picked up by the notification service)
            _logger.LogWarning("DROPDOWN_ERROR_NOTIFICATION: {errorNotification}", System.Text.Json.JsonSerializer.Serialize(errorNotification));
            
            // Add screenshot path to errors for tracking
            errors.Add($"Screenshot saved: {screenshotPath}");
            
            // Add structured error data for email notification
            errors.Add($"ERROR_NOTIFICATION_DATA: {System.Text.Json.JsonSerializer.Serialize(errorNotification)}");
            
        }
        catch (Exception screenshotEx)
        {
            _logger.LogError(screenshotEx, "Failed to take screenshot for {fieldName} error", fieldName);
            errors.Add($"Failed to capture error screenshot for {fieldName}: {screenshotEx.Message}");
        }
    }


    private async Task CloseExistingDropdowns()
    {
        try
        {
            _logger.LogInformation("🚫 Closing any existing open dropdowns...");
            
            // Method 1: Try clicking outside dropdowns (generic approach)
            await _page.ClickAsync("body", new PageClickOptions { Position = new() { X = 100, Y = 100 } });
            await _page.WaitForTimeoutAsync(300);
            
            // Method 2: Try pressing Escape key to close dropdowns
            await _page.PressAsync("body", "Escape");
            await _page.WaitForTimeoutAsync(300);
            
            // Method 3: Close specific DevExtreme dropdown overlays if they exist
            var overlays = await _page.QuerySelectorAllAsync(".dx-overlay-wrapper, .dx-popup-wrapper, .dx-selectbox-popup-wrapper");
            foreach (var overlay in overlays)
            {
                try
                {
                    if (await overlay.IsVisibleAsync())
                    {
                        // Click outside the overlay to close it
                        await _page.ClickAsync("body", new PageClickOptions { Position = new() { X = 50, Y = 50 } });
                        await _page.WaitForTimeoutAsync(200);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Minor error closing overlay: {error}", ex.Message);
                }
            }
            
            _logger.LogInformation("✅ Finished closing existing dropdowns");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Non-critical error closing dropdowns: {error}", ex.Message);
        }
    }

    private async Task FillFinishedFormatDropdown(string value, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("🎯 Enhanced Finished Format dropdown handling for: {value}", value);
            
            // Multiple strategies for Finished Format dropdown
            var strategies = new[]
            {
                async () => {
                    // Strategy 1: Direct click and type
                    var dropdown = await _page.QuerySelectorAsync("#Planfinishedformat");
                    if (dropdown != null)
                    {
                        await dropdown.ScrollIntoViewIfNeededAsync();
                        await _page.WaitForTimeoutAsync(1000);
                        await dropdown.ClickAsync();
                        await _page.WaitForTimeoutAsync(500);
                        
                        // Type the value
                        await _page.PressAsync("body", "Control+a"); // Select all
                        await _page.TypeAsync("body", value);
                        await _page.PressAsync("body", "Tab"); // Move to next field
                        return true;
                    }
                    return false;
                },
                
                async () => {
                    // Strategy 2: Find input within dropdown
                    var input = await _page.QuerySelectorAsync("#Planfinishedformat .dx-texteditor-input");
                    if (input != null)
                    {
                        await input.ScrollIntoViewIfNeededAsync();
                        await _page.WaitForTimeoutAsync(1000);
                        await input.FillAsync(value);
                        await _page.PressAsync("body", "Tab");
                        return true;
                    }
                    return false;
                },
                
                async () => {
                    // Strategy 3: JavaScript direct value setting
                    await _page.EvaluateAsync($@"
                        const element = document.querySelector('#Planfinishedformat');
                        if (element) {{
                            element.value = '{value}';
                            element.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            element.dispatchEvent(new Event('input', {{ bubbles: true }}));
                        }}
                    ");
                    return true;
                }
            };
            
            foreach (var strategy in strategies)
            {
                try
                {
                    if (await strategy())
                    {
                        await _page.WaitForTimeoutAsync(1000);
                        filledFields.Add($"Finished Format = {value}");
                        _logger.LogInformation("✅ Successfully filled Finished Format: {value}", value);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Finished Format strategy failed: {error}", ex.Message);
                }
            }
            
            _logger.LogWarning("❌ All strategies failed for Finished Format");
            errors.Add("Could not fill Finished Format dropdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FillFinishedFormatDropdown");
            errors.Add($"Finished Format error: {ex.Message}");
        }
    }

    private async Task ScrollToElementAndFill(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("📍 Scrolling to and filling {fieldName} with selector {selector}", fieldName, selector);
            
            var element = await _page.QuerySelectorAsync(selector);
            if (element != null)
            {
                // Scroll element into view
                await element.ScrollIntoViewIfNeededAsync();
                await _page.WaitForTimeoutAsync(1000);
                
                // Highlight the element (add a border)
                await element.EvaluateAsync("element => element.style.border = '3px solid red'");
                await _page.WaitForTimeoutAsync(1000);
                
                // Fill the element
                await element.FillAsync(value);
                await _page.WaitForTimeoutAsync(500);
                
                // Remove highlight
                await element.EvaluateAsync("element => element.style.border = ''");
                
                filledFields.Add($"{fieldName} = {value}");
                _logger.LogInformation("✅ Successfully filled {fieldName} with value: {value}", fieldName, value);
            }
            else
            {
                _logger.LogWarning("❌ Could not find element {fieldName} with selector: {selector}", fieldName, selector);
                errors.Add($"Could not find {fieldName} element");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error filling {fieldName}: {error}", fieldName, ex.Message);
            errors.Add($"Error filling {fieldName}: {ex.Message}");
        }
    }


}

