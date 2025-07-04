@echo off
echo Building .NET Framework MCP Server...
echo.

cd /d "%~dp0"

echo Restoring packages...
dotnet restore

echo.
echo Building Release configuration...
dotnet build -c Release

echo.
echo Publishing self-contained executable...
dotnet publish src\DotNetFrameworkMCP.Server -c Release -r win-x64 --self-contained -o publish

echo.
echo Build complete! 
echo Executable location: publish\DotNetFrameworkMCP.Server.exe
echo.
echo You can now run the server with:
echo   publish\DotNetFrameworkMCP.Server.exe --tcp --port 3001
echo.
pause