# Zalo.Net SDK — Ví Dụ Hướng Dẫn Lập Trình (Samples)

Thư mục **`samples/`** chứa các dự án mẫu minh họa cách tích hợp bộ thư viện **Zalo.Net** trong các ứng dụng C# / .NET 10.

---

## 📁 Danh Sách Dự Án Mẫu

### 1. [Zalo.Console](file:///e:/Cs/Zalo.Net/samples/Zalo.Console)
Ứng dụng Console C# minh họa toàn bộ vòng đời ứng dụng Zalo Client:
- **Đăng nhập bằng mã QR Code**: Hiển thị QR Code trên Terminal và chờ ứng dụng điện thoại quét xác nhận.
- **Tự động lưu và khôi phục Session**: Mã hóa và lưu trữ `ZaloSessionMaterial` cục bộ để tái sử dụng mà không cần quét lại mã QR.
- **Lắng nghe sự kiện WebSocket Realtime**: Nhận sự kiện tin nhắn đến (`MessageReceived`), cập nhật trạng thái online/offline của bạn bè.
- **Gửi tin nhắn & Tương tác**: Gửi tin nhắn văn bản, trích dẫn (reply), thả biểu tượng cảm xúc (reactions), và gửi tệp tin đính kèm.

---

## 🛠️ Hướng Dẫn Chạy Mẫu Console

Mở Terminal tại thư mục gốc dự án và chạy:

```bash
dotnet run --project samples/Zalo.Console
```

---

## 💻 Cấu Trúc Mã Nguồn Cơ Bản

```csharp
using Zalo.Net;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.WebSocket;

// 1. Tạo WebSocket & Session Manager
using ZaloWebClient client = new();

// 2. Bắt đầu luồng đăng nhập QR
ZaloQrSession qr = await ZaloWebClient.StartQrLoginAsync();
Console.WriteLine($"Quét mã QR tại: {qr.QrUrl}");

// 3. Lắng nghe tin nhắn WebSocket realtime
using ZaloWebSocketListener listener = new(session);
listener.MessageReceived += (sender, msg) =>
{
    Console.WriteLine($"[TIN NHẮN ĐẾN] {msg.DisplayName}: {msg.Content}");
};
await listener.StartAsync();
```
