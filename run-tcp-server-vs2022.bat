@echo off
echo Starting .NET Framework MCP Server with Visual Studio 2022 MSBuild...
echo Server will listen on port 3001
echo Press Ctrl+C to stop
echo.

cd /d "%~dp0"

REM Set MSBuild path to VS2022 (adjust path as needed)
set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin"
if not exist "%MSBUILD_PATH%" (
    set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin"
)
if not exist "%MSBUILD_PATH%" (
    set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin"
)
if not exist "%MSBUILD_PATH%" (
    set "MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"
)

echo Using MSBuild from: %MSBUILD_PATH%

if exist "publish\DotNetFrameworkMCP.Server.exe" (
    echo Using compiled executable...
    publish\DotNetFrameworkMCP.Server.exe --port 3001
) else (
    echo Using dotnet run (building if needed)...
    dotnet run --project "src\DotNetFrameworkMCP.Server" -- --port 3001
)

pause