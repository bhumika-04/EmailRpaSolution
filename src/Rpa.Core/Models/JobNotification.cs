using Rpa.Core.Models;

namespace Rpa.Core.Models;

public class JobNotification
{
    public Guid JobId { get; set; }
    public JobStatus Status { get; set; }
    public ProcessingResult Result { get; set; } = new();
    public string RecipientEmail { get; set; } = string.Empty;
}