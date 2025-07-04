#!/bin/bash

# MCP Bridge Script for WSL
# This script connects to the Windows TCP MCP server from WSL

# Configuration
WINDOWS_HOST="localhost"  # Or use the actual Windows IP if needed
MCP_PORT="3001"

# Check if netcat is available
if ! command -v nc &> /dev/null; then
    echo "Error: netcat (nc) is required but not installed." >&2
    echo "Install it with: sudo apt install netcat-openbsd" >&2
    exit 1
fi

# Connect to the Windows MCP server via TCP
exec nc "$WINDOWS_HOST" "$MCP_PORT"