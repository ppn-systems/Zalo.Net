using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net;
using Zalo.Net.Contracts;

namespace Zalo.Console;

internal static class Program
{
    private const string SessionFilePath = "session.json";
    private const string QrCodeFilePath = "qrcode.png";

    private static async Task Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine("       Zalo.Net - Live Login & Test CLI         ");
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine();

        using ZaloWebClient client = new();
        ZaloSessionMaterial? material = await LoadSessionAsync();

        if (material == null)
        {
            System.Console.WriteLine("🔑 Chưa tìm thấy session cũ. Bắt đầu luồng Đăng nhập bằng mã QR...");
            material = await PerformQrLoginLoopAsync(client);
            if (material == null)
            {
                System.Console.WriteLine("❌ Đăng nhập bị hủy hoặc thất bại.");
                return;
            }
            await SaveSessionAsync(material);
            System.Console.WriteLine("💾 Đã lưu session vào session.json cho các lần sử dụng sau!");
        }
        else
        {
            System.Console.WriteLine("✅ Đã tải session từ session.json.");
        }

        System.Console.WriteLine("🔌 Đang kết nối tới Zalo Session...");
        using CancellationTokenSource cts = new();

        ZaloSession session;
        try
        {
            session = await ZaloWebClient.LoginWithSessionAsync(material, cts.Token);
            System.Console.WriteLine($"🎉 Đăng nhập thành công! UID: {session.Uid}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ Lỗi đăng nhập session: {ex.Message}");
            System.Console.WriteLine("Xóa file session.json cũ và chạy lại ứng dụng để quét mã QR mới.");
            if (File.Exists(SessionFilePath))
            {
                File.Delete(SessionFilePath);
            }
            return;
        }

