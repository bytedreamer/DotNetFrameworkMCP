# .NET Framework MCP Server for Windows

A TCP-based Model Context Protocol (MCP) server specifically designed for building, testing, and running .NET Framework projects on Windows. This tool enables Claude Code (running in WSL or other environments) to remotely build .NET Framework projects using the Windows build toolchain.

## Purpose

This MCP server provides a bridge between AI assistants (like Claude) and the Windows .NET Framework build environment. It's specifically designed for scenarios where:
- Claude Code is running in WSL/Linux but needs to build Windows .NET Framework projects
- You need to build legacy .NET Framework applications that require Visual Studio MSBuild
- You want AI assistance for .NET Framework development on Windows

## Architecture

```
┌─────────────────┐    TCP/3001     ┌──────────────────┐
│ Claude Code     │ ──────────────→ │ TCP MCP Server   │
│ (WSL/Linux)     │                 │ (Windows)        │
└─────────────────┘                 └──────────────────┘
                                            │
                                            ▼
                                    ┌──────────────────┐
                                    │ MSBuild.exe      │
                                    │ (Visual Studio)  │
                                    └──────────────────┘
```

## Features

- **TCP-Only Architecture**: Designed for remote access from WSL or other environments
- **Process-Based MSBuild**: Direct execution of MSBuild.exe for maximum compatibility
- **.NET Framework Focus**: Specifically built for .NET Framework projects (not .NET Core/.NET 5+)
- **Build Operations**: Full support for Debug/Release configurations and platform targets
- **Error Parsing**: Structured parsing of MSBuild errors and warnings
- **Minimal Dependencies**: Clean architecture with only essential packages

## Prerequisites

### On Windows (Server Side)
- **Visual Studio 2019/2022** or **Build Tools for Visual Studio**
- **.NET 8.0 SDK** (for running the MCP server itself)
- **Windows OS** (Windows 10/11 or Windows Server)

### On WSL/Linux (Client Side)
- **Claude Code** with MCP support
- **netcat (nc)** for TCP communication (only if using the bridge script): `sudo apt install netcat-openbsd`

## Installation & Setup

### Windows Setup

1. **Clone the repository** on your Windows machine:
   ```cmd
   git clone https://github.com/yourusername/dotnet-framework-mcp
   cd dotnet-framework-mcp
   ```

2. **Build the MCP server**:
   ```cmd
   build-on-windows.bat
   ```
   This creates a self-contained executable in the `publish` folder.

3. **Start the TCP server**:
   ```cmd
   run-tcp-server.bat
   ```
   The server will start listening on port 3001.

### WSL/Linux Setup (Claude Code)

#### How the WSL-to-Windows Bridge Works

Since Claude Code only runs on macOS/Linux, Windows users must run it in WSL2. The bridge enables communication between Claude Code (in WSL2) and the MCP server (on Windows):

1. **The MCP server** runs on Windows as a TCP server listening on port 3001
2. **Claude Code** runs in WSL2 and needs to connect to the Windows MCP server
3. **WSL2 networking** - By default, WSL2 uses NAT-based networking. To connect from WSL2 to Windows:
   - In WSL2's default NAT mode, you may need to use the Windows host IP instead of localhost
   - Windows 11 22H2+ offers "Mirrored Mode" networking which improves localhost connectivity
   - The bridge script is configured to use `localhost` which works in many setups
4. **The bridge script** (`wsl-mcp-bridge.sh`) uses netcat to establish the TCP connection:
   - Claude Code executes the bridge script
   - The script runs `nc localhost 3001` 
   - This connects to the Windows MCP server via WSL2's localhost forwarding
   - All MCP protocol communication flows through this TCP connection

The flow looks like this:
```
Claude Code (WSL2) → Bridge Script → netcat → localhost:3001 → Windows MCP Server
```

You don't directly interact with netcat - it's wrapped inside the bridge script that Claude Code executes automatically.

#### Configuration Steps

1. **Configure Claude Code** by editing `~/.config/Claude/claude_desktop_config.json`:
   ```json
   {
     "mcpServers": {
       "dotnet-framework": {
         "command": "/path/to/wsl-mcp-bridge.sh",
         "env": {
           "MCP_DEBUG": "true"
         }
       }
     }
   }
   ```

2. **Create the bridge script** (adjust the path to your project location):
   ```bash
   #!/bin/bash
   exec nc localhost 3001
   ```

