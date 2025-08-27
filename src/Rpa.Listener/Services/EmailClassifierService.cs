using Microsoft.Extensions.Configuration;
using Rpa.Core.Models;
using Rpa.Core.Services;
using System.Text.RegularExpressions;

namespace Rpa.Listener;

public class EmailClassifierService : IEmailClassifier
{
    private readonly ILogger<EmailClassifierService> _logger;
    private readonly IConfiguration _configuration;

    public EmailClassifierService(ILogger<EmailClassifierService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<EmailClassificationResult> ClassifyAsync(EmailMessage emailMessage)
    {
        try
        {
            var classification = await ClassifyByRules(emailMessage);
            
            if (classification.Confidence < 0.7)
            {
                classification = await ClassifyByAI(emailMessage);
            }

            _logger.LogDebug("Email classified as {classification} with confidence {confidence}", 
                classification.Classification, classification.Confidence);

            return classification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying email");
            return new EmailClassificationResult
            {
                Classification = "general-automation",
                Confidence = 0.5,
                Metadata = new Dictionary<string, object> { { "error", ex.Message } }
            };
        }
    }

    private async Task<EmailClassificationResult> ClassifyByRules(EmailMessage emailMessage)
    {
        var subject = emailMessage.Subject.ToLowerInvariant();
        var body = emailMessage.Body.ToLowerInvariant();
        var combined = $"{subject} {body}";

        // Check for ERP estimation workflow pattern first
        if (IsErpEstimationWorkflow(body))
        {
            return new EmailClassificationResult
            {
                Classification = "erp-estimation-workflow",
                Confidence = 0.95,
                Metadata = new Dictionary<string, object>
                {
                    { "method", "erp-pattern-detection" },
                    { "workflowType", "estimation" }
                }
            };
        }

        var patterns = new Dictionary<string, (string[] patterns, double confidence)>
        {
            ["job-card-entry"] = (new[] { 
                "job card", "job number", "work order", "project card", 
                "job id", "task card", "job entry" 
            }, 0.9),
            
            ["credential-update"] = (new[] { 
                "username", "password", "login", "credentials", 
                "access", "authentication", "login details" 
            }, 0.85),
            
            ["costing-request"] = (new[] { 
                "cost", "costing", "estimate", "budget", "pricing", 
                "quote", "financial", "expense" 
            }, 0.8),
            
            ["general-automation"] = (new[] { 
                "automation", "process", "task", "execute", "run" 
            }, 0.6)
        };

        foreach (var (classification, (keywords, confidence)) in patterns)
        {
            var matches = keywords.Count(keyword => combined.Contains(keyword));
            if (matches > 0)
            {
                var calculatedConfidence = Math.Min(confidence, (double)matches / keywords.Length + 0.5);
                return new EmailClassificationResult
                {
                    Classification = classification,
                    Confidence = calculatedConfidence,
                    Metadata = new Dictionary<string, object>
                    {
                        { "matchedKeywords", keywords.Where(k => combined.Contains(k)).ToArray() },
                        { "matchCount", matches },
                        { "method", "rule-based" }
                    }
                };
            }
        }

        return new EmailClassificationResult
        {
            Classification = "unknown",
            Confidence = 0.1,
            Metadata = new Dictionary<string, object> { { "method", "rule-based-fallback" } }
        };
    }

    private bool IsErpEstimationWorkflow(string body)
    {
        var erpIndicators = new[]
        {
            "company log-in:",
            "user log-in:",
            "job size (mm):",
            "material:",
            "printing:",
            "wastage & finishing:",
            "quantity:",
            "client:"
        };

        var matchCount = erpIndicators.Count(indicator => body.Contains(indicator.ToLowerInvariant()));
        return matchCount >= 5; // Require at least 5 matching indicators
    }

    private async Task<EmailClassificationResult> ClassifyByAI(EmailMessage emailMessage)
    {
        await Task.Delay(100);

        var bodyLength = emailMessage.Body.Length;
        var hasJobTerms = Regex.IsMatch(emailMessage.Body, @"\b(job|card|number|id)\b", RegexOptions.IgnoreCase);
        var hasCredentialTerms = Regex.IsMatch(emailMessage.Body, @"\b(username|password|login|credential)\b", RegexOptions.IgnoreCase);
        var hasCostTerms = Regex.IsMatch(emailMessage.Body, @"\b(cost|costing|price|budget|estimate)\b", RegexOptions.IgnoreCase);

        string classification;
        double confidence;

        if (hasJobTerms && bodyLength > 50)
        {
            classification = "job-card-entry";
            confidence = 0.8;
        }
        else if (hasCredentialTerms)
        {
            classification = "credential-update";
            confidence = 0.85;
        }
        else if (hasCostTerms)
        {
            classification = "costing-request";
            confidence = 0.75;
        }
        else
        {
            classification = "general-automation";
            confidence = 0.6;
        }

        return new EmailClassificationResult
        {
            Classification = classification,
            Confidence = confidence,
            Metadata = new Dictionary<string, object>
            {
                { "method", "ai-simulation" },
                { "bodyLength", bodyLength },
                { "hasJobTerms", hasJobTerms },
                { "hasCredentialTerms", hasCredentialTerms },
                { "hasCostTerms", hasCostTerms }
            }
        };
    }
}

public class DataExtractorService : IDataExtractor
{
    private readonly ILogger<DataExtractorService> _logger;

