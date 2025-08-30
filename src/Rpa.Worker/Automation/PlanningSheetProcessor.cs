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

            // Wait for planning sheet to load completely
            await _page.WaitForSelectorAsync("#planJob_Size1", new PageWaitForSelectorOptions 
            { 
                Timeout = 10000 
            });

            // Segment 1: Job Size (Critical - Always Required)
            await FillJobSizeSegment(erpData, filledFields, errors);

            // Segment 2: Raw Material (if data available)  
            if (erpData.Material != null)
            {
                await FillRawMaterialSegment(erpData.Material, filledFields, errors);
            }

            // Segment 3: Printing Details (if data available)
            if (erpData.PrintingDetails != null) 
            {
                await FillPrintingDetailsSegment(erpData.PrintingDetails, filledFields, errors);
            }

            // Segment 4: Wastage & Finishing (if data available)
            if (erpData.WastageFinishing != null)
            {
                await FillWastageFinishingSegment(erpData.WastageFinishing, filledFields, errors);
            }

            // Segment 5: Process Details (Dynamic - Add processes)
            await FillProcessDetailsSegment(filledFields, errors);

            result.Message = $"Planning sheet filled successfully. {filledFields.Count} fields completed.";
            result.Data = new Dictionary<string, object>
            {
                { "filledFields", filledFields },
                { "errors", errors },
                { "totalFields", filledFields.Count }
            };

            if (errors.Any())
            {
                result.Success = false;
                result.Errors = errors;
                result.Message = $"Planning sheet completed with {errors.Count} errors. {filledFields.Count} fields filled.";
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
        _logger.LogInformation("Filling Raw Material segment (Segment 2)");
        
        try 
        {
            // DevExtreme Quality Dropdown - Primary field
            await FillDevExtremeDropdown("#ItemPlanQuality", material.Quality, "Item Quality", filledFields, errors);
            
            // GSM Dropdown (DevExtreme)
            await FillDevExtremeDropdown("#ItemPlanGsm", material.Gsm.ToString(), "GSM", filledFields, errors);
            
            // Mill Dropdown (DevExtreme)
            await FillDevExtremeDropdown("#ItemPlanMill", material.Mill, "Mill", filledFields, errors);
            
            // Finish Dropdown (DevExtreme) 
            await FillDevExtremeDropdown("#ItemPlanFinish", material.Finish, "Finish", filledFields, errors);
            
            // Paper Trimming fields (T/B/L/R format)
            var trimmingParts = material.Quality.Contains("trim") ? new[] {"3", "3", "3", "3"} : new[] {"0", "0", "0", "0"};
            await FillInputField("#PaperTrimtop", trimmingParts[0], "Paper Trim Top", filledFields, errors);
            await FillInputField("#PaperTrimbottom", trimmingParts[1], "Paper Trim Bottom", filledFields, errors);
            await FillInputField("#PaperTrimleft", trimmingParts[2], "Paper Trim Left", filledFields, errors);
            await FillInputField("#PaperTrimright", trimmingParts[3], "Paper Trim Right", filledFields, errors);
            
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Raw Material segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Raw Material segment");
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
            
            // Trimming fields (T/B/L/R)
            await FillTrimmingFields(wastage.Trimming, filledFields, errors);
            
            // Striping fields (T/B/L/R)
            await FillStripingFields(wastage.Striping, filledFields, errors);
            
            // Additional fields with default values
            await FillInputField("#PlanGripper", "12", "Gripper", filledFields, errors);
            await FillInputField("#PlanColorStrip", "6", "Color Strip", filledFields, errors);
            
            // Finished Format dropdown
            await FillDevExtremeDropdown("#Planfinishedformat", "Sheet Fed", "Finished Format", filledFields, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Wastage & Finishing segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Wastage & Finishing segment");
        }
    }

    private async Task FillTrimmingFields(string trimmingValue, List<string> filledFields, List<string> errors)
    {
        // Parse T/B/L/R format or use default "0/0/0/0"
        var values = string.IsNullOrEmpty(trimmingValue) ? new[] {"0", "0", "0", "0"} : trimmingValue.Split('/');
        if (values.Length != 4) values = new[] {"0", "0", "0", "0"};
        
        await FillInputField("#Trimmingtop", values[0], "Trimming Top", filledFields, errors);
        await FillInputField("#Trimmingbottom", values[1], "Trimming Bottom", filledFields, errors);
        await FillInputField("#Trimmingleft", values[2], "Trimming Left", filledFields, errors);
        await FillInputField("#Trimmingright", values[3], "Trimming Right", filledFields, errors);
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
        _logger.LogInformation("Filling Process Details segment (Segment 5) - DevExtreme DataGrid");
        
        try
        {
            // Wait for process grid to be fully loaded
            await _page.WaitForSelectorAsync("#GridOperation", new PageWaitForSelectorOptions { Timeout = 5000 });
            
            // Check if grid has data or is empty
            var noDataElement = await _page.QuerySelectorAsync(".dx-datagrid-nodata");
            var isGridEmpty = noDataElement != null && await noDataElement.IsVisibleAsync();
            
            if (isGridEmpty)
            {
                _logger.LogInformation("Process grid is empty - attempting to add common processes");
                
                // Common processes for box manufacturing
                var commonProcesses = new[] 
                { 
                    "Die Cutting", 
                    "Gluing", 
                    "Window Patching", 
                    "Lamination"
                };
                
                // Try to add processes using the filter/search approach
                foreach (var process in commonProcesses)
                {
                    await TryAddProcessToGrid(process, filledFields, errors);
                }
            }
            else
            {
                _logger.LogInformation("Process grid already contains data - skipping process addition");
                filledFields.Add("Process Grid: Already populated");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error in Process Details segment: {ex.Message}");
            _logger.LogError(ex, "Error filling Process Details segment");
        }
    }

    private async Task TryAddProcessToGrid(string processName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("Attempting to add process: {processName} to DevExtreme grid", processName);
            
            // Method 1: Try using the filter row to search for process
            var processNameFilter = await _page.QuerySelectorAsync(".dx-datagrid-filter-row .dx-texteditor-input");
            if (processNameFilter != null && await processNameFilter.IsVisibleAsync())
            {
                await processNameFilter.ClickAsync();
                await processNameFilter.FillAsync(processName);
                await _page.WaitForTimeoutAsync(2000); // Wait for grid to filter
                
                // Look for rows with the process name
                var processRow = await _page.QuerySelectorAsync($".dx-datagrid-rowsview tr:has-text('{processName}')");
                if (processRow != null)
                {
                    // Look for Add button in the row (first column)
                    var addButton = await processRow.QuerySelectorAsync("td:first-child, .dx-datagrid-action");
                    if (addButton != null && await addButton.IsVisibleAsync())
                    {
                        await addButton.ClickAsync();
                        await _page.WaitForTimeoutAsync(1000);
                        filledFields.Add($"Process Added: {processName}");
                        return;
                    }
                }
            }
            
            // Method 2: Try toolbar buttons (refresh and add)
            var toolbarAddButton = await _page.QuerySelectorAsync(".dx-toolbar .dx-icon-add");
            if (toolbarAddButton != null)
            {
                await toolbarAddButton.ClickAsync();
                await _page.WaitForTimeoutAsync(1000);
                
                // This might open a process selection dialog
                // Handle the dialog if it appears
                await HandleProcessSelectionDialog(processName, filledFields, errors);
            }
            
            // Method 3: Direct row addition if grid supports it
            var emptyRow = await _page.QuerySelectorAsync(".dx-row-inserted, .dx-datagrid-rowsview .dx-row:first-child");
            if (emptyRow != null)
            {
                var processNameCell = await emptyRow.QuerySelectorAsync("td:nth-child(2) input, td[aria-colindex='2'] input");
                if (processNameCell != null)
                {
                    await processNameCell.FillAsync(processName);
                    filledFields.Add($"Process Added: {processName}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error adding process {processName}: {ex.Message}");
            _logger.LogWarning(ex, "Could not add process {processName}", processName);
        }
    }

    private async Task HandleProcessSelectionDialog(string processName, List<string> filledFields, List<string> errors)
    {
        try
        {
            // Wait for potential dialog or popup
            await _page.WaitForTimeoutAsync(1000);
            
            // Look for process selection dialog/modal
            var dialog = await _page.QuerySelectorAsync(".dx-popup, .dx-overlay, .modal");
            if (dialog != null && await dialog.IsVisibleAsync())
            {
                // Search within dialog
                var searchField = await dialog.QuerySelectorAsync("input[type='text'], .dx-texteditor-input");
                if (searchField != null)
                {
                    await searchField.FillAsync(processName);
                    await _page.WaitForTimeoutAsync(1000);
                    
                    // Look for process in list and click
                    var processOption = await dialog.QuerySelectorAsync($"*:has-text('{processName}')");
                    if (processOption != null)
                    {
                        await processOption.ClickAsync();
                        
                        // Look for OK/Save button
                        var saveButton = await dialog.QuerySelectorAsync("button:has-text('OK'), button:has-text('Save'), .dx-button-success");
                        if (saveButton != null)
                        {
                            await saveButton.ClickAsync();
                            filledFields.Add($"Process Added via Dialog: {processName}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error in process dialog for {processName}: {ex.Message}");
        }
    }

    private async Task TryAddProcess(string processName, List<string> filledFields, List<string> errors)
    {
        try
        {
            // Search for process in the process selection area
            var searchField = await _page.QuerySelectorAsync("#process_search, .process-search");
            if (searchField != null)
            {
                await searchField.FillAsync(processName);
                await _page.WaitForTimeoutAsync(1000);
                
                // Look for "+" button to add process
                var addButton = await _page.QuerySelectorAsync($"button:has-text('+'):visible, .add-process-btn:visible");
                if (addButton != null)
                {
                    await addButton.ClickAsync();
                    filledFields.Add($"Process Added: {processName}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error adding process {processName}: {ex.Message}");
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
            
            // Enhanced DevExtreme handling with exact widget API calls
            var jsCode = @"
                try {
                    const dropdown = document.querySelector('" + selector + @"');
                    if (!dropdown) {
                        console.log('Dropdown container not found: " + selector + @"');
                        return false;
                    }

                    // Method 1: Use DevExtreme widget API directly
                    let widget = null;
                    if (window.DevExpress) {
                        // Try different ways to get the widget instance
                        widget = window.DevExpress.ui.dxSelectBox.getInstance(dropdown) ||
                                window.DevExpress.ui.dxDropDownBox.getInstance(dropdown) ||
                                dropdown._component;
                    }
                    
                    if (widget && typeof widget.option === 'function') {
                        // For dropdowns with datasource, try to find matching item
                        const dataSource = widget.option('dataSource');
                        if (Array.isArray(dataSource)) {
                            const matchingItem = dataSource.find(item => 
                                (typeof item === 'string' && item.toLowerCase().includes('" + value.ToLower() + @"')) ||
                                (typeof item === 'object' && (item.text || item.name || item.value || '').toLowerCase().includes('" + value.ToLower() + @"'))
                            );
                            if (matchingItem) {
                                widget.option('value', typeof matchingItem === 'string' ? matchingItem : (matchingItem.value || matchingItem.text || matchingItem.name));
                                console.log('Set DevExtreme value via datasource match: ', matchingItem);
                                return true;
                            }
                        }
                        
                        // Fallback: try setting the value directly
                        widget.option('value', '" + value + @"');
                        widget.option('text', '" + value + @"');
                        console.log('Set DevExtreme value directly: " + value + @"');
                        return true;
                    }

                    // Method 2: Direct input manipulation for stubborn dropdowns
                    const input = dropdown.querySelector('.dx-texteditor-input, input[type=\\'text\\']');
                    if (input) {
                        // Clear existing value
                        input.value = '';
                        input.focus();
                        
                        // Set new value
                        input.value = '" + value + @"';
                        
                        // Trigger all necessary events
                        input.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
                        input.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
                        input.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, cancelable: true }));
                        
                        // Update the hidden input if it exists
                        const hiddenInput = dropdown.querySelector('input[type=\\'hidden\\']');
                        if (hiddenInput) {
                            hiddenInput.value = '" + value + @"';
                        }
                        
                        console.log('Set input value directly: " + value + @"');
                        return true;
                    }
                    
                    console.log('No suitable method found for dropdown: " + selector + @"');
                    return false;
                } catch (e) {
                    console.error('DevExtreme dropdown error:', e);
                    return false;
                }
            ";

            var result = await _page.EvaluateAsync<bool>(jsCode);

            if (result)
            {
                filledFields.Add($"{fieldName} = {value}");
                await _page.WaitForTimeoutAsync(500);
            }
            else
            {
                // Fallback method: Physical interaction
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
                await searchInput.TypeAsync(value, new ElementHandleTypeOptions { Delay = 100 });
                await _page.WaitForTimeoutAsync(1500); // Wait for database search results
                
                // Wait for dropdown list to appear with search results
                var dropdownList = await _page.WaitForSelectorAsync(".dx-list, .dx-popup-content .dx-selectbox-popup, .dx-overlay .dx-list", 
                    new PageWaitForSelectorOptions { Timeout = 5000 });
                
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

    private async Task<bool> FillMaterialDropdownWithSearch(string selector, string value, string fieldName, List<string> filledFields, List<string> errors)
    {
        try
        {
            _logger.LogInformation("Using Quality-style dropdown search for {fieldName}: {value}", fieldName, value);
            
            // Use the same approach as Quality dropdown - find search input and type
            var searchInput = await _page.QuerySelectorAsync($"{selector} .dx-texteditor-input");
            if (searchInput != null)
            {
                _logger.LogInformation("Found search input for {fieldName}, typing: {value}", fieldName, value);
                
                // Clear and type search term (same as Quality dropdown)
                await searchInput.FillAsync("");
                await _page.WaitForTimeoutAsync(200);
                await searchInput.TypeAsync(value, new ElementHandleTypeOptions { Delay = 100 });
                await _page.WaitForTimeoutAsync(1500); // Wait for database search results
            }
            
            // Wait for dropdown list to appear with search results
            var dropdownList = await _page.WaitForSelectorAsync(".dx-list, .dx-popup-content .dx-selectbox-popup, .dx-overlay .dx-list", 
                new PageWaitForSelectorOptions { Timeout = 5000 });
            
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
}

