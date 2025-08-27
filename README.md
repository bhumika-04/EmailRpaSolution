# Email-Driven RPA Automation Bot

A comprehensive .NET 8 solution for email-driven automation using AI and RPA technologies.

## 🏗️ Architecture Overview

This solution implements an end-to-end email-driven automation workflow with the following components:

```
[Email Inbox] → [Listener] → [Message Queue] → [Worker] → [Database]
                                                   ↓
[Email Response] ← [Notifier] ← [Message Queue] ← [AI Processing]
```

## 📁 Project Structure

```
EmailRpaSolution/
├── src/
│   ├── Rpa.Core/           # Shared library (models, data, services)
│   ├── Rpa.Listener/       # Email ingestion service
│   ├── Rpa.Worker/         # Core automation with Playwright
│   └── Rpa.Notifier/       # Email notification service
└── tests/
    ├── Rpa.Core.Tests/     # Unit tests for core library
    └── Rpa.Worker.Tests/   # Unit tests for worker service
```

## 🚀 Features

### Email Processing
- **IMAP Email Monitoring**: Continuously monitors inbox for new automation requests
- **AI-Powered Classification**: Automatically classifies emails by type and intent
- **Smart Data Extraction**: Extracts credentials, job cards, and structured data from emails
- **Sender Validation**: Configurable whitelist of allowed senders

### Automation Capabilities
- **Web Automation**: Playwright-based browser automation for ERP systems
- **Multi-Browser Support**: Chromium, Firefox, and WebKit support
- **Intelligent Element Detection**: Robust element finding with multiple selector strategies
- **Error Handling**: Comprehensive error handling with retry mechanisms

### AI Integration
- **Email Classification**: OpenAI integration for content analysis
- **Data Extraction**: AI-powered extraction of structured data
- **Anomaly Detection**: Intelligent detection of processing anomalies
- **Sentiment Analysis**: Text sentiment analysis for quality assurance

### Queue Management
- **RabbitMQ Integration**: Durable message queuing with dead letter handling
- **Horizontal Scaling**: Multi-worker support for high-throughput processing
- **Retry Logic**: Configurable retry mechanisms with exponential backoff

### Database Management
- **Entity Framework Core**: Modern ORM with SQL Server support
- **Job Tracking**: Comprehensive job status and result tracking
- **Audit Trail**: Full audit trail of processing activities

## 🔧 Configuration

### Email Settings (appsettings.json)

```json
{
  "Email": {
    "Imap": {
      "Host": "imap.gmail.com",
      "Port": 993,
      "Username": "your-email@gmail.com",
      "Password": "your-app-password"
    },
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "Username": "your-email@gmail.com",
      "Password": "your-app-password"
    },
    "AllowedSenders": [
      "automation@company.com",
      "erp@company.com"
    ],
    "SubjectPatterns": [
      "JOB CARD",
      "AUTOMATION REQUEST",
      "ERP ENTRY"
    ]
  }
}
```

### Database Connection

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=RpaAutomationDb;Trusted_Connection=true"
  }
}
```

### RabbitMQ Configuration

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

## 🚀 Getting Started

### Prerequisites

- .NET 8.0 SDK
- SQL Server (LocalDB or full instance)
- RabbitMQ Server
- Email account with IMAP/SMTP access

### Installation

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd EmailRpaSolution
   ```

2. **Install Playwright browsers**
   ```bash
   cd src/Rpa.Worker
   dotnet run --project . --playwright install
   ```

3. **Set up database**
   ```bash
   cd src/Rpa.Core
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

4. **Configure settings**
   - Update `appsettings.json` in each service project
   - Set email credentials (use app passwords for Gmail)
   - Configure RabbitMQ connection

5. **Build solution**
   ```bash
   dotnet build
   ```

### Running the Services

Start each service in separate terminals:

```bash
# Terminal 1: Listener Service
cd src/Rpa.Listener
dotnet run

# Terminal 2: Worker Service  
cd src/Rpa.Worker
dotnet run

# Terminal 3: Notifier Service
cd src/Rpa.Notifier
dotnet run
```

## 📧 Email Formats

### Job Card Entry Example

```
Subject: JOB CARD - New Manufacturing Order

Job Number: JOB-2024-001
Description: Custom widget manufacturing
Customer: Acme Corporation
Estimated Cost: $2,500.00

System: https://erp.company.com
Username: automation_user
Password: secure_password123
```

### Credential Update Example

```
Subject: AUTOMATION REQUEST - Update ERP Credentials

System URL: https://erp.company.com/login
Username: new_automation_user
Password: new_secure_password456
Domain: COMPANY
```

## 🔍 Monitoring & Logging

- **Structured Logging**: Serilog with console and file outputs
- **Health Checks**: Built-in health checks for dependencies
- **Job Tracking**: Comprehensive job status tracking in database
- **Error Notifications**: Automatic error notifications via email

## 🧪 Testing

Run unit tests:

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 📊 Workflow Details

1. **Email Ingestion**: Listener monitors IMAP inbox for new emails
2. **AI Classification**: Emails are classified using AI/ML models
3. **Data Extraction**: Structured data is extracted from email content
4. **Queue Processing**: Jobs are queued for processing
5. **RPA Execution**: Worker processes automation tasks using Playwright
6. **Validation**: Results are validated and errors detected
7. **Notifications**: Status notifications sent back to originators

## 🔐 Security Considerations

- **Credential Handling**: Secure storage and handling of extracted credentials
- **Input Validation**: Comprehensive validation of email inputs
- **Sender Authentication**: Whitelist-based sender validation
- **Audit Logging**: Complete audit trail of all activities
- **Error Handling**: Safe error handling without credential exposure

## 🛠️ Customization

### Adding New Automation Types

1. Extend `JobType` enumeration in `Job.cs`
2. Add classification logic in `EmailClassifierService.cs`
3. Implement processing logic in `WebsiteProcessor.cs`
4. Update email templates in `TemplateService.cs`

### Custom AI Models

Replace `OpenAIService` implementation with your preferred AI service:
- Azure Cognitive Services
- Google Cloud AI
- Custom ML models
- Local LLM integration

### Additional Queue Providers

Extend `IMessageQueue` interface for:
- Azure Service Bus
- AWS SQS
- Apache Kafka
- Redis Pub/Sub

## 📈 Performance & Scaling

- **Horizontal Scaling**: Multiple worker instances supported
- **Queue Durability**: Messages persist across service restarts
- **Database Optimization**: Indexed queries for efficient job retrieval
- **Resource Management**: Configurable browser timeouts and limits

## 🐛 Troubleshooting

### Common Issues

1. **Email Connection Failures**
   - Verify IMAP/SMTP settings
   - Check firewall/network connectivity
   - Ensure app passwords are used for Gmail

2. **Browser Automation Issues**
   - Install Playwright browsers: `playwright install`
   - Check headless mode settings
   - Verify target website accessibility

3. **Queue Connection Issues**
   - Ensure RabbitMQ server is running
   - Verify connection credentials
   - Check network connectivity

### Debug Mode

Set `Browser:Headless: false` in configuration to see browser automation in action.

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## 📞 Support

For support and questions:
- Create an issue in the repository
- Check the troubleshooting guide
- Review the configuration documentation