# 🚀 Zalo.Net

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/ppn-systems/Zalo.Net)
[![Framework](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Native AOT](https://img.shields.io/badge/Native%20AOT-Ready-success.svg)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)

**Zalo.Net** là thư viện C# SDK mã nguồn mở, hiệu năng cao, nhẹ và hiện đại dành cho giao thức **Zalo Web Client** trên nền tảng **.NET 10**.

Được thiết kế theo kiến trúc doanh nghiệp (Enterprise Architecture), **Zalo.Net** hỗ trợ Native AOT & Trimming, mã hóa tăng tốc phần cứng SIMD (Hardware Acceleration), hỗ trợ Proxy riêng cho từng tài khoản và tích hợp sẵn hệ thống Diagnostics Event qua `DiagnosticSource`.

---

## 🌟 Tính Năng Nổi Bật

- 📱 **Đăng Nhập QR Code**: Khởi tạo luồng quét mã QR, tự động tạo ảnh QR Base64 PNG và Polling trạng thái xác thực.
- ⚡ **Realtime WebSocket Engine**: Tự động duy trì kết nối WebSocket thời gian thực với cơ chế tự động kết nối lại theo chiến lược Exponential Backoff.
- 💬 **Tương Tác Tin Nhắn Đa Dạng**:
  - Gửi tin nhắn cá nhân (User DM) và nhóm chat (Group).
  - Trích dẫn tin nhắn trả lời (**Quote / Reply**).
  - Thu hồi tin nhắn (**Recall / Undo**).
  - Thả cảm xúc biểu tượng (**Reactions**: Heart, Like, Haha, Wow, Cry, Angry, v.v.).
  - Gửi nhãn dán (**Stickers**) và Đính kèm hình ảnh/tệp tin (**Attachments**).
- 👥 **Quản Lý Nhóm Chat (Group Management)**:
  - Lấy danh sách nhóm chat đã tham gia.
  - Tạo nhóm chat mới.
  - Thêm / Xóa thành viên khỏi nhóm.
  - Rời nhóm chat (Silent / Bình thường).
  - Đổi tên hiển thị nhóm chat.
- 📇 **Quản Lý Bạn Bè & Danh Bạ**:
  - Tìm kiếm tài khoản Zalo theo Số điện thoại.
  - Gửi & Chấp nhận lời mời kết bạn.
  - Chặn & Bỏ chặn người dùng.
  - Đổi biệt danh (Alias) bạn bè phục vụ theo dõi CRM.
- 🌐 **Hỗ Trợ Proxy Đa Tài Khoản (`IWebProxy`)**:
  - Gán Proxy riêng (HTTP, HTTPS, SOCKS5) cho từng tài khoản Zalo trên cùng 1 Server.
  - Áp dụng đồng bộ trên cả luồng Web API lẫn Realtime WebSocket.
- 📊 **Diagnostics Tracing chuẩn .NET**: Tích hợp `DiagnosticSource` (`ZaloDiagnosticsEvents`) phát sự kiện chi tiết 100% các thao tác API & khung tin nhắn WebSocket.
- 🧩 **Hỗ Trợ Dependency Injection & Mocking**: Giao diện `IZaloClient` sẵn sàng đăng ký IoC Container trong ASP.NET Core và viết Unit Tests.
- 🚀 **Zero Third-Party Crypto Dependencies**: Mã hóa AES-GCM tăng tốc phần cứng Native (SIMD/AES-NI) không dùng thư viện ngoài.

---

## 📦 Cài Đặt

Cài đặt package thông qua NuGet Package Manager CLI:

```bash
dotnet add package Zalo.Net
```

Hoặc qua Package Manager Console trong Visual Studio:

```powershell
Install-Package Zalo.Net
```

---

## 🚀 Hướng Dẫn Sử Dụng Nhanh (Quick Start)

### 1. Luồng Đăng Nhập Bằng Mã QR (QR Login)

```csharp
using Zalo.Net;
using Zalo.Net.Contracts;

using ZaloWebClient client = new();

// 1. Bắt đầu luồng đăng nhập QR
ZaloQrSession qrSession = await client.StartQrLoginAsync();
Console.WriteLine($"Mã QR Code: {qrSession.QrCode}");
Console.WriteLine($"Ảnh QR Base64: {qrSession.QrImageBase64[..30]}...");

// 2. Lắng nghe trạng thái quét mã từ ứng dụng Zalo Mobile
while (true)
{
    ZaloLoginState state = await client.PollQrStatusAsync(qrSession.SessionId);
    Console.WriteLine($"Trạng thái hiện tại: {state.Status}");

    if (state.Status == ZaloLoginStatus.Connected)
    {
        // Đăng nhập thành công! Lấy thông tin phiên (Session Material)
        ZaloSessionMaterial? material = client.ConsumePendingMaterial(qrSession.SessionId);
        string json = System.Text.Json.JsonSerializer.Serialize(material);
        File.WriteAllText("zalo_session.json", json);
        Console.WriteLine("Đã lưu phiên làm việc vào zalo_session.json!");
        break;
    }

    if (state.Status is ZaloLoginStatus.Expired or ZaloLoginStatus.Declined)
    {
        Console.WriteLine("Phiên đăng nhập QR đã hết hạn hoặc bị từ chối.");
        break;
    }

    await Task.Delay(2000);
}
```

---

### 2. Nạp Phiên & Lắng Nghe Tin Nhắn Realtime WebSocket

```csharp
using Zalo.Net;
using Zalo.Net.Contracts;

// Nạp phiên từ file đã lưu
string json = File.ReadAllText("zalo_session.json");
ZaloSessionMaterial material = System.Text.Json.JsonSerializer.Deserialize<ZaloSessionMaterial>(json)!;

// Khởi tạo phiên Zalo
ZaloSession session = await ZaloWebClient.LoginWithSessionAsync(material, CancellationToken.None);

using ZaloWebClient client = new();

// Đăng ký nhận tin nhắn Realtime
client.MessageReceived += (sender, msg) =>
{
    Console.WriteLine($"[TIN NHẮN MỚI] Từ: {msg.DisplayName} ({msg.UidFrom}) | Nội dung: {msg.Content}");
};

client.StatusChanged += (sender, status) =>
{
    Console.WriteLine($"[TRẠNG THÁI KẾT NỐI] {status.Status} - {status.Reason}");
};

// Chạy Listener với cơ chế tự động kết nối lại (Auto Reconnect)
await client.RunWithReconnectAsync(material, CancellationToken.None);
```

---

### 3. Gửi Tin Nhắn, Trích Dẫn & Thả Cảm Xúc

```csharp
// 1. Gửi tin nhắn văn bản
ZaloSendResult sendResult = await ZaloWebClient.SendTextAsync(
    session,
    threadId: "6586684611809565061",
    threadType: ZaloThreadType.User,
    text: "Xin chào từ Zalo.Net SDK!",
    cancellationToken: CancellationToken.None);

// 2. Gửi tin nhắn trích dẫn (Quote / Reply)
string quoteMsgId = await ZaloWebClient.SendQuoteMessageAsync(
    session,
    threadId: "6586684611809565061",
    threadType: ZaloThreadType.User,
    text: "Tôi đã nhận được tin nhắn của bạn!",
    quoteMsgId: sendResult.MsgId,
    quoteCliMsgId: sendResult.MsgId,
    quoteSenderUid: "6586684611809565061",
    quoteContent: "Xin chào từ Zalo.Net SDK!");

// 3. Thả cảm xúc biểu tượng (Reaction)
await ZaloWebClient.AddReactionAsync(
    session,
    threadId: "6586684611809565061",
    msgId: sendResult.MsgId,
    cliMsgId: sendResult.MsgId,
    type: ZaloThreadType.User,
    reaction: ZaloReactionType.Heart);

// 4. Thu hồi tin nhắn (Recall / Undo)
await ZaloWebClient.UndoMessageAsync(
    session,
    threadId: "6586684611809565061",
    msgId: sendResult.MsgId,
    cliMsgId: sendResult.MsgId,
    type: ZaloThreadType.User);
```

---

### 4. Chạy Đa Tài Khoản Trên 1 Server Với Proxy Riêng (`IWebProxy`)

```csharp
using System.Net;
using Zalo.Net;
using Zalo.Net.Contracts;

// Cấu hình Proxy riêng cho Tài khoản A
WebProxy proxyAccountA = new("http://192.168.1.100:8080")
{
    Credentials = new NetworkCredential("proxy_user", "proxy_pass")
};

// Đăng nhập phiên tài khoản A định tuyến qua Proxy A
ZaloSession sessionA = await ZaloWebClient.LoginWithSessionAsync(
    materialA,
    CancellationToken.None,
    proxy: proxyAccountA);

// Kết nối Realtime WebSocket cũng đi qua Proxy A
using ZaloWebClient clientA = new(proxy: proxyAccountA);
await clientA.StartListenerAsync(sessionA, CancellationToken.None);
```

---

### 5. Đăng Ký Dependency Injection Trong ASP.NET Core

```csharp
using Zalo.Net;
using Zalo.Net.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký ZaloClient vào IoC Container
builder.Services.AddSingleton<IZaloClient, ZaloWebClient>();

var app = builder.Build();
```

---

## 🏛️ Cấu Trúc Dự Án (Project Architecture)

| Assembly | Mô tả vai trò |
| :--- | :--- |
| **`Zalo.Net.Contracts`** | Chứa DTOs, Record Models, Enum, Exceptions, Hằng số tập trung `ZaloConstants`, `ZaloDiagnosticsEvents` và Interface `IZaloClient`. Zero dependencies ngoài BCL. |
| **`Zalo.Net.Cryptography`** | Thư viện mã hóa AES-GCM (SIMD hardware-accelerated), AES-CBC One-Shot, MD5 stackalloc và WsFrameCodec. |
| **`Zalo.Net.Auth`** | Quản lý CookieStore, `ZaloHttpClient` (nền `SocketsHttpHandler`) và Endpoints xác thực. |
| **`Zalo.Net.WebSocket`** | Realtime WebSocket Engine (`ZaloWsListener`) duy trì kết nối, giải mã khung nhị phân và gửi Ping Heartbeat. |
| **`Zalo.Net`** | Client Facade chính (`ZaloWebClient`) tích hợp toàn bộ Web API Endpoints. |

---

## 📄 License & Copyright

Copyright (c) 2026 PPN Corporation. All rights reserved.

Dự án được phân phối dưới giấy phép [Apache License 2.0](LICENSE).
