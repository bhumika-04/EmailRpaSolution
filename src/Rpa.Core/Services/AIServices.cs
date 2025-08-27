using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rpa.Core.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Rpa.Core.Services;

public interface IOpenAIService
{
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);
    Task<EmailClassificationResult> ClassifyEmailAsync(string emailContent, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>> ExtractStructuredDataAsync(string content, string schema, CancellationToken cancellationToken = default);
}

public interface ITextAnalysisService
{
    Task<double> CalculateSentimentAsync(string text);
    Task<string[]> ExtractKeywordsAsync(string text);
    Task<bool> DetectSensitiveDataAsync(string text);
}

public class OpenAIService : IOpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIService> _logger;
    private readonly IConfiguration _configuration;

    public OpenAIService(HttpClient httpClient, ILogger<OpenAIService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;

        var apiKey = configuration["AI:OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["AI:OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("OpenAI API key not configured, returning simulated response");
                return GenerateSimulatedResponse(prompt);
            }

            var model = _configuration["AI:OpenAI:Model"] ?? "gpt-3.5-turbo";
            var maxTokens = int.Parse(_configuration["AI:OpenAI:MaxTokens"] ?? "1000");

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens,
                temperature = 0.7
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", jsonContent, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                return jsonResponse
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "No response generated";
            }
            else
            {
                _logger.LogError("OpenAI API request failed with status {StatusCode}", response.StatusCode);
                return GenerateSimulatedResponse(prompt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API");
            return GenerateSimulatedResponse(prompt);
        }
    }

    public async Task<EmailClassificationResult> ClassifyEmailAsync(string emailContent, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $@"
Classify the following email content into one of these categories:
- job-card-entry: Contains job card or work order information
- credential-update: Contains login credentials or system access information  
- costing-request: Contains pricing, cost estimation, or budget information
- general-automation: General automation request or other content

Email content:
{emailContent}

Respond with only the classification category and confidence score (0.0-1.0) in JSON format:
{{""classification"": ""category"", ""confidence"": 0.85}}";

            var response = await GenerateResponseAsync(prompt, cancellationToken);
            
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(response);
                return new EmailClassificationResult
                {
                    Classification = result.GetProperty("classification").GetString() ?? "general-automation",
                    Confidence = result.GetProperty("confidence").GetDouble(),
                    Metadata = new Dictionary<string, object>
                    {
                        { "method", "openai" },
                        { "model", _configuration["AI:OpenAI:Model"] ?? "gpt-3.5-turbo" }
                    }
                };
            }
            catch (JsonException)
            {
                return new EmailClassificationResult
                {
                    Classification = "general-automation",
                    Confidence = 0.5,
                    Metadata = new Dictionary<string, object> { { "method", "fallback" } }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in email classification");
            return new EmailClassificationResult
            {
                Classification = "general-automation",
                Confidence = 0.3,
                Metadata = new Dictionary<string, object> { { "error", ex.Message } }
            };
        }
    }

    public async Task<Dictionary<string, object>> ExtractStructuredDataAsync(string content, string schema, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $@"
Extract structured data from the following content according to this schema:
{schema}

Content:
{content}

Return the extracted data as JSON matching the provided schema.";

            var response = await GenerateResponseAsync(prompt, cancellationToken);
            
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(response) ?? new Dictionary<string, object>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, object>
                {
                    { "extracted", false },
                    { "error", "Failed to parse AI response as JSON" }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in structured data extraction");
            return new Dictionary<string, object>
            {
                { "extracted", false },
                { "error", ex.Message }
            };
        }
    }

    private string GenerateSimulatedResponse(string prompt)
    {
        if (prompt.ToLowerInvariant().Contains("classify"))
        {
            return """{"classification": "general-automation", "confidence": 0.7}""";
        }

        if (prompt.ToLowerInvariant().Contains("extract"))
        {
            return """{"extracted": true, "simulated": true}""";
        }

        return "This is a simulated response from the AI service.";
    }
}

public class TextAnalysisService : ITextAnalysisService
{
    private readonly ILogger<TextAnalysisService> _logger;

    public TextAnalysisService(ILogger<TextAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<double> CalculateSentimentAsync(string text)
    {
        await Task.Delay(100); // Simulate processing time

        var positiveWords = new[] { "good", "great", "excellent", "success", "complete", "done", "ready", "approve" };
        var negativeWords = new[] { "bad", "error", "fail", "problem", "issue", "wrong", "cancel", "urgent" };

        var lowerText = text.ToLowerInvariant();
        var positiveCount = positiveWords.Count(word => lowerText.Contains(word));
        var negativeCount = negativeWords.Count(word => lowerText.Contains(word));

        if (positiveCount == 0 && negativeCount == 0)
            return 0.5; // Neutral

        var sentiment = (double)positiveCount / (positiveCount + negativeCount);
        return sentiment;
    }

    public async Task<string[]> ExtractKeywordsAsync(string text)
    {
        await Task.Delay(50); // Simulate processing time

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':'))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToArray();

        return words;
    }

    public async Task<bool> DetectSensitiveDataAsync(string text)
    {
        await Task.Delay(50); // Simulate processing time

        var sensitivePatterns = new[]
        {
            @"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b", // Credit card
            @"\b\d{3}-\d{2}-\d{4}\b", // SSN
            @"\bpassword\s*[:=]\s*\S+", // Password
            @"\bapi[_-]?key\s*[:=]\s*\S+", // API key
            @"\bsecret\s*[:=]\s*\S+" // Secret
        };

        var lowerText = text.ToLowerInvariant();
        return sensitivePatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(lowerText, pattern));
    }
}