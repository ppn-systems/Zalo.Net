# Zalo.Net.Mcp — Model Context Protocol Server for Zalo API

**`Zalo.Net.Mcp`** là một MCP Server chuẩn (.NET 10) cung cấp khả năng tích hợp trọn bộ tính năng Zalo API SDK cho các Trợ lý Trí tuệ Nhân tạo (AI Assistants) như **ChatGPT (Desktop App & Custom GPT Actions)**, **Claude Desktop**, **Antigravity IDE**, và **Cursor / VS Code**.

---

## 🌟 Tính Năng Nổi Bật

- **Full 25 MCP Tools**: Hỗ trợ đầy đủ tất cả các tính năng Nhắn tin, Gửi file, Nhãn dán, Thẻ ngân hàng, Danh thiếp, Quản lý bạn bè và Nhóm chat Zalo.
- **SQLite FTS5 Full-Text Search**: Đánh chỉ mục cơ sở dữ liệu tin nhắn cục bộ (`zalo_data.db`), tìm kiếm tin nhắn với tốc độ dưới **1ms**.
- **Smart AI Memory & Entity Extraction**: Tự động bóc tách Số tài khoản ngân hàng (STK), Số điện thoại, Links, và Lịch hẹn/Cuộc họp từ tin nhắn Zalo đến.
- **Đa Nền Tảng AI Client**:
  - **ChatGPT Desktop App**: Tự động cài đặt cấu hình 1-click qua lệnh `--setup`.
  - **ChatGPT Web (Custom GPT Actions)**: Hỗ trợ Web Action Server (`--web --port 5000`) cung cấp file OpenAPI Schema chuẩn `http://localhost:5000/openapi.json`.
  - **Claude Desktop / Antigravity IDE / Cursor**: Tự động nhận cấu hình Stdio MCP.
- **Hiệu năng vượt trội**: Tích hợp **System.Text.Json Source Generator**, không sử dụng Reflection runtime, tối ưu hóa bộ nhớ cho Native AOT / Single-File.

---

## 🚀 Hướng Dẫn Sử Dụng Lệnh CLI

### 1. Cài Đặt Tự Động Cho Các Ứng Dụng AI (`--setup`)
Cài đặt/cập nhật file cấu hình MCP cho ChatGPT Desktop, Claude Desktop, Antigravity IDE, và Cursor:
```bash
dotnet run --project mcp/Zalo.Net.Mcp -- --setup
```

### 2. Đăng Nhập QR Code Trực Tiếp Từ Terminal (`--login`)
Bắt đầu luồng đăng nhập QR Code và lưu phiên làm việc (Session) an toàn vào Local SQLite DB:
```bash
dotnet run --project mcp/Zalo.Net.Mcp -- --login
```

### 3. Chẩn Đoán Sức Khỏe Hệ Thống (`--diagnose`)
Kiểm tra độ trễ SQLite Database và trạng thái file cấu hình của các ứng dụng AI:
```bash
dotnet run --project mcp/Zalo.Net.Mcp -- --diagnose
```

### 4. Khởi Chạy Web Action Server Cho ChatGPT Web Custom GPTs (`--web`)
Mở HTTP Action Server cung cấp OpenAPI JSON Schema tại `http://localhost:5000/openapi.json`:
```bash
dotnet run --project mcp/Zalo.Net.Mcp -- --web --port 5000
```

---

## 📦 Đóng Gói Binary Độc Lập Single-File

Để đóng gói file thực thi đơn lẻ (Self-Contained Single File) không cần cài .NET SDK:

### Chạy PowerShell script tự động đóng gói cả 4 hệ điều hành:
```powershell
.\pack-mcp.ps1
```

### Hoặc build thủ công cho từng nền tảng:
- **macOS Intel (`osx-x64`)**:
  ```bash
  dotnet publish mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
  ```
- **macOS Apple Silicon (`osx-arm64`)**:
  ```bash
  dotnet publish mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
  ```
- **Windows x64 (`win-x64`)**:
  ```bash
  dotnet publish mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
