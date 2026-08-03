# Zalo.Net

A high-performance C# SDK for the Zalo Web protocol built on **.NET 10**.

## Features

- **QR Code Authentication**: Initiate QR login, obtain Base64 QR images, and poll confirmation state.
- **Real-Time WebSocket**: Receive messages via WebSockets with automatic exponential backoff reconnects.
- **Messaging Operations**: Send direct/group text messages, quotes/replies, recalls/undo, reactions (Heart, Like, Haha, etc.), stickers, and file attachments.
- **Group & Contact Management**: Create groups, manage members, leave groups, rename groups, find users by phone number, manage friend requests, block/unblock users.
- **Multi-Account Proxy Support**: Assign dedicated `IWebProxy` instances (HTTP/HTTPS/SOCKS5) per Zalo account for HTTP and WebSocket traffic.
- **Diagnostics & Tracing**: Integrated `ZaloDiagnosticsEvents` using standard .NET `DiagnosticSource`.

---

## Solution Architecture

| Project | Description |
| :--- | :--- |
| **`Zalo.Net.Contracts`** | DTOs, Record Models, Exceptions, `IZaloClient`, `ZaloConstants`, `ZaloDiagnosticsEvents`. |
| **`Zalo.Net.Cryptography`** | AES-GCM (SIMD hardware-accelerated), AES-CBC, MD5, and frame codec (`WsFrameCodec`). |
| **`Zalo.Net.Auth`** | `CookieStore`, `ZaloHttpClient` (`SocketsHttpHandler`), and authentication endpoints. |
| **`Zalo.Net.WebSocket`** | Real-time WebSocket engine (`ZaloWsListener`). |
| **`Zalo.Net`** | Main client facade (`ZaloWebClient`) integrating all Web API endpoints. |

---

## Usage Example

```csharp
using System.Net;
using Zalo.Net;
using Zalo.Net.Contracts;

// 1. Restore session material and initialize client with a proxy
ZaloSessionMaterial material = GetSavedMaterial();
using ZaloWebClient client = new(proxy: new WebProxy("http://127.0.0.1:8080"));

ZaloSession session = await ZaloWebClient.LoginWithSessionAsync(material, CancellationToken.None);

// 2. Subscribe to real-time message events
client.MessageReceived += (sender, msg) =>
{
    Console.WriteLine($"[{msg.DisplayName}]: {msg.Content}");
};

// 3. Send text message
await ZaloWebClient.SendTextAsync(session, "USER_ID", ZaloThreadType.User, "Hello!", CancellationToken.None);

// 4. Run real-time WebSocket listener with auto-reconnect
await client.RunWithReconnectAsync(material, CancellationToken.None);
```

---

## License & Copyright

Copyright (c) 2026 PPN Corporation. All rights reserved.

Distributed under the [Apache License 2.0](LICENSE).
