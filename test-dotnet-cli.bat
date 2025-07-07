@echo off
echo Testing dotnet CLI support...
echo.
echo Starting server with dotnet CLI enabled...

REM Build the server first
cd src\DotNetFrameworkMCP.Server
dotnet build

REM Run with dotnet CLI enabled
set MCPSERVER__UseDotNetCli=true
set MCPSERVER__EnableDetailedLogging=true

echo.
echo Running server with dotnet CLI support enabled...
dotnet run -- --port 3001