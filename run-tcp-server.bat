@echo off
echo Starting .NET Framework MCP Server in TCP mode...
echo Server will listen on port 3001
echo Press Ctrl+C to stop
echo.

cd /d "%~dp0"

if exist "publish\DotNetFrameworkMCP.Server.exe" (
    publish\DotNetFrameworkMCP.Server.exe --tcp --port 3001
) else (
    echo ERROR: Server executable not found!
    echo Please run build-on-windows.bat first to build the project.
    pause
    exit /b 1
)