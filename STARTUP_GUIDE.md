# 🚀 Complete Startup Guide for ERP Automation Testing

## Prerequisites Check

### 1. Required Software Installation
```bash
# Check if .NET 8 is installed
dotnet --version

# Check if SQL Server LocalDB is available
sqllocaldb info

# Check if RabbitMQ is running (if installed)
# Windows: Services → RabbitMQ
# Or download from: https://www.rabbitmq.com/download.html
```

### 2. Install Playwright Browsers (CRITICAL)
```bash
cd E:\AI-RPA\EmailRpaSolution\src\Rpa.Worker
dotnet add package Microsoft.Playwright
pwsh bin/Debug/net8.0/playwright.ps1 install
# OR
playwright install chromium
```

## Database Setup

### 1. Create Database
```bash
cd E:\AI-RPA\EmailRpaSolution\src\Rpa.Core
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
dotnet ef database update
```

## Build Solution

### 1. Clean and Build
```bash
cd E:\AI-RPA\EmailRpaSolution
dotnet clean
dotnet restore
dotnet build --configuration Debug
```

## Start Services (3 Terminals)

### Terminal 1: RPA Worker (Main Automation)
```bash
cd E:\AI-RPA\EmailRpaSolution\src\Rpa.Worker
dotnet run

# Should show:
# info: Rpa.Worker.Worker[0] RPA Worker started at: [timestamp]
# info: Microsoft.Hosting.Lifetime[0] Application started...
```

### Terminal 2: Email Listener
```bash
cd E:\AI-RPA\EmailRpaSolution\src\Rpa.Listener  
dotnet run

# Should show:
# info: Rpa.Listener.Worker[0] Email Listener started at: [timestamp]
# info: Starting email monitoring...
```

### Terminal 3: Notifier Service
```bash
cd E:\AI-RPA\EmailRpaSolution\src\Rpa.Notifier
dotnet run

# Should show:
# info: Rpa.Notifier.Worker[0] Notifier started at: [timestamp]
# info: Listening for notifications...
```

## Quick Test Without Email

### Direct Test Option
```bash
cd E:\AI-RPA\EmailRpaSolution\VisualRpaTest
dotnet run

# This will run the ERP automation directly without email
```

## Configuration Check

### Update Email Settings (If Using Email)
Edit `src/Rpa.Listener/appsettings.json`:
```json
{
  "Email": {
    "Imap": {
      "Host": "imap.gmail.com",
      "Port": 993,
      "Username": "bhumika.indasanalytics@gmail.com",
      "Password": "YOUR_APP_PASSWORD"
    },
    "AllowedSenders": [
      "your-test-email@gmail.com"
    ]
  }
}
```

## Expected Startup Logs

### Worker Service Should Show:
```
info: Rpa.Worker.Worker[0] RPA Worker started at: 2025-01-14T10:00:00
info: Microsoft.Extensions.Hosting.Internal.Host[1] Hosting environment: Development
info: Microsoft.Extensions.Hosting.Internal.Host[2] Content root path: E:\AI-RPA\EmailRpaSolution\src\Rpa.Worker
info: Microsoft.Hosting.Lifetime[0] Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0] Hosting environment: Development
```

### Browser Should Launch:
- Chrome browser window opens (because `"Headless": false`)
- You should see the browser navigating to ERP system

## Troubleshooting

### Common Issues:

1. **"No such file or directory"**
   - Make sure you're in Windows Command Prompt or PowerShell
   - Use backslashes: `cd E:\AI-RPA\EmailRpaSolution`

2. **"dotnet command not found"**
   - Install .NET 8 SDK from: https://dotnet.microsoft.com/download

3. **Database errors**
   - Install SQL Server LocalDB
   - Run the database update commands above

4. **Playwright browser errors**
   - Run the Playwright install commands above
   - Make sure Chromium is installed

5. **RabbitMQ connection errors**
   - Install RabbitMQ or disable message queue in config

## Manual Test Steps

### 1. Test Browser Automation Only
Create a simple test file `TestRun.cs`:
```csharp
using Rpa.Worker.Automation;
using Rpa.Core.Models;

var erpData = new ErpJobData
{
    CompanyLogin = new CompanyLogin { CompanyName = "indusweb", Password = "123" },
    UserLogin = new UserLogin { Username = "Admin", Password = "99811" },
    JobDetails = new JobDetails { Client = "Akrati Offset", Content = "Reverse Tuck In", Quantity = 10000 },
    // ... rest of test data
};

var processor = new ErpEstimationProcessor(logger, configuration);
var result = await processor.ProcessEstimationWorkflowAsync(erpData);
```

### 2. Send Test Email
Send to: `bhumika.indasanalytics@gmail.com`
Subject: `ERP ESTIMATION - Test Request`
Body: [Your test email content]

## Success Indicators

✅ All 3 services start without errors  
✅ Browser opens and navigates to ERP system  
✅ Database connection successful  
✅ Email monitoring active (if configured)  
✅ Ready to process automation requests  

## Next Steps After Startup

1. Send your test email
2. Watch the logs for processing
3. Browser should automatically:
   - Navigate to http://13.200.122.70/
   - Login with company credentials
   - Login with user credentials  
   - Navigate to estimation page
   - Fill all planning sheet segments
   - Take screenshot of results
4. Receive email response with costing screenshot