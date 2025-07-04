#!/bin/bash

echo "Testing TCP MCP Server..."

# Start the server in background
cd "/mnt/c/Users/work-tower/Projects/Open/MCP For .Net Framework"
dotnet run --project src/DotNetFrameworkMCP.Server -- --tcp --port 3001 &
SERVER_PID=$!

# Wait a moment for server to start
sleep 2

echo "Server started with PID $SERVER_PID"
echo "Testing connection..."

# Test the server
{
    echo '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "1.0"}}'
    sleep 0.1
    echo '{"jsonrpc": "2.0", "id": 2, "method": "tools/list"}'
    sleep 0.1
    echo '{"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "build_project", "arguments": {"path": "./src/DotNetFrameworkMCP.Server/DotNetFrameworkMCP.Server.csproj", "configuration": "Debug", "restore": true}}}'
    sleep 1
} | nc localhost 3001

echo "Test completed. Stopping server..."
kill $SERVER_PID
wait $SERVER_PID 2>/dev/null
echo "Server stopped."