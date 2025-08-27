using System.ComponentModel.DataAnnotations;

namespace Rpa.Core.Models;

public class EmailMessage
{
    [Required]
    public Guid JobId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Subject { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string From { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? To { get; set; }
    
    [Required]
    public string Body { get; set; } = string.Empty;
    
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    [MaxLength(50)]
    public string? Classification { get; set; }
    
    public double? ConfidenceScore { get; set; }
    
    [MaxLength(1000)]
    public string? ExtractedData { get; set; }
    
    public Dictionary<string, object>? Attachments { get; set; }
    
    [Range(0, 10)]
    public int Priority { get; set; } = 5;
}

public class JobCardInfo
{
    public string? JobNumber { get; set; }
    public string? Description { get; set; }
    public string? CustomerName { get; set; }
    public decimal? EstimatedCost { get; set; }
    public DateTime? DueDate { get; set; }
    public Dictionary<string, object>? AdditionalFields { get; set; }
}

public class ExtractedCredentials
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? SystemUrl { get; set; }
    public string? Domain { get; set; }
    public Dictionary<string, string>? AdditionalParameters { get; set; }
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public List<string>? Errors { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}