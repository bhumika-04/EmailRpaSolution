using Rpa.Core.Models;
using Microsoft.Extensions.Logging;

namespace Rpa.Core.Services;

public interface IProcessSelectionService
{
    Task<ProcessSelection> GetProcessesForContentAsync(string contentType, string client = "");
    Task<List<ProcessDefinition>> GetAllAvailableProcessesAsync();
    Task<ProcessDefinition?> SearchProcessAsync(string searchTerm);
}

public class ProcessSelectionService : IProcessSelectionService
{
    private readonly ILogger<ProcessSelectionService> _logger;

    public ProcessSelectionService(ILogger<ProcessSelectionService> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessSelection> GetProcessesForContentAsync(string contentType, string client = "")
    {
        _logger.LogInformation("Getting processes for content type: {contentType}, client: {client}", contentType, client);
        
        await Task.Delay(10); // Simulate async operation
        
        var processSelection = new ProcessSelection();
        
        // Base processes for all box types
        var baseProcesses = new List<ProcessDefinition>
        {
            new() { Name = "Die Cutting", Category = "Cutting", IsRequired = true, DisplayOrder = 1 },
            new() { Name = "Creasing", Category = "Cutting", IsRequired = true, DisplayOrder = 2 },
            new() { Name = "Gluing", Category = "Assembly", IsRequired = true, DisplayOrder = 3 }
        };

        // Content-specific processes based on content type
        var contentSpecificProcesses = GetContentSpecificProcesses(contentType.ToLower());
        
        // Client-specific processes (if any special requirements)
        var clientSpecificProcesses = GetClientSpecificProcesses(client.ToLower());

        processSelection.RequiredProcesses = baseProcesses.Where(p => p.IsRequired).ToList();
        processSelection.ContentBasedProcesses = contentSpecificProcesses;
        processSelection.OptionalProcesses = clientSpecificProcesses;

        _logger.LogInformation("Selected {requiredCount} required, {contentCount} content-based, {optionalCount} optional processes", 
            processSelection.RequiredProcesses.Count, 
            processSelection.ContentBasedProcesses.Count, 
            processSelection.OptionalProcesses.Count);

        return processSelection;
    }

    public async Task<List<ProcessDefinition>> GetAllAvailableProcessesAsync()
    {
        await Task.Delay(10); // Simulate async operation
        
        return new List<ProcessDefinition>
        {
            // Cutting processes
            new() { Name = "Die Cutting", Category = "Cutting", IsRequired = false, DisplayOrder = 1 },
            new() { Name = "Creasing", Category = "Cutting", IsRequired = false, DisplayOrder = 2 },
            new() { Name = "Perforation", Category = "Cutting", IsRequired = false, DisplayOrder = 3 },
            new() { Name = "Scoring", Category = "Cutting", IsRequired = false, DisplayOrder = 4 },
            
            // Assembly processes
            new() { Name = "Gluing", Category = "Assembly", IsRequired = false, DisplayOrder = 5 },
            new() { Name = "Stitching", Category = "Assembly", IsRequired = false, DisplayOrder = 6 },
            new() { Name = "Folding", Category = "Assembly", IsRequired = false, DisplayOrder = 7 },
            
            // Finishing processes
            new() { Name = "UV Coating", Category = "Finishing", IsRequired = false, DisplayOrder = 8 },
            new() { Name = "Lamination", Category = "Finishing", IsRequired = false, DisplayOrder = 9 },
            new() { Name = "Embossing", Category = "Finishing", IsRequired = false, DisplayOrder = 10 },
            new() { Name = "Foil Stamping", Category = "Finishing", IsRequired = false, DisplayOrder = 11 },
            
            // Special processes
            new() { Name = "Window Patching", Category = "Special", IsRequired = false, DisplayOrder = 12 },
            new() { Name = "Handle Attachment", Category = "Special", IsRequired = false, DisplayOrder = 13 },
            new() { Name = "Magnetic Closure", Category = "Special", IsRequired = false, DisplayOrder = 14 },
            new() { Name = "Ribbon Attachment", Category = "Special", IsRequired = false, DisplayOrder = 15 }
        };
    }

    public async Task<ProcessDefinition?> SearchProcessAsync(string searchTerm)
    {
        var allProcesses = await GetAllAvailableProcessesAsync();
        
        // Exact match first
        var exactMatch = allProcesses.FirstOrDefault(p => 
            p.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));
        
        if (exactMatch != null)
            return exactMatch;

        // Partial match
        var partialMatch = allProcesses.FirstOrDefault(p => 
            p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        
        return partialMatch;
    }

    private List<ProcessDefinition> GetContentSpecificProcesses(string contentType)
    {
        return contentType switch
        {
            "reverse tuck in" => new List<ProcessDefinition>
            {
                new() { Name = "Window Patching", Category = "Special", IsRequired = false, DisplayOrder = 10 },
                new() { Name = "UV Coating", Category = "Finishing", IsRequired = false, DisplayOrder = 11 }
            },
            "straight tuck" => new List<ProcessDefinition>
            {
                new() { Name = "Perforation", Category = "Cutting", IsRequired = false, DisplayOrder = 10 },
                new() { Name = "Folding", Category = "Assembly", IsRequired = false, DisplayOrder = 11 }
            },
            "auto bottom" => new List<ProcessDefinition>
            {
                new() { Name = "Stitching", Category = "Assembly", IsRequired = true, DisplayOrder = 10 },
                new() { Name = "Handle Attachment", Category = "Special", IsRequired = false, DisplayOrder = 11 }
            },
            "pillow box" => new List<ProcessDefinition>
            {
                new() { Name = "Ribbon Attachment", Category = "Special", IsRequired = false, DisplayOrder = 10 },
                new() { Name = "Embossing", Category = "Finishing", IsRequired = false, DisplayOrder = 11 }
            },
            _ => new List<ProcessDefinition>()
        };
    }

    private List<ProcessDefinition> GetClientSpecificProcesses(string client)
    {
        return client switch
        {
            "akrati offset" => new List<ProcessDefinition>
            {
                new() { Name = "UV Coating", Category = "Finishing", IsRequired = false, DisplayOrder = 20 },
                new() { Name = "Lamination", Category = "Finishing", IsRequired = false, DisplayOrder = 21 }
            },
            _ => new List<ProcessDefinition>()
        };
    }
}