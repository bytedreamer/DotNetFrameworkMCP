# .NET Framework MCP Service

A Model Context Protocol (MCP) service that enables Claude Code to build, test, and run .NET Framework projects through standardized operations.

## Features

- **Build Operations**: Trigger MSBuild for solutions and projects with support for multiple configurations and platforms
- **Test Execution**: Run tests from MSTest, NUnit, and xUnit frameworks
- **Project Execution**: Run console applications with arguments and capture output
- **Solution Analysis**: Analyze solution structure, dependencies, and NuGet packages

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- MSBuild (usually comes with Visual Studio or Build Tools for Visual Studio)
- .NET Framework projects to work with

### Building the Service

```bash
dotnet build
```

### Running the Service

```bash
dotnet run --project src/DotNetFrameworkMCP.Server
```

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

### With Claude Code (Same Machine)

Add this configuration to your Claude Code MCP settings:

```json
{
  "mcpServers": {
    "dotnet-framework": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/your/project/src/DotNetFrameworkMCP.Server"
      ],
      "env": {
        "MCPSERVER_EnableDetailedLogging": "true"
      }
    }
  }
}
```

### With Claude Code (WSL to Windows)

If Claude Code is running in WSL but you want the MCP server on Windows:

1. **Start the TCP server on Windows:**
   ```cmd
   start-tcp-server.bat
   ```
   Or manually:
   ```cmd
   dotnet run --project src\DotNetFrameworkMCP.Server -- --tcp --port 3001
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

### Manual Testing

Use the provided test script:

```bash
./test-server.sh
```

Or send MCP messages manually:

```bash
echo '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "1.0"}}' | dotnet run --project src/DotNetFrameworkMCP.Server
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