# Claude Development Guidelines

## Project Information

This is a .NET Framework MCP (Model Context Protocol) server for Windows that enables building, testing, and running .NET Framework projects remotely.

## Testing Framework

**Use NUnit for all tests** - This project uses NUnit, not xUnit or MSTest.

### Test Structure:
```csharp
using NUnit.Framework;

[TestFixture]
public class MyServiceTests
{
    [SetUp]
    public void SetUp()
    {
        // Initialize test dependencies
    }

    [TearDown] 
    public void TearDown()
    {
        // Cleanup resources
    }

    [Test]
    public void MyMethod_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        // Act
        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("value1", "expected1")]
    [TestCase("value2", "expected2")]
    public void MyMethod_WithDifferentInputs_ReturnsExpectedResults(string input, string expected)
    {
        // Test implementation
    }
}
```

### NUnit Assertions:
- Use `Assert.That(actual, Is.EqualTo(expected))` instead of `Assert.Equal()`
- Use `Assert.That(collection, Has.Count.EqualTo(3))` for collections
- Use `Assert.That(text, Does.Contain("substring"))` for string checks
- Use `Times.Once` with Moq for verifications

## Build Commands

### Development:
```bash
# Build the server
dotnet build src/DotNetFrameworkMCP.Server

# Run tests
dotnet test

# Run with dotnet CLI enabled
set MCPSERVER__UseDotNetCli=true
dotnet run --project src/DotNetFrameworkMCP.Server -- --port 3001
```

### IMPORTANT: Pre-Commit Workflow
**ALWAYS build and test before committing!**

```bash
# 1. Build to ensure no compilation errors
dotnet build

# 2. Run tests to ensure functionality works
dotnet test

# 3. Only commit if both succeed
git add -A
git commit -m "Your commit message"
```

If build or tests fail, fix the issues before committing. Never commit broken code.

### Production:
```bash
# Build self-contained executable
build-on-windows.bat

# Run the compiled executable
run-tcp-server.bat
```

## Architecture Notes

- Uses Strategy pattern for build/test executors
- MSBuild vs dotnet CLI selection via configuration
- Factory pattern for executor creation
- Services delegate to executors, no conditional logic
- All executors implement `IBuildExecutor` or `ITestExecutor`

## Configuration

Key settings in `appsettings.json`:
- `UseDotNetCli`: Switch between MSBuild and dotnet CLI
- `PreferredVSVersion`: VS version preference for MSBuild
- `BuildTimeout`: Build operation timeout
- `TestTimeout`: Test operation timeout