        // Lắng nghe sự kiện tin nhắn WebSocket thời gian thực
        client.MessageReceived += (sender, msgEvent) =>
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine($"\n[TIN NHẮN MỚI] Từ UID: {msgEvent.UidFrom} | Nội dung: {msgEvent.Content}");
            System.Console.ResetColor();
        };

        client.StatusChanged += (sender, status) =>
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[TRẠNG THÁI WS] {status.Status} - {status.Reason}");
            System.Console.ResetColor();
        };

        _ = Task.Run(() => client.StartListenerAsync(session, cts.Token, (level, msg) =>
        {
            // Debug logs
        }), cts.Token);

        await InteractiveMenuAsync(session, cts.Token);
    }

    private static async Task<ZaloSessionMaterial?> PerformQrLoginLoopAsync(ZaloWebClient client)
    {
        int attempt = 1;
        while (true)
        {
            System.Console.WriteLine($"\n🔄 [Lần {attempt}] Đang tạo mã QR Login Zalo mới...");
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
            try
            {
                if (File.Exists(QrCodeFilePath))
                {
                    try { File.Delete(QrCodeFilePath); } catch { }
                }

                ZaloQrSession qrSession = await client.StartQrLoginAsync(cts.Token);
                byte[] imageBytes = Convert.FromBase64String(qrSession.QrImageBase64);
                await File.WriteAllBytesAsync(QrCodeFilePath, imageBytes, cts.Token);

                string fullQrPath = Path.GetFullPath(QrCodeFilePath);
                System.Console.WriteLine("-------------------------------------------------");
                System.Console.WriteLine($"📸 Mã QR đã lưu tại: {fullQrPath}");
                System.Console.WriteLine("⚠️ CHÚ Ý: Đóng cửa sổ mở ảnh cũ (nếu có) và mở lại file 'qrcode.png' để quét mã MỚI NÀY!");
                System.Console.WriteLine("👉 Mở Zalo trên điện thoại -> Quét mã QR -> Nhấn XÁC NHẬN.");
                System.Console.WriteLine("-------------------------------------------------");

                ZaloLoginState lastState = await client.PollLoginAsync(qrSession.SessionId);
                bool qrExpired = false;

                while (!cts.Token.IsCancellationRequested)
                {
                    ZaloLoginState state = await client.PollLoginAsync(qrSession.SessionId);
                    if (state.Status != lastState.Status)
                    {
                        lastState = state;
                        switch (state.Status)
                        {
                            case ZaloLoginStatus.Scanned:
                                string nameInfo = string.IsNullOrWhiteSpace(state.DisplayName) ? "" : $" [{state.DisplayName}]";
                                System.Console.WriteLine($"📱 Đã quét QR thành công{nameInfo}! Vui lòng nhấn [XÁC NHẬN] trên màn hình điện thoại...");
                                break;
                            case ZaloLoginStatus.Connected:
                                System.Console.WriteLine($"✅ Đăng nhập thành công với tài khoản: {state.DisplayName}!");
                                return client.ConsumePendingMaterial(qrSession.SessionId);
                            case ZaloLoginStatus.Declined:
                                System.Console.WriteLine("🚫 Bạn đã từ chối đăng nhập trên điện thoại.");
                                return null;
                            case ZaloLoginStatus.Expired:
                                if (!string.IsNullOrEmpty(state.ErrorMessage))
                                {
                                    System.Console.WriteLine($"❌ Lỗi luồng đăng nhập: {state.ErrorMessage}");
                                    return null;
                                }
                                System.Console.WriteLine("⌛ Mã QR cũ đã hết hạn! Đang tạo lại mã QR mới...");
                                qrExpired = true;
                                break;
                            default:
                                break;
                        }
                    }

                    if (qrExpired)
                    {
                        break;
                    }

                    if (state.Status == ZaloLoginStatus.Connected)
                    {
                        return client.ConsumePendingMaterial(qrSession.SessionId);
                    }

                    await Task.Delay(1000, cts.Token);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Lỗi trong quá trình tạo/quét QR: {ex.Message}");
            }

            attempt++;
            await Task.Delay(1500);
        }
    }

    private static async Task InteractiveMenuAsync(ZaloSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("================ MENU LỆNH ================");
            System.Console.WriteLine("1. Gửi tin nhắn văn bản");
            System.Console.WriteLine("2. Danh sách bạn bè");
            System.Console.WriteLine("3. Tra cứu thông tin người dùng (theo SĐT)");
            System.Console.WriteLine("4. Đăng xuất (xóa session.json)");
            System.Console.WriteLine("Q. Thoát");
            System.Console.Write("Lựa chọn của bạn: ");

            string? choice = System.Console.ReadLine()?.Trim().ToLowerInvariant();
            if (choice == "q")
            {
                break;
            }

            switch (choice)
            {
                case "1":
                    System.Console.Write("Nhập ThreadId (User ID hoặc Group ID): ");
                    string? targetId = System.Console.ReadLine()?.Trim();
                    System.Console.Write("Loại hội thoại (1: User, 2: Group): ");
                    string? typeStr = System.Console.ReadLine()?.Trim();
                    ZaloThreadType threadType = typeStr == "2" ? ZaloThreadType.Group : ZaloThreadType.User;

                    System.Console.Write("Nhập nội dung tin nhắn: ");
                    string? text = System.Console.ReadLine()?.Trim();

                    if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(text))
                    {
                        try
                        {
                            ZaloSendResult res = await ZaloWebClient.SendTextAsync(session, targetId, threadType, text, ct);
                            System.Console.WriteLine($"✅ Đã gửi thành công! MsgId: {res.MsgId}");
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"❌ Lỗi gửi tin nhắn: {ex.Message}");
                        }
                    }
                    break;

                case "2":
                    try
                    {
                        System.Console.WriteLine("Đang tải danh sách bạn bè...");
                        var friends = await ZaloWebClient.GetAllFriendsAsync(session, count: 50, page: 1, ct: ct);
                        System.Console.WriteLine($"📋 Tìm thấy {friends.Count} người bạn:");
                        foreach (var f in friends)
                        {
                            System.Console.WriteLine($" - {f.DisplayName} (UID: {f.UserId}, SĐT: {f.PhoneNumber ?? "Ẩn"})");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"❌ Lỗi lấy danh sách bạn bè: {ex.Message}");
                    }
                    break;

                case "3":
                    System.Console.Write("Nhập SĐT cần tìm (vd: 0987654321): ");
                    string? phone = System.Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(phone))
                    {
                        try
                        {
                            ZaloUserProfile profile = await ZaloWebClient.FindUserByPhoneAsync(session, phone, ct);
                            System.Console.WriteLine($"👤 Tên: {profile.DisplayName} | UID: {profile.Uid} | Avatar: {profile.AvatarUrl}");
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"❌ Lỗi tra cứu: {ex.Message}");
                        }
                    }
                    break;

                case "4":
                    if (File.Exists(SessionFilePath))
                    {
                        File.Delete(SessionFilePath);
                    }
                    System.Console.WriteLine("👋 Đã xóa session.json. Hãy khởi động lại app để quét QR mới.");
                    return;

                default:
                    break;
            }
        }
    }

    private static async Task<ZaloSessionMaterial?> LoadSessionAsync()
    {
        if (!File.Exists(SessionFilePath))
        {
            return null;
        }
        try
        {
            string json = await File.ReadAllTextAsync(SessionFilePath);
            return JsonSerializer.Deserialize<ZaloSessionMaterial>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveSessionAsync(ZaloSessionMaterial material)
    {
        string json = JsonSerializer.Serialize(material, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SessionFilePath, json);
    }
}
