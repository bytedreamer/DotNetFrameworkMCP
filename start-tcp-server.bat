@echo off
echo Starting .NET Framework MCP Server in TCP mode...
echo Server will listen on port 3001
echo Press Ctrl+C to stop
echo.

cd /d "%~dp0"

if exist "publish\DotNetFrameworkMCP.Server.exe" (
    echo Using compiled executable...
    publish\DotNetFrameworkMCP.Server.exe --port 3001
) else (
    echo Using dotnet run (building if needed)...
    dotnet run --project "src\DotNetFrameworkMCP.Server" -- --port 3001
)

pause