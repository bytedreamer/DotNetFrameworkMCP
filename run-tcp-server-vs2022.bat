@echo off
echo Starting .NET Framework MCP Server with Visual Studio 2022 MSBuild...
echo Server will listen on port 3001
echo Press Ctrl+C to stop
echo.

cd /d "%~dp0"

REM Set MSBuild path to VS2022 (adjust path as needed)
setlocal EnableDelayedExpansion

set "paths[0]=C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin"
set "paths[1]=C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin"
set "paths[2]=C:\Program Files (x86)\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin"
set "paths[3]=C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin"
set "paths[4]=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"
set "paths[5]=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin"
set "paths[6]=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin"
set "paths[7]=C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin"
set "paths[8]=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin"
set "paths[9]=C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"

set "MSBUILD_PATH="
for /L %%i in (0,1,9) do (
    call set "currentPath=%%paths[%%i]%%"
    if exist "!currentPath!" (
        set "MSBUILD_PATH=!currentPath!"
        goto :found
    )
)

:found
if defined MSBUILD_PATH (
    echo Using MSBuild from: %MSBUILD_PATH%
) else (
    echo Error: MSBuild path not found.
    pause
)

echo.

REM Run DotNetFramework MCP Server
if exist "publish\DotNetFrameworkMCP.Server.exe" (
    echo Using compiled executable...
    "publish\DotNetFrameworkMCP.Server.exe" --port 3001
) else (
    echo Using dotnet run, building if needed...
    dotnet run --project "src\DotNetFrameworkMCP.Server" -- --port 3001
)

pause