#!/bin/bash

# Test script to interact with the MCP server
cd "/mnt/c/Users/work-tower/Projects/Open/MCP For .Net Framework"

echo "Testing MCP Server..."

# Start the server and send test messages
{
    echo '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "1.0"}}'
    sleep 0.1
    echo '{"jsonrpc": "2.0", "id": 2, "method": "tools/list"}'
    sleep 0.1
    echo '{"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "build_project", "arguments": {"path": "./src/DotNetFrameworkMCP.Server/DotNetFrameworkMCP.Server.csproj", "configuration": "Debug", "restore": true}}}'
    sleep 0.1
} | dotnet run --project src/DotNetFrameworkMCP.Server