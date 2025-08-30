using System.Text.Json;

namespace Rpa.Worker.Automation;

public static class PlanningSheetConfiguration
{
    // Configuration for all 5 Planning Sheet segments
    public static readonly Dictionary<string, PlanningSegment> Segments = new()
    {
        ["JobSize"] = new PlanningSegment
        {
            Name = "Job Size (MM)",
            ContainerSelector = "#planJob_Size1",
            Fields = new()
            {
                ["Height"] = new PlanningField
                {
                    PrimarySelector = "#SizeHeight",
                    FallbackSelectors = new[] { "[name='H']", "[placeholder*='Height']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "JobSize.Height"
                },
                ["Length"] = new PlanningField
                {
                    PrimarySelector = "#SizeLength",
                    FallbackSelectors = new[] { "[name='L']", "[placeholder*='Length']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "JobSize.Length"
                },
                ["Width"] = new PlanningField
                {
                    PrimarySelector = "#SizeWidth",
                    FallbackSelectors = new[] { "[name='W']", "[placeholder*='Width']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "JobSize.Width"
                },
                ["OFlap"] = new PlanningField
                {
                    PrimarySelector = "#SizeOpenflap",
                    FallbackSelectors = new[] { "[name='OF']", "[placeholder*='Openflap']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "JobSize.OFlap"
                },
                ["PFlap"] = new PlanningField
                {
                    PrimarySelector = "#SizePastingflap",
                    FallbackSelectors = new[] { "[name='PF']", "[placeholder*='Pastingflap']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "JobSize.PFlap"
                },
                ["JobSizeSummary"] = new PlanningField
                {
                    PrimarySelector = "#JobPrePlan",
                    FallbackSelectors = new[] { "[placeholder*='Job Size']" },
                    FieldType = FieldType.TextArea,
                    IsRequired = false,
                    IsCalculated = true
                }
            }
        },

        ["RawMaterial"] = new PlanningSegment
        {
            Name = "Raw Material",
            ContainerSelector = "#planRawMaterial, .raw-material-section",
            Fields = new()
            {
                ["Quality"] = new PlanningField
                {
                    PrimarySelector = "#ddl_Quality, #MaterialQuality",
                    FallbackSelectors = new[] { "[name*='quality']", "select:has(option:contains('Art Paper'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "Material.Quality"
                },
                ["GSM"] = new PlanningField
                {
                    PrimarySelector = "#txt_GSM, #MaterialGSM",
                    FallbackSelectors = new[] { "[name*='gsm']", "[placeholder*='GSM']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "Material.Gsm"
                },
                ["Mill"] = new PlanningField
                {
                    PrimarySelector = "#ddl_Mill, #MaterialMill",
                    FallbackSelectors = new[] { "[name*='mill']", "select:has(option:contains('JK'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "Material.Mill"
                },
                ["Finish"] = new PlanningField
                {
                    PrimarySelector = "#ddl_Finish, #MaterialFinish",
                    FallbackSelectors = new[] { "[name*='finish']", "select:has(option:contains('Gloss'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "Material.Finish"
                }
            }
        },

        ["PrintingDetails"] = new PlanningSegment
        {
            Name = "Printing Details",
            ContainerSelector = "#planPrintingDetails, .printing-section",
            Fields = new()
            {
                ["FrontColors"] = new PlanningField
                {
                    PrimarySelector = "#txt_FrontColors, #PrintingFrontColors",
                    FallbackSelectors = new[] { "[name*='front']", "[placeholder*='Front Color']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "PrintingDetails.FrontColors"
                },
                ["BackColors"] = new PlanningField
                {
                    PrimarySelector = "#txt_BackColors, #PrintingBackColors",
                    FallbackSelectors = new[] { "[name*='back']", "[placeholder*='Back Color']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = true,
                    DataPath = "PrintingDetails.BackColors"
                },
                ["SpecialFront"] = new PlanningField
                {
                    PrimarySelector = "#txt_SpecialFront, #PrintingSpecialFront",
                    FallbackSelectors = new[] { "[name*='specialfront']", "[placeholder*='Special Front']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = false,
                    DataPath = "PrintingDetails.SpecialFront"
                },
                ["SpecialBack"] = new PlanningField
                {
                    PrimarySelector = "#txt_SpecialBack, #PrintingSpecialBack",
                    FallbackSelectors = new[] { "[name*='specialback']", "[placeholder*='Special Back']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = false,
                    DataPath = "PrintingDetails.SpecialBack"
                },
                ["Style"] = new PlanningField
                {
                    PrimarySelector = "#ddl_PrintingStyle, #PrintingStyle",
                    FallbackSelectors = new[] { "[name*='style']", "select:has(option:contains('Single'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "PrintingDetails.Style"
                },
                ["Plate"] = new PlanningField
                {
                    PrimarySelector = "#ddl_PlateType, #PrintingPlate",
                    FallbackSelectors = new[] { "[name*='plate']", "select:has(option:contains('CTP'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "PrintingDetails.Plate"
                }
            }
        },

        ["WastageFinishing"] = new PlanningSegment
        {
            Name = "Wastage & Finishing",
            ContainerSelector = "#planWastageFinishing, .wastage-section",
            Fields = new()
            {
                ["MakeReadySheets"] = new PlanningField
                {
                    PrimarySelector = "#txt_MakeReady, #WastageMakeReady",
                    FallbackSelectors = new[] { "[name*='makeready']", "[placeholder*='Make Ready']" },
                    ValidationPattern = @"^\d+$",
                    IsRequired = false,
                    DataPath = "WastageFinishing.MakeReadySheets"
                },
                ["WastageType"] = new PlanningField
                {
                    PrimarySelector = "#ddl_WastageType, #WastageType",
                    FallbackSelectors = new[] { "[name*='wastage']", "select:has(option:contains('Standard'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "WastageFinishing.WastageType"
                },
                ["GrainDirection"] = new PlanningField
                {
                    PrimarySelector = "#ddl_GrainDirection, #WastageGrainDirection",
                    FallbackSelectors = new[] { "[name*='grain']", "select:has(option:contains('Across'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = true,
                    DataPath = "WastageFinishing.GrainDirection"
                },
                ["OnlineCoating"] = new PlanningField
                {
                    PrimarySelector = "#ddl_OnlineCoating, #WastageOnlineCoating",
                    FallbackSelectors = new[] { "[name*='coating']", "select:has(option:contains('None'))" },
                    FieldType = FieldType.Dropdown,
                    IsRequired = false,
                    DataPath = "WastageFinishing.OnlineCoating"
                },
                ["Trimming"] = new PlanningField
                {
                    PrimarySelector = "#txt_Trimming, #WastageTrimming",
                    FallbackSelectors = new[] { "[name*='trimming']", "[placeholder*='Trimming']" },
                    IsRequired = false,
                    DataPath = "WastageFinishing.Trimming",
                    DefaultValue = "0/0/0/0"
                },
                ["Striping"] = new PlanningField
                {
                    PrimarySelector = "#txt_Striping, #WastageStriping",
                    FallbackSelectors = new[] { "[name*='striping']", "[placeholder*='Striping']" },
                    IsRequired = false,
                    DataPath = "WastageFinishing.Striping",
                    DefaultValue = "0/0/0/0"
                }
            }
        },

        ["ProcessDetails"] = new PlanningSegment
        {
            Name = "Process Details",
            ContainerSelector = "#planProcessDetails, .process-section",
            IsProcessList = true,
            Fields = new()
            {
                ["ProcessSearch"] = new PlanningField
                {
                    PrimarySelector = "#process_search, #ProcessSearchBox",
                    FallbackSelectors = new[] { "[placeholder*='Search Process']", ".process-search" },
                    FieldType = FieldType.SearchBox,
                    IsRequired = false
                },
                ["AddProcessButton"] = new PlanningField
                {
                    PrimarySelector = ".add-process-btn, button:has-text('+')",
                    FallbackSelectors = new[] { "[data-action='add-process']", ".btn-add-process" },
                    FieldType = FieldType.Button,
                    IsRequired = false
                }
            },
            DefaultProcesses = new[] 
            { 
                "Die Cutting", 
                "Gluing", 
                "Window Patching", 
                "Lamination",
                "UV Coating",
                "Embossing"
            }
        }
    };

    public static object? GetFieldValue(Rpa.Core.Models.ErpJobData erpData, string dataPath)
    {
        try
        {
            var pathParts = dataPath.Split('.');
            var currentObj = (object)erpData;

            foreach (var part in pathParts)
            {
                var property = currentObj.GetType().GetProperty(part);
                if (property == null) return null;
                
                currentObj = property.GetValue(currentObj);
                if (currentObj == null) return null;
            }

            return currentObj;
        }
        catch
        {
            return null;
        }
    }
}

public class PlanningSegment
{
    public string Name { get; set; } = string.Empty;
    public string ContainerSelector { get; set; } = string.Empty;
    public Dictionary<string, PlanningField> Fields { get; set; } = new();
    public bool IsProcessList { get; set; } = false;
    public string[]? DefaultProcesses { get; set; }
}

public class PlanningField
{
    public string PrimarySelector { get; set; } = string.Empty;
    public string[] FallbackSelectors { get; set; } = Array.Empty<string>();
    public string ValidationPattern { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = false;
    public bool IsConditional { get; set; } = false;
    public bool IsCalculated { get; set; } = false;
    public bool IsDevExtremeDropdown { get; set; } = false;
    public string Segment { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public FieldType FieldType { get; set; } = FieldType.Input;
}

public enum FieldType
{
    Input,
    Dropdown,
    TextArea,
    SearchBox,
    Button
}