3. **Make it executable**:
   ```bash
   chmod +x wsl-mcp-bridge.sh
   ```

4. **Restart Claude Code** to pick up the configuration.

## MCP Tools

The service implements the following MCP tools:

### build_project
Build a .NET project or solution.

Parameters:
- `path` (string, required): Path to .csproj or .sln file
- `configuration` (string): Build configuration (Debug/Release)
- `platform` (string): Target platform (Any CPU/x86/x64)
- `restore` (boolean): Restore NuGet packages

### run_tests
Run tests in a .NET test project.

Parameters:
- `path` (string, required): Path to test project
- `filter` (string): Test filter expression
- `verbose` (boolean): Enable verbose output

### run_project
Execute a .NET console application.

Parameters:
- `path` (string, required): Path to project
- `args` (array): Command line arguments
- `workingDirectory` (string): Working directory

### analyze_solution
Get information about a solution structure.

Parameters:
- `path` (string, required): Path to .sln file

### list_packages
List NuGet packages in a project.

Parameters:
- `path` (string, required): Path to project

## Testing the Service

### With Claude Code (WSL to Windows)

If Claude Code is running in WSL but you want the MCP server on Windows:

#### Option 1: Build Once, Run Compiled Executable (Recommended)

1. **Build the project on Windows:**
   ```cmd
   build-on-windows.bat
   ```
   This creates a self-contained executable in the `publish` folder.

2. **Start the TCP server:**
   ```cmd
   run-tcp-server.bat
   ```
   Or directly:
   ```cmd
   publish\DotNetFrameworkMCP.Server.exe --port 3001
   ```

#### Option 2: Build and Run Each Time

1. **Start the TCP server (builds if needed):**
   ```cmd
   start-tcp-server.bat
   ```
   Or manually:
   ```cmd
   dotnet run --project src\DotNetFrameworkMCP.Server -- --port 3001
   ```

2. **Configure Claude Code in WSL:**
   ```json
   {
     "mcpServers": {
       "dotnet-framework": {
         "command": "/path/to/project/wsl-mcp-bridge.sh",
         "env": {
           "MCP_DEBUG": "true"
         }
       }
     }
   }
   ```

3. **Make sure netcat is installed in WSL:**
   ```bash
   sudo apt install netcat-openbsd
   ```

#### Benefits of Pre-Building on Windows

- **Faster startup**: No build time when starting the server
- **No build dependencies**: The compiled executable includes all dependencies
- **Consistent behavior**: Same executable every time
- **Easy deployment**: Just copy the `publish` folder

#### Troubleshooting

- **MSBuild errors**: The server will automatically find Visual Studio MSBuild. If you get MSBuild errors:
  - Use `run-tcp-server-vs2022.bat` for explicit VS2022 MSBuild
  - Or set environment variable: `set MSBUILD_PATH=C:\Path\To\MSBuild\Bin`
  - Ensure Visual Studio or Build Tools for Visual Studio is installed
- **If the server doesn't start**: Check Windows Firewall - it may block port 3001
- **If WSL can't connect**: WSL2 networking varies by configuration. Try these solutions:
  ```bash
  # Option 1: Find Windows host IP (for default NAT mode):
  ip route show | grep -i default | awk '{ print $3}'
  # Or check nameserver:
  cat /etc/resolv.conf | grep nameserver
  
  # Option 2: Enable Mirrored Mode networking (Windows 11 22H2+)
  # Add to .wslconfig in your Windows user directory:
  # [wsl2]
  # networkingMode=mirrored
  ```
  Then update `WINDOWS_HOST` in `wsl-mcp-bridge.sh` to use the IP address instead of "localhost"
- **Port already in use**: Change the port in both the server startup and bridge script

### Manual Testing

Use the provided test script:

```bash
./test-tcp-server.sh
```

Or test with netcat:

```bash
echo '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}}' | nc localhost 3001
```

### Available Test Messages

See `test-messages.json` for example MCP protocol messages you can send to test different functionality.

## Configuration

The service can be configured through `appsettings.json` or environment variables:

```json
{
  "McpServer": {
    "MsBuildPath": "auto",
    "DefaultConfiguration": "Debug",
    "DefaultPlatform": "Any CPU",
    "TestTimeout": 300000,
    "BuildTimeout": 1200000,
    "EnableDetailedLogging": false,
    "PreferredVSVersion": "2022",
    "UseDotNetCli": false,
    "DotNetPath": "dotnet"
  }
}
```

