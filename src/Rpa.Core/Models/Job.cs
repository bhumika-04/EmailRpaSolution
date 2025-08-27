using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rpa.Core.Models;

public class Job
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(255)]
    public string EmailSubject { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string SenderEmail { get; set; } = string.Empty;
    
    [Required]
    public string EmailBody { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public JobStatus Status { get; set; } = JobStatus.Pending;
    
    [MaxLength(50)]
    public string? JobType { get; set; }
    
    public string? ExtractedCredentials { get; set; }
    
    public string? JobCardDetails { get; set; }
    
    public string? ProcessingResult { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? StartedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    [Range(0, 5)]
    public int RetryCount { get; set; } = 0;
    
    [MaxLength(1000)]
    public string? Metadata { get; set; }
    
    public int Priority { get; set; } = 5;
}

public enum JobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Retrying = 5
}