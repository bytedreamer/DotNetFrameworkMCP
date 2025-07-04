# .NET Framework MCP Server for Windows

A TCP-based Model Context Protocol (MCP) server specifically designed for building, testing, and running .NET Framework projects on Windows. This tool enables Claude Code (running in WSL or other environments) to remotely build .NET Framework projects using the Windows build toolchain.

## Purpose

This MCP server provides a bridge between AI assistants (like Claude) and the Windows .NET Framework build environment. It's specifically designed for scenarios where:
- Claude Code is running in WSL/Linux but needs to build Windows .NET Framework projects
- You need to build legacy .NET Framework applications that require Visual Studio MSBuild
- You want AI assistance for .NET Framework development on Windows

## Architecture

```
┌─────────────────┐    TCP/3001    ┌──────────────────┐
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
- **netcat (nc)** for TCP communication: `sudo apt install netcat-openbsd`

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
- **If WSL can't connect**: Try using the Windows host IP instead of localhost:
  ```bash
  # In WSL, find Windows host IP:
  cat /etc/resolv.conf | grep nameserver
  ```
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
    "BuildTimeout": 600000,
    "EnableDetailedLogging": false
  }
}
```

Environment variables use the prefix `MCPSERVER_`, for example:
- `MCPSERVER_DefaultConfiguration=Release`
- `MCPSERVER_EnableDetailedLogging=true`

## Development Status

This project is currently in development. See ProjectPlan.md for the implementation roadmap.

### Completed
- ✅ Project structure setup
- ✅ MCP protocol handling foundation
- ✅ Basic server lifecycle
- ✅ Configuration management
- ✅ Tool handler interface

### In Progress
- 🚧 MSBuild integration
- 🚧 Build output parsing

### TODO
- ⏳ Test runner integration
- ⏳ Project execution
- ⏳ Solution analysis
- ⏳ Package listing
- ⏳ Comprehensive testing
- ⏳ Documentation

## License

[Add your license here]