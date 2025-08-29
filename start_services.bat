@echo off
echo Starting ERP RPA Automation Services...
echo.

echo Checking prerequisites...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET is not installed or not in PATH
    pause
    exit /b 1
)

echo .NET is available
echo.

echo Starting services in separate windows...

echo Starting RPA Worker (Main Automation)...
start "RPA Worker" cmd /k "cd /d E:\AI-RPA\EmailRpaSolution\src\Rpa.Worker && dotnet run"

timeout /t 3 /nobreak >nul

echo Starting Email Listener...
start "Email Listener" cmd /k "cd /d E:\AI-RPA\EmailRpaSolution\src\Rpa.Listener && dotnet run"

timeout /t 3 /nobreak >nul

echo Starting Notifier Service...
start "Notifier Service" cmd /k "cd /d E:\AI-RPA\EmailRpaSolution\src\Rpa.Notifier && dotnet run"

echo.
echo All services are starting in separate windows...
echo.
echo Look for these windows:
echo - "RPA Worker" - Main automation engine
echo - "Email Listener" - Email monitoring
echo - "Notifier Service" - Email responses
echo.
echo Once all services show "Application started", you can send your test email to:
echo bhumika.indasanalytics@gmail.com
echo.
echo Press any key to exit this launcher...
pause >nul