using System.ComponentModel.DataAnnotations;

namespace Rpa.Core.Models;

public class ErpJobData
{
    public CompanyLogin? CompanyLogin { get; set; }
    public UserLogin? UserLogin { get; set; }
    public JobDetails? JobDetails { get; set; }
    public JobSize? JobSize { get; set; }
    public Material? Material { get; set; }
    public PrintingDetails? PrintingDetails { get; set; }
    public WastageFinishing? WastageFinishing { get; set; }
    public FinishingFields? FinishingFields { get; set; }
    public ProcessSelection? ProcessSelection { get; set; }
}

public class CompanyLogin
{
    public string CompanyName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UserLogin
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class JobDetails
{
    public string Client { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class JobSize
{
    public int Height { get; set; }
    public int Length { get; set; }
    public int Width { get; set; }
    [Display(Name = "O.flap")]
    public int OFlap { get; set; }
    [Display(Name = "P.flap")]
    public int PFlap { get; set; }
}

public class Material
{
    public string Quality { get; set; } = string.Empty;
    public int Gsm { get; set; }
    public string Mill { get; set; } = string.Empty;
    public string Finish { get; set; } = string.Empty;
}

public class PrintingDetails
{
    [Display(Name = "Front Colors")]
    public int FrontColors { get; set; }
    [Display(Name = "Back Colors")]
    public int BackColors { get; set; }
    [Display(Name = "Special Front")]
    public int SpecialFront { get; set; }
    [Display(Name = "Special Back")]
    public int SpecialBack { get; set; }
    public string Style { get; set; } = string.Empty;
    public string Plate { get; set; } = string.Empty;
}

public class WastageFinishing
{
    [Display(Name = "Make Ready Sheets")]
    public int MakeReadySheets { get; set; }
    [Display(Name = "Wastage Type")]
    public string WastageType { get; set; } = string.Empty;
    [Display(Name = "Grain Direction")]
    public string GrainDirection { get; set; } = string.Empty;
    [Display(Name = "Online Coating")]
    public string OnlineCoating { get; set; } = string.Empty;
}

public class FinishingFields
{
    [Display(Name = "Trimming (T/B/L/R)")]
    public string Trimming { get; set; } = string.Empty;
    [Display(Name = "Striping (T/B/L/R)")]
    public string Striping { get; set; } = string.Empty;
    [Display(Name = "Gripper")]
    public string Gripper { get; set; } = string.Empty;
    [Display(Name = "Color Strip")]
    public string ColorStrip { get; set; } = string.Empty;
    [Display(Name = "Finished Format")]
    public string FinishedFormat { get; set; } = string.Empty;
}

public class ProcessDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class ProcessSelection
{
    public List<ProcessDefinition> RequiredProcesses { get; set; } = new();
    public List<ProcessDefinition> OptionalProcesses { get; set; } = new();
    public List<ProcessDefinition> ContentBasedProcesses { get; set; } = new();
}

public class ErpWorkflowStep
{
    public int StepNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
}