    public DataExtractorService(ILogger<DataExtractorService> logger)
    {
        _logger = logger;
    }

    public async Task<ExtractedCredentials?> ExtractCredentialsAsync(string emailBody)
    {
        try
        {
            var usernameMatch = Regex.Match(emailBody, @"(?:username|user|login):\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
            var passwordMatch = Regex.Match(emailBody, @"(?:password|pass|pwd):\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
            var urlMatch = Regex.Match(emailBody, @"(?:url|link|system):\s*(https?://[^\s\r\n]+)", RegexOptions.IgnoreCase);

            if (usernameMatch.Success || passwordMatch.Success || urlMatch.Success)
            {
                return new ExtractedCredentials
                {
                    Username = usernameMatch.Success ? usernameMatch.Groups[1].Value : null,
                    Password = passwordMatch.Success ? passwordMatch.Groups[1].Value : null,
                    SystemUrl = urlMatch.Success ? urlMatch.Groups[1].Value : null,
                    AdditionalParameters = new Dictionary<string, string>()
                };
            }

            return await Task.FromResult<ExtractedCredentials?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting credentials from email body");
            return null;
        }
    }

    public async Task<JobCardInfo?> ExtractJobCardInfoAsync(string emailBody)
    {
        try
        {
            var jobNumberMatch = Regex.Match(emailBody, @"(?:job\s*(?:number|id|card)):\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
            var descriptionMatch = Regex.Match(emailBody, @"(?:description|desc):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var customerMatch = Regex.Match(emailBody, @"(?:customer|client):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var costMatch = Regex.Match(emailBody, @"(?:cost|amount|price):\s*[\$]?([0-9,]+\.?[0-9]*)", RegexOptions.IgnoreCase);

            if (jobNumberMatch.Success)
            {
                var jobCard = new JobCardInfo
                {
                    JobNumber = jobNumberMatch.Groups[1].Value.Trim(),
                    Description = descriptionMatch.Success ? descriptionMatch.Groups[1].Value.Trim() : null,
                    CustomerName = customerMatch.Success ? customerMatch.Groups[1].Value.Trim() : null,
                    AdditionalFields = new Dictionary<string, object>()
                };

                if (costMatch.Success && decimal.TryParse(costMatch.Groups[1].Value.Replace(",", ""), out var cost))
                {
                    jobCard.EstimatedCost = cost;
                }

                return jobCard;
            }

            return await Task.FromResult<JobCardInfo?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting job card info from email body");
            return null;
        }
    }

    public async Task<Dictionary<string, object>> ExtractStructuredDataAsync(string emailBody, string classificationType)
    {
        var data = new Dictionary<string, object>();

        try
        {
            switch (classificationType.ToLowerInvariant())
            {
                case "erp-estimation-workflow":
                    var erpData = await ExtractErpJobDataAsync(emailBody);
                    if (erpData != null)
                    {
                        data["erpJobData"] = erpData;
                    }
                    break;

                case "job-card-entry":
                    var jobInfo = await ExtractJobCardInfoAsync(emailBody);
                    if (jobInfo != null)
                    {
                        data["jobCard"] = jobInfo;
                    }
                    break;

                case "credential-update":
                    var credentials = await ExtractCredentialsAsync(emailBody);
                    if (credentials != null)
                    {
                        data["credentials"] = credentials;
                    }
                    break;

                case "costing-request":
                    var costMatches = Regex.Matches(emailBody, @"[\$]?([0-9,]+\.?[0-9]*)", RegexOptions.IgnoreCase);
                    if (costMatches.Count > 0)
                    {
                        data["amounts"] = costMatches.Select(m => m.Groups[1].Value).ToArray();
                    }
                    break;
            }

            data["extractedAt"] = DateTime.UtcNow;
            data["classification"] = classificationType;

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting structured data for type {classificationType}", classificationType);
            return data;
        }
    }

    public async Task<ErpJobData?> ExtractErpJobDataAsync(string emailBody)
    {
        try
        {
            var erpData = new ErpJobData();

            // Extract Company Login
            var companyNameMatch = Regex.Match(emailBody, @"Company Name:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var companyPasswordMatch = Regex.Match(emailBody, @"Company Log-IN:[\s\S]*?Password:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            
            if (companyNameMatch.Success && companyPasswordMatch.Success)
            {
                erpData.CompanyLogin = new CompanyLogin
                {
                    CompanyName = companyNameMatch.Groups[1].Value.Trim(),
                    Password = companyPasswordMatch.Groups[1].Value.Trim()
                };
            }

            // Extract User Login
            var userNameMatch = Regex.Match(emailBody, @"Username\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var userPasswordMatch = Regex.Match(emailBody, @"User Log-IN:[\s\S]*?Password:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            
            if (userNameMatch.Success && userPasswordMatch.Success)
            {
                erpData.UserLogin = new UserLogin
                {
                    Username = userNameMatch.Groups[1].Value.Trim(),
                    Password = userPasswordMatch.Groups[1].Value.Trim()
                };
            }

            // Extract Job Details
            var clientMatch = Regex.Match(emailBody, @"Client:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var contentMatch = Regex.Match(emailBody, @"Content:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var quantityMatch = Regex.Match(emailBody, @"Quantity:\s*(\d+)", RegexOptions.IgnoreCase);

            if (clientMatch.Success || contentMatch.Success || quantityMatch.Success)
            {
                erpData.JobDetails = new JobDetails
                {
                    Client = clientMatch.Success ? clientMatch.Groups[1].Value.Trim() : "",
                    Content = contentMatch.Success ? contentMatch.Groups[1].Value.Trim() : "",
                    Quantity = quantityMatch.Success ? int.Parse(quantityMatch.Groups[1].Value) : 0
                };
            }

            // Extract Job Size
            var heightMatch = Regex.Match(emailBody, @"Height\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var lengthMatch = Regex.Match(emailBody, @"Length\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var widthMatch = Regex.Match(emailBody, @"Width\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var oflapMatch = Regex.Match(emailBody, @"O\.flap\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var pflapMatch = Regex.Match(emailBody, @"P\.flap\s*=\s*(\d+)", RegexOptions.IgnoreCase);

            if (heightMatch.Success || lengthMatch.Success || widthMatch.Success)
            {
                erpData.JobSize = new JobSize
                {
                    Height = heightMatch.Success ? int.Parse(heightMatch.Groups[1].Value) : 0,
                    Length = lengthMatch.Success ? int.Parse(lengthMatch.Groups[1].Value) : 0,
                    Width = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0,
                    OFlap = oflapMatch.Success ? int.Parse(oflapMatch.Groups[1].Value) : 0,
                    PFlap = pflapMatch.Success ? int.Parse(pflapMatch.Groups[1].Value) : 0
                };
            }

            // Extract Material
            var qualityMatch = Regex.Match(emailBody, @"Quality\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var gsmMatch = Regex.Match(emailBody, @"GSM\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var millMatch = Regex.Match(emailBody, @"Mill\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var finishMatch = Regex.Match(emailBody, @"Finish\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);

            if (qualityMatch.Success || gsmMatch.Success || millMatch.Success || finishMatch.Success)
            {
                erpData.Material = new Material
                {
                    Quality = qualityMatch.Success ? qualityMatch.Groups[1].Value.Trim() : "",
                    Gsm = gsmMatch.Success ? int.Parse(gsmMatch.Groups[1].Value) : 0,
                    Mill = millMatch.Success ? millMatch.Groups[1].Value.Trim() : "",
                    Finish = finishMatch.Success ? finishMatch.Groups[1].Value.Trim() : ""
                };
            }

            // Extract Printing Details
            var frontColorsMatch = Regex.Match(emailBody, @"Front Colors\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var backColorsMatch = Regex.Match(emailBody, @"Back Colors\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var specialFrontMatch = Regex.Match(emailBody, @"Special Front\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var specialBackMatch = Regex.Match(emailBody, @"Special Back\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var styleMatch = Regex.Match(emailBody, @"Style\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var plateMatch = Regex.Match(emailBody, @"Plate\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);

            if (frontColorsMatch.Success || backColorsMatch.Success)
            {
                erpData.PrintingDetails = new PrintingDetails
                {
                    FrontColors = frontColorsMatch.Success ? int.Parse(frontColorsMatch.Groups[1].Value) : 0,
                    BackColors = backColorsMatch.Success ? int.Parse(backColorsMatch.Groups[1].Value) : 0,
                    SpecialFront = specialFrontMatch.Success ? int.Parse(specialFrontMatch.Groups[1].Value) : 0,
                    SpecialBack = specialBackMatch.Success ? int.Parse(specialBackMatch.Groups[1].Value) : 0,
                    Style = styleMatch.Success ? styleMatch.Groups[1].Value.Trim() : "",
                    Plate = plateMatch.Success ? plateMatch.Groups[1].Value.Trim() : ""
                };
            }

            // Extract Wastage & Finishing
            var makeReadySheetsMatch = Regex.Match(emailBody, @"Make Ready Sheets\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var wastageTypeMatch = Regex.Match(emailBody, @"Wastage Type\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var grainDirectionMatch = Regex.Match(emailBody, @"Grain Direction\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var onlineCoatingMatch = Regex.Match(emailBody, @"Online Coating\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var trimmingMatch = Regex.Match(emailBody, @"Trimming \(T/B/L/R\)\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var stripingMatch = Regex.Match(emailBody, @"Striping \(T/B/L/R\)\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);

            erpData.WastageFinishing = new WastageFinishing
            {
                MakeReadySheets = makeReadySheetsMatch.Success ? int.Parse(makeReadySheetsMatch.Groups[1].Value) : 0,
                WastageType = wastageTypeMatch.Success ? wastageTypeMatch.Groups[1].Value.Trim() : "",
                GrainDirection = grainDirectionMatch.Success ? grainDirectionMatch.Groups[1].Value.Trim() : "",
                OnlineCoating = onlineCoatingMatch.Success ? onlineCoatingMatch.Groups[1].Value.Trim() : "",
                Trimming = trimmingMatch.Success ? trimmingMatch.Groups[1].Value.Trim() : "",
                Striping = stripingMatch.Success ? stripingMatch.Groups[1].Value.Trim() : ""
            };

            return erpData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting ERP job data from email body");
            return null;
        }
    }
}