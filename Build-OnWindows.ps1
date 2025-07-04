#!/usr/bin/env pwsh

Write-Host "Building .NET Framework MCP Server..." -ForegroundColor Green
Write-Host ""

# Change to script directory
Set-Location $PSScriptRoot

Write-Host "Restoring packages..." -ForegroundColor Yellow
dotnet restore

Write-Host ""
Write-Host "Building Release configuration..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
dotnet publish src\DotNetFrameworkMCP.Server -c Release -r win-x64 --self-contained -o publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Executable location: publish\DotNetFrameworkMCP.Server.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now run the server with:" -ForegroundColor Yellow
Write-Host "  .\publish\DotNetFrameworkMCP.Server.exe --tcp --port 3001" -ForegroundColor White
Write-Host ""