### Configuration Options

- **MsBuildPath**: Path to MSBuild.exe or "auto" for automatic detection
- **DefaultConfiguration**: Default build configuration (Debug/Release)
- **DefaultPlatform**: Default target platform (Any CPU/x86/x64)
- **TestTimeout**: Test execution timeout in milliseconds (default: 5 minutes)
- **BuildTimeout**: Build timeout in milliseconds (default: 20 minutes)
- **EnableDetailedLogging**: Enable verbose logging
- **PreferredVSVersion**: Preferred Visual Studio version ("2022", "2019", or "auto")
- **UseDotNetCli**: Use dotnet CLI instead of MSBuild/VSTest (default: false)
- **DotNetPath**: Path to dotnet CLI executable (default: "dotnet")

The **PreferredVSVersion** setting controls which Visual Studio version's MSBuild and VSTest tools to use when multiple versions are installed:
- `"2022"`: Prefer Visual Studio 2022 tools (default)
- `"2019"`: Prefer Visual Studio 2019 tools
- `"auto"`: Use any available version (searches 2022 first, then 2019)

Environment variables use the prefix `MCPSERVER_`, for example:
- `MCPSERVER_DefaultConfiguration=Release`
- `MCPSERVER_EnableDetailedLogging=true`
- `MCPSERVER_PreferredVSVersion=2019`
- `MCPSERVER_UseDotNetCli=true`

### Using dotnet CLI Instead of MSBuild

The MCP server now supports using the dotnet CLI as an alternative to MSBuild/VSTest. This can be useful when:
- You don't have Visual Studio or Build Tools installed
- You prefer using the .NET SDK toolchain
- You're working with newer .NET projects that support the dotnet CLI

To enable dotnet CLI mode:

1. **Via configuration file** (`appsettings.json`):
   ```json
   {
     "McpServer": {
       "UseDotNetCli": true
     }
   }
   ```

2. **Via environment variable**:
   ```cmd
   set MCPSERVER__UseDotNetCli=true
   ```

**Note**: The dotnet CLI has some limitations when working with older .NET Framework projects:
- Some project types may not be fully supported
- Complex build configurations might require MSBuild
- Legacy project formats (.csproj before SDK-style) may have limited support

When `UseDotNetCli` is enabled:
- `dotnet build` is used instead of MSBuild.exe
- `dotnet test` is used instead of VSTest.Console.exe

## Development Status

**🎉 FIRST RELEASE READY** - Core functionality complete for .NET Framework building and testing.

### ✅ Completed (Ready for Release)
- **Core Infrastructure**: MCP protocol, TCP server, configuration management
- **Build System**: MSBuild integration with VS version selection, output parsing, error handling
- **Test Runner**: Multi-framework support (NUnit/xUnit/MSTest) with TRX parsing for detailed results
- **Quality Features**: Cancellation support, intelligent output truncation, comprehensive logging
- **Cross-Platform**: WSL-to-Windows communication bridge for Claude Code integration

### 🚀 Available MCP Tools
- `build_project` - Build .NET Framework solutions and projects
- `run_tests` - Execute tests with detailed error reporting and stack traces

### 📋 Future Enhancements (Post-Release)
- ⏳ `run_project` - Execute console applications  
- ⏳ `analyze_solution` - Solution structure analysis
- ⏳ `list_packages` - NuGet package listing
- ⏳ **Authentication & Security** - API key or token-based authentication for secure access
- ⏳ Enhanced debugging and profiling integration

## Release Notes

### v1.0.0 - First Release
**Core Features:**
- Complete MCP protocol implementation with TCP server for WSL-to-Windows communication
- MSBuild integration with Visual Studio 2019/2022 version selection
- Comprehensive test runner supporting NUnit, xUnit, and MSTest frameworks
- TRX file parsing for accurate error messages, stack traces, and class names
- Intelligent output truncation to comply with Claude Code's 25k token limit
- Robust error handling, build cancellation, and timeout management
- Extensive configuration options via appsettings.json and environment variables

**Requirements:**
- Windows with Visual Studio 2019 or 2022 (or Build Tools)
- .NET Framework projects and solutions
- WSL environment for Claude Code integration (optional)

## License

[Add your license here]