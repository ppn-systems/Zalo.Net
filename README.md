# Zalo.Net

Thư viện C# SDK hiệu năng cao cho giao thức Zalo Web Client trên nền tảng **.NET 10**.

## Features

- **Xác thực QR Code**: Bắt đầu luồng đăng nhập QR, lấy ảnh mã QR dạng Base64 PNG và kiểm tra trạng thái xác thực.
- **WebSocket Thời Gian Thực**: Lắng nghe tin nhắn Realtime qua WebSocket với cơ chế tự động kết nối lại (Auto-Reconnect) Exponential Backoff.
- **Tương Tác Tin Nhắn**: Gửi tin nhắn cá nhân & nhóm chat, trích dẫn (Quote), thu hồi (Recall), thả cảm xúc (Reaction), gửi Sticker, tệp tin & hình ảnh.
- **Quản Lý Nhóm & Danh Bạ**: Tạo nhóm, thêm/xóa thành viên, rời nhóm, đổi tên nhóm; tìm kiếm người dùng theo số điện thoại, quản lý lời mời kết bạn, chặn/bỏ chặn người dùng.
- **Hỗ Trợ Proxy Đa Tài Khoản**: Gán Proxy riêng (`IWebProxy` HTTP/HTTPS/SOCKS5) cho từng tài khoản Zalo trên cùng 1 Server.
- **Diagnostics Tracing**: Tích hợp sẵn `ZaloDiagnosticsEvents` sử dụng chuẩn `DiagnosticSource` của .NET.

---

## Solution Architecture

| Dự án | Mô tả vai trò |
| :--- | :--- |
| **`Zalo.Net.Contracts`** | DTOs, Record Models, Exceptions, `IZaloClient`, `ZaloConstants`, `ZaloDiagnosticsEvents`. |
| **`Zalo.Net.Cryptography`** | Mã hóa/Giải mã AES-GCM phần cứng Native (SIMD), AES-CBC, MD5 và giải mã khung WebSocket (`WsFrameCodec`). |
| **`Zalo.Net.Auth`** | Quản lý `CookieStore`, `ZaloHttpClient` (`SocketsHttpHandler`) và các API xác thực. |
| **`Zalo.Net.WebSocket`** | Real-time WebSocket engine (`ZaloWsListener`) duy trì kết nối và nhận tin nhắn. |
| **`Zalo.Net`** | Client facade chính (`ZaloWebClient`) tích hợp toàn bộ các API Endpoints. |

---

## Usage Example

```csharp
using System.Net;
using Zalo.Net;
using Zalo.Net.Contracts;

// 1. Nạp phiên đã lưu và khởi tạo client kèm Proxy
ZaloSessionMaterial material = GetSavedMaterial();
using ZaloWebClient client = new(proxy: new WebProxy("http://127.0.0.1:8080"));

ZaloSession session = await ZaloWebClient.LoginWithSessionAsync(material, CancellationToken.None);

// 2. Đăng ký sự kiện nhận tin nhắn Realtime
client.MessageReceived += (sender, msg) =>
{
    Console.WriteLine($"[{msg.DisplayName}]: {msg.Content}");
};

// 3. Gửi tin nhắn văn bản
await ZaloWebClient.SendTextAsync(session, "USER_ID", ZaloThreadType.User, "Xin chào!", CancellationToken.None);

// 4. Lắng nghe WebSocket thời gian thực với cơ chế tự động kết nối lại
await client.RunWithReconnectAsync(material, CancellationToken.None);
```

---

## Disclaimer

> **Chú ý**: *Zalo.Net là thư viện mã nguồn mở độc lập được phát triển phục vụ mục đích nghiên cứu học tập và giao thức kỹ thuật. Thư viện không trực thuộc, liên kết hay được ủy quyền bởi VNG Corporation hay Zalo. Người sử dụng tự chịu mọi trách nhiệm tuân thủ Điều khoản dịch vụ của Zalo khi vận hành.*

---

## License & Copyright

Copyright (c) 2026 PPN Corporation. All rights reserved.

Distributed under the [Apache License 2.0](LICENSE).
