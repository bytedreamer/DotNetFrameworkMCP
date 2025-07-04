@echo off
echo Starting .NET Framework MCP Server in TCP mode...
echo Server will listen on port 3001
echo Press Ctrl+C to stop

cd /d "%~dp0"
dotnet run --project "src\DotNetFrameworkMCP.Server" -- --tcp --port 3001

pause