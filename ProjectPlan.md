# .NET Framework MCP Service Project Plan

## Project Overview

Build a Model Context Protocol (MCP) service that enables Claude Code to build, test, and run .NET Framework projects. The service will provide a standardized interface for interacting with MSBuild, test runners, and other .NET tooling.

## Core Requirements

### 1. Build Operations
- Trigger MSBuild for solutions and projects
- Support multiple build configurations (Debug/Release)
- Support multiple platforms (Any CPU, x86, x64)
- Handle NuGet package restoration
- Parse and return structured build output (errors, warnings, success status)

### 2. Test Operations
- Discover test projects and test methods
- Execute all tests or specific test selections
- Support multiple test frameworks (MSTest, NUnit, xUnit)
- Parse and return test results with pass/fail counts and error details

### 3. Run Operations
- Execute console applications with arguments
- Capture and return console output
- Handle process termination and timeouts

### 4. Project Analysis
- List projects in a solution
- Show project dependencies
- Display project properties and configurations
- List NuGet packages and versions

## Technical Architecture

### Technology Stack
- **Language**: C# (.NET 6+ for the MCP service itself)
- **MCP SDK**: Use official MCP SDK for C# (if available) or implement protocol directly
- **Dependencies**:
  - Microsoft.Build for MSBuild integration
  - Microsoft.Build.Locator for finding MSBuild installations
  - Test framework APIs for test discovery/execution

### MCP Methods to Implement

```
tools:
  - name: build_project
    description: Build a .NET project or solution
    inputSchema:
      type: object
      properties:
        path: { type: string, description: "Path to .csproj or .sln file" }
        configuration: { type: string, enum: ["Debug", "Release"], default: "Debug" }
        platform: { type: string, enum: ["Any CPU", "x86", "x64"], default: "Any CPU" }
        restore: { type: boolean, default: true, description: "Restore NuGet packages" }

  - name: run_tests
    description: Run tests in a .NET test project
    inputSchema:
      type: object
      properties:
        path: { type: string, description: "Path to test project" }
        filter: { type: string, description: "Test filter expression" }
        verbose: { type: boolean, default: false }

  - name: run_project
    description: Execute a .NET console application
    inputSchema:
      type: object
      properties:
        path: { type: string, description: "Path to project" }
        args: { type: array, items: { type: string } }
        workingDirectory: { type: string }

  - name: analyze_solution
    description: Get information about a solution structure
    inputSchema:
      type: object
      properties:
        path: { type: string, description: "Path to .sln file" }

  - name: list_packages
    description: List NuGet packages in a project
    inputSchema:
      type: object
      properties:
        path: { type: string, description: "Path to project" }
```

## Implementation Phases

### Phase 1: Core Infrastructure (Week 1) ✅ COMPLETED
- [x] Set up C# project structure
- [x] Implement MCP protocol handling with JSON-RPC support
- [x] Create basic server lifecycle (start, stop, health check)
- [x] Implement logging framework with configurable verbosity
- [x] Add configuration management with environment variable support
- [x] Switch test framework to NUnit
- [x] Create initial unit tests

### Phase 2: Build Functionality (Week 2) ✅ COMPLETED
- [x] Implement MSBuild locator logic with Visual Studio version selection
- [x] Create build_project method
- [x] Add build output parsing with intelligent truncation
- [x] Implement error/warning extraction
- [x] Add NuGet restore functionality
- [x] Add TCP server support for cross-platform communication
- [x] Create WSL-to-Windows bridge for Claude Code integration
- [x] Implement build cancellation and timeout handling
- [x] Add MCP token limit compliance (25k token limit)

### Phase 3: Test Runner Integration (Week 3) ✅ COMPLETED
- [x] Implement test discovery logic
- [x] Add support for MSTest runner
- [x] Add support for NUnit runner  
- [x] Add support for xUnit runner
- [x] Create test result parsing with TRX file support
- [x] Implement test filtering
- [x] Add comprehensive error message and stack trace extraction
- [x] Implement test adapter discovery and integration
- [x] Add solution-based building for proper test configuration

### Phase 4: Project Execution (Week 4)
- [ ] Implement run_project method
- [ ] Add process management
- [ ] Implement output capture
- [ ] Add timeout handling
- [ ] Handle process termination
- [ ] Write unit tests for execution operations

### Phase 5: Analysis Features (Week 5)
- [ ] Implement solution analysis
- [ ] Add project dependency mapping
- [ ] Create package listing functionality
- [ ] Add project property inspection
- [ ] Write unit tests for analysis operations

### Phase 6: Polish & Documentation (Week 6)
- [ ] Comprehensive error handling
- [ ] Performance optimization
- [ ] Add integration tests
- [ ] Write user documentation
- [ ] Create example usage scenarios
- [ ] Package for distribution

## Key Implementation Details

### MSBuild Integration
```csharp
// Use Microsoft.Build.Locator to find MSBuild
MSBuildLocator.RegisterDefaults();

// Use Microsoft.Build API for building
var projectCollection = new ProjectCollection();
var project = projectCollection.LoadProject(projectPath);
```

### Output Parsing Strategy
- Use MSBuild loggers to capture structured output
- Implement custom logger for JSON-formatted results
- Parse compiler error format: `file(line,col): error CODE: message`

### Test Runner Integration
- Use VSTest.Console.exe for universal test execution
- Parse TRX files for structured results
- Support test filtering using standard syntax

### Error Handling
- Graceful handling of missing MSBuild installations
- Clear error messages for missing dependencies
- Timeout handling for long-running operations
- Process cleanup on service shutdown

## Configuration Schema

```json
{
  "McpServer": {
    "MsBuildPath": "auto",
    "DefaultConfiguration": "Debug",
    "DefaultPlatform": "Any CPU",
    "TestTimeout": 300000,
    "BuildTimeout": 1200000,
    "EnableDetailedLogging": false,
    "PreferredVSVersion": "2022"
  }
}
```

## Testing Strategy

### Unit Tests
- Mock MSBuild API calls
- Test output parsing logic
- Verify error handling paths
- Test configuration management

### Integration Tests
- Use sample .NET projects
- Test full build/test/run cycles
- Verify cross-framework compatibility
- Test error scenarios (missing files, bad syntax)

## Distribution Plan

1. **NuGet Package**: Primary distribution as a .NET tool
2. **GitHub Releases**: Compiled binaries with installation script
3. **Docker Image**: Containerized version with MSBuild pre-installed

## Success Criteria

- Successfully builds complex multi-project solutions
- Accurately reports build errors and warnings
- Runs tests from all major test frameworks
- Provides clear, actionable error messages
- Performs operations within reasonable time limits
- Maintains stability during long-running operations

## Future Enhancements

- Support for .NET Core/.NET 5+ projects
- Web project launching with browser integration
- Code coverage reporting
- Incremental build support
- Watch mode for continuous building/testing
- Integration with code analyzers
- Support for F# and VB.NET projects

## Resources & References

- [MCP Specification](https://modelcontextprotocol.io/docs)
- [MSBuild API Documentation](https://docs.microsoft.com/en-us/dotnet/api/microsoft.build)
- [VSTest Documentation](https://docs.microsoft.com/en-us/visualstudio/test/vstest-console-options)
- [NuGet API Reference](https://docs.microsoft.com/en-us/nuget/api/overview)