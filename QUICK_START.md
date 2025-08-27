# Quick Start Guide - ERP Automation Testing

## Prerequisites Check

### 1. Install Required Software
```bash
# Install .NET 8 SDK
# Install SQL Server LocalDB or SQL Server
# Install RabbitMQ Server
```

### 2. Install Playwright Browsers
```bash
cd src/Rpa.Worker
dotnet run --project . --playwright install
```

## Database Setup

### 1. Create Database Migration
```bash
cd src/Rpa.Core
dotnet ef migrations add InitialCreate
dotnet ef database update
```

## Testing Steps

### Phase 1: Basic Component Testing

#### 1. Test Email Classification & Extraction
```bash
cd tests/Rpa.Core.Tests
dotnet test --filter "EmailClassifierService"
```

#### 2. Test Database Operations
```bash
cd tests/Rpa.Core.Tests
dotnet test --filter "RpaDbContextTests"
```

#### 3. Test Worker Components
```bash
cd tests/Rpa.Worker.Tests
dotnet test --filter "ErpEstimationProcessor"
```

### Phase 2: Integration Testing

#### 1. Start RabbitMQ Server
- Start RabbitMQ service
- Access management UI at http://localhost:15672 (guest/guest)

#### 2. Test Email Listener (Manual)
```bash
cd src/Rpa.Listener
dotnet run
```
**Send test email to:** bhumika.indasanalytics@gmail.com

#### 3. Test Worker Service
```bash
cd src/Rpa.Worker
dotnet run
```

#### 4. Test Notifier Service
```bash
cd src/Rpa.Notifier
dotnet run
```

### Phase 3: End-to-End Testing

#### Sample Test Email Format:
```
Subject: ERP ESTIMATION - Test Request

Company Log-IN:
Company Name: indusweb
Password: 123
User Log-IN:
Username : Admin
Password: 99811
Client: Akrati Offset
Content: Reverse Tuck In
Quantity: 10000
Job Size (mm):
  Height = 100
  Length = 150
  Width  = 50
  O.flap = 20
  P.flap = 20
Material:
  Quality = Real Art Paper
  GSM     = 100
  Mill    = JK
  Finish  = Gloss
Printing:
  Front Colors   = 4
  Back Colors    = 0
  Special Front  = 0
  Special Back   = 0
  Style          = Single Side
  Plate          = CTP Plate
Wastage & Finishing:
  Make Ready Sheets = 0
  Wastage Type      = Standard
  Grain Direction   = Across
  Online Coating    = None
  Trimming (T/B/L/R)= 0/0/0/0
  Striping (T/B/L/R)= 0/0/0/0
```

## Monitoring & Debugging

### 1. Check Logs
- Listener logs: `src/Rpa.Listener/logs/`
- Worker logs: `src/Rpa.Worker/logs/`
- Notifier logs: `src/Rpa.Notifier/logs/`

### 2. Check Database
```sql
-- Check job status
SELECT * FROM Jobs ORDER BY CreatedAt DESC;

-- Check for processing errors
SELECT * FROM Jobs WHERE Status = 'Failed';
```

### 3. Browser Automation (Visual Debug)
Set in `appsettings.json`:
```json
"Browser": {
  "Headless": false,
  "SlowMo": 2000
}
```

## Troubleshooting

### Common Issues:
1. **Email Connection**: Verify Gmail settings and app password
2. **RabbitMQ**: Ensure service is running
3. **Database**: Check connection string and LocalDB
4. **ERP Access**: Verify ERP system is accessible at http://13.200.122.70/
5. **Playwright**: Run browser install if automation fails

## Success Indicators:
✅ Email received and classified as "erp-estimation-workflow"
✅ Job created in database with extracted data
✅ Browser automation completes all 16 steps
✅ Screenshot captured of costing results
✅ Email sent back with screenshot attachment