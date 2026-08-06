# Zalo.Net.Mcp - Model Context Protocol (MCP) Server for Zalo

⚡ **High-performance Model Context Protocol (MCP) Server for Zalo Messaging & AI Smart Memory** 🚀

`Zalo.Net.Mcp` bridges **Zalo messaging, contact search, group management, and AI smart CRM memory** directly into your favorite AI Clients: **ChatGPT Desktop**, **ChatGPT Web (Custom GPT Actions)**, **Claude Desktop**, **Antigravity IDE**, and **Cursor / VS Code**.

---

## 🌟 Key Features

- **25 Full MCP Tools**: Covers 100% of Zalo SDK endpoints (Messaging, Groups, Friends, Smart Search, CRM Insights, Reminders, Bank Card Payloads).
- **Dual Transport Protocols**:
  1. **Stdio JSON-RPC 2.0**: Native stdio transport for Claude Desktop, ChatGPT Desktop, Antigravity IDE, Cursor.
  2. **ASP.NET Core Web REST API (`--web`)**: Hosts OpenAPI 3.0 schema at `http://localhost:5000/openapi.json` for ChatGPT Web Custom GPT Actions.
- **SQLite FTS5 Sub-Millisecond Search**: Embedded SQLite WAL mode database (`zalo_data.db`) with full-text search index and automatic regex entity extraction (Bank accounts, Phone numbers, URLs, Reminders).
- **Docker Container Support**: 24/7 Cloud deployment (`ghcr.io/ppn-systems/zalo-net-mcp:latest`).
- **1-Click Self-Setup (`--setup`)**: Auto-configures `mcp.json` for ChatGPT Desktop, Claude Desktop, Antigravity, and Cursor.

---

## 📦 Downloads & Installation

### Option 1: Direct Executable Downloads (Pre-compiled Single File)
Download pre-compiled standalone single-file executables from [GitHub Releases](https://github.com/ppn-systems/Zalo.Net/releases):
- 🪟 **Windows x64**: `zalo-net-mcp-win-x64.exe`
- 🍏 **macOS Intel (x64)**: `zalo-net-mcp-osx-x64`
- 🍏 **macOS Apple Silicon (ARM64)**: `zalo-net-mcp-osx-arm64`
- 🐧 **Linux x64**: `zalo-net-mcp-linux-x64`

### Option 2: Docker Container (24/7 Cloud / VPS Deployment)
Pull and run the pre-built Docker image from GitHub Container Registry (GHCR):

```bash
docker run -d \
  -p 5000:5000 \
  -v zalo_data:/root/.local/share/Zalo.Net.Mcp \
  --name zalo-mcp \
  ghcr.io/ppn-systems/zalo-net-mcp:latest
```

OpenAPI Schema available at: `http://localhost:5000/openapi.json`

---

## 🛠️ CLI Commands & Usage

| Command | Description |
| :--- | :--- |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --setup` | 1-Click auto-configuration for AI Desktop Clients. |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --login` | Interactive terminal QR code login with auto image opener. |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --diagnose` | Run system diagnostics & session status check. |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --web --port 5000` | Run ASP.NET Core Web server hosting OpenAPI schema. |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --import-session <path>` | Import session material directly into SQLite database. |
| `dotnet run --project mcp/Zalo.Net.Mcp -- --test-all-tools` | Run 100% automated test suite across all 25 MCP tools. |

---

## 🔄 CI/CD & Build Matrix (GitHub Actions)

Workflow defined in `.github/workflows/build-mcp.yml`:
- **Selective Build Dispatch (`workflow_dispatch`)**: Choose specific targets (`all`, `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`, `docker`) to optimize GitHub Actions runner minutes.
- **Cross-platform Ubuntu Build**: Cross-compiles single-file executables on `ubuntu-latest` (saving 10x macOS runner minute costs).
- **Auto Release Attachment**: Attaches build artifacts directly to GitHub Releases.
- **Automated GHCR Docker Push**: Builds multi-arch Docker image and pushes to `ghcr.io/ppn-systems/zalo-net-mcp:latest`.

---

## 📄 License

Licensed under Apache 2.0. Copyright (c) 2026 PPN Corporation.
