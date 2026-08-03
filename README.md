# Zalo.Net

Thư viện C# SDK cho giao thức Zalo Web Client trên nền tảng **.NET 10**.

## 📌 Tính năng chính

- **Đăng nhập QR Code**: Khởi tạo luồng quét mã QR, lấy ảnh Base64 và polling trạng thái xác thực.
- **Realtime WebSocket**: Lắng nghe tin nhắn thời gian thực, tự động kết nối lại (Auto-Reconnect).
- **Tương tác tin nhắn**: Gửi tin nhắn cá nhân & nhóm, trích dẫn (Quote), thu hồi (Recall), thả cảm xúc (Reaction), gửi Sticker/Ảnh/File.
- **Quản lý Nhóm & Danh bạ**: Tạo nhóm, thêm/xóa thành viên, rời nhóm, đổi tên nhóm; tìm kiếm bạn bè theo số điện thoại, kết bạn, chặn người dùng.
- **Hỗ trợ Proxy (`IWebProxy`)**: Gán Proxy riêng (HTTP/HTTPS/SOCKS5) cho từng tài khoản Zalo.
- **Chẩn đoán & Trace**: Tích hợp `ZaloDiagnosticsEvents` qua `DiagnosticSource`.

---

## 🏛️ Cấu trúc dự án

| Dự án | Vai trò |
| :--- | :--- |
| **`Zalo.Net.Contracts`** | DTOs, Record Models, Exceptions, `IZaloClient`, `ZaloConstants`, `ZaloDiagnosticsEvents`. |
| **`Zalo.Net.Cryptography`** | Thư viện mã hóa AES-GCM, AES-CBC, MD5 và giải mã khung WebSocket (`WsFrameCodec`). |
| **`Zalo.Net.Auth`** | Quản lý CookieStore, `ZaloHttpClient` và các API xác thực. |
| **`Zalo.Net.WebSocket`** | Engine WebSocket (`ZaloWsListener`) nhận tin nhắn thời gian thực. |
| **`Zalo.Net`** | Client facade chính (`ZaloWebClient`) tích hợp toàn bộ các API Endpoints. |

---

## 💡 Ví dụ sử dụng

```csharp
using System.Net;
using Zalo.Net;
using Zalo.Net.Contracts;

// 1. Đăng nhập với Session đã lưu
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

// 4. Lắng nghe WebSocket thời gian thực
await client.RunWithReconnectAsync(material, CancellationToken.None);
```

---

## 📄 Bản quyền & Giấy phép

Copyright (c) 2026 PPN Corporation. All rights reserved.

Dự án được phân phối dưới giấy phép [Apache License 2.0](LICENSE).
