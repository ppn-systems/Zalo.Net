using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zalo.Net;
using Zalo.Net.Contracts;

namespace Zalo.Console;

internal static class Program
{
    private static readonly TargetRegistry s_registry = new();

    private static async Task Main()
    {
        System.Console.OutputEncoding = Encoding.UTF8;
        System.Console.InputEncoding = Encoding.UTF8;

        PrintHeader();

        using ZaloWebClient client = new();
        ZaloSessionMaterial? material = await LoadSessionAsync().ConfigureAwait(false);

        if (material == null)
        {
            System.Console.WriteLine("[THÔNG BÁO] Chưa tìm thấy phiên làm việc cũ. Bắt đầu luồng đăng nhập bằng mã QR...");
            material = await PerformQrLoginLoopAsync(client).ConfigureAwait(false);
            if (material == null)
            {
                System.Console.WriteLine("[LỖI] Đăng nhập bị hủy hoặc đã quá thời gian chờ.");
                return;
            }
            await SaveSessionAsync(material).ConfigureAwait(false);
            System.Console.WriteLine("[THÀNH CÔNG] Đã lưu thông tin phiên vào file session.json.");
        }
        else
        {
            System.Console.WriteLine("[THÔNG BÁO] Đã tải thành công phiên làm việc từ session.json.");
        }

        System.Console.WriteLine("[THÔNG BÁO] Đang thiết lập kết nối tới hệ thống Zalo...");
        using CancellationTokenSource cts = new();

        ZaloSession session;
        try
        {
            session = await ZaloWebClient.LoginWithSessionAsync(material, cts.Token).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đăng nhập thành công! Mã định danh UID tài khoản: {session.Uid}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể kết nối phiên: {ex.Message}");
            System.Console.WriteLine("[HƯỚNG DẪN] Vui lòng xóa file session.json và khởi động lại để quét mã QR mới.");
            if (File.Exists(ConsoleConstants.SessionFilePath))
            {
                File.Delete(ConsoleConstants.SessionFilePath);
            }
            return;
        }

        // Đăng ký sự kiện lắng nghe tin nhắn thời gian thực qua WebSocket
        client.MessageReceived += (sender, msgEvent) =>
        {
            if (!msgEvent.IsSelf && !string.IsNullOrEmpty(msgEvent.ThreadId))
            {
                s_registry.AddOrUpdate(msgEvent.DisplayName, msgEvent.ThreadId, msgEvent.ThreadType);
            }

            System.Console.ForegroundColor = msgEvent.IsSelf ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
            string scopeLabel = msgEvent.ThreadType == ZaloThreadType.Group ? "[NHÓM]" : "[CÁ NHÂN]";
            string senderName = msgEvent.IsSelf
                ? "Bạn (Chính mình)"
                : (!string.IsNullOrEmpty(msgEvent.DisplayName) ? $"{msgEvent.DisplayName} ({msgEvent.UidFrom})" : msgEvent.UidFrom);

            System.Console.WriteLine($"\n[TIN NHẮN MỚI {scopeLabel}] Từ: {senderName} | ID Hội thoại: {msgEvent.ThreadId}");
            System.Console.WriteLine($"  Nội dung: {msgEvent.Content}");
            System.Console.ResetColor();
        };

        client.StatusChanged += (sender, status) =>
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[TRẠNG THÁI WS] {status.Status} - {status.Reason}");
            System.Console.ResetColor();
        };

        _ = Task.Run(() => client.StartListenerAsync(session, cts.Token, (level, msg) => { }), cts.Token);

        await InteractiveMenuAsync(session, cts.Token).ConfigureAwait(false);
    }

    private static void PrintHeader()
    {
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║            ZALO.NET - BỘ CÔNG CỤ THỬ NGHIỆM CLI VÀ API           ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    private static async Task<ZaloSessionMaterial?> PerformQrLoginLoopAsync(ZaloWebClient client)
    {
        int attempt = 1;
        while (true)
        {
            System.Console.WriteLine($"\n[THÔNG BÁO] (Lần {attempt}) Đang tạo mã QR Code đăng nhập...");
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
            try
            {
                if (File.Exists(ConsoleConstants.QrCodeFilePath))
                {
                    try { File.Delete(ConsoleConstants.QrCodeFilePath); } catch { }
                }

                ZaloQrSession qrSession = await client.StartQrLoginAsync(cts.Token).ConfigureAwait(false);
                byte[] imageBytes = Convert.FromBase64String(qrSession.QrImageBase64);
                await File.WriteAllBytesAsync(ConsoleConstants.QrCodeFilePath, imageBytes, cts.Token).ConfigureAwait(false);

                string fullQrPath = Path.GetFullPath(ConsoleConstants.QrCodeFilePath);
                System.Console.WriteLine("┌──────────────────────────────────────────────────────────────────┐");
                System.Console.WriteLine($"│ Mã QR Code đã lưu tại: {fullQrPath}");
                System.Console.WriteLine("│ Yêu cầu: Đóng cửa sổ ảnh cũ (nếu có) và dùng Zalo quét mã này.  │");
                System.Console.WriteLine("└──────────────────────────────────────────────────────────────────┘");

                ZaloLoginState lastState = await client.PollLoginAsync(qrSession.SessionId).ConfigureAwait(false);
                bool qrExpired = false;

                while (!cts.Token.IsCancellationRequested)
                {
                    ZaloLoginState state = await client.PollLoginAsync(qrSession.SessionId).ConfigureAwait(false);
                    if (state.Status != lastState.Status)
                    {
                        lastState = state;
                        switch (state.Status)
                        {
                            case ZaloLoginStatus.Scanned:
                                System.Console.WriteLine("[TRẠNG THÁI] Đã quét mã QR. Vui lòng bấm [XÁC NHẬN] trên màn hình điện thoại.");
                                break;
                            case ZaloLoginStatus.Connected:
                                System.Console.WriteLine("[THÀNH CÔNG] Đăng nhập thành công qua mã QR.");
                                return client.ConsumePendingMaterial(qrSession.SessionId);
                            case ZaloLoginStatus.Declined:
                                System.Console.WriteLine("[CẢNH BÁO] Đã từ chối đăng nhập trên điện thoại.");
                                return null;
                            case ZaloLoginStatus.Expired:
                                if (!string.IsNullOrEmpty(state.ErrorMessage))
                                {
                                    System.Console.WriteLine($"[LỖI] Luồng đăng nhập gián đoạn: {state.ErrorMessage}");
                                    return null;
                                }
                                System.Console.WriteLine("[CẢNH BÁO] Mã QR đã hết hạn. Đang sinh mã mới...");
                                qrExpired = true;
                                break;
                        }
                    }

                    if (qrExpired)
                    {
                        break;
                    }

                    await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[LỖI] Xảy ra lỗi trong luồng QR: {ex.Message}");
            }

            attempt++;
            await Task.Delay(1500, cts.Token).ConfigureAwait(false);
        }
    }

    private static async Task InteractiveMenuAsync(ZaloSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            System.Console.WriteLine();
            ConsoleTable menuTable = new("STT", "TÊN CHỨC NĂNG");
            menuTable.AddRow("1", "Gửi tin nhắn văn bản (Cá nhân / Nhóm)");
            menuTable.AddRow("2", "Danh sách bạn bè");
            menuTable.AddRow("3", "Tra cứu thông tin người dùng qua SĐT");
            menuTable.AddRow("4", "Đăng xuất khỏi phiên hiện tại");
            menuTable.AddRow("Q", "Thoát khỏi chương trình");
            menuTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            System.Console.Write("👉 Lựa chọn của bạn: ");
            string? choice = System.Console.ReadLine()?.Trim().ToLowerInvariant();
            if (choice == "q")
            {
                break;
            }

            switch (choice)
            {
                case "1":
                    await HandleSendMessageAsync(session, ct).ConfigureAwait(false);
                    break;

                case "2":
                    await HandleGetFriendsAsync(session, ct).ConfigureAwait(false);
                    break;

                case "3":
                    await HandleFindUserAsync(session, ct).ConfigureAwait(false);
                    break;

                case "4":
                    if (File.Exists(ConsoleConstants.SessionFilePath))
                    {
                        File.Delete(ConsoleConstants.SessionFilePath);
                    }
                    System.Console.WriteLine("[THÔNG BÁO] Đã xóa file session.json. Khởi động lại ứng dụng để quét mã mới.");
                    return;

                default:
                    break;
            }
        }
    }

    private static async Task HandleSendMessageAsync(ZaloSession session, CancellationToken ct)
    {
        string targetId = "";
        ZaloThreadType threadType = ZaloThreadType.User;

        IReadOnlyList<QuickTarget> targets = s_registry.GetAll();
        if (targets.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH HỘI THOẠI & BẠN BÈ GẦN ĐÂY]");
            ConsoleTable targetTable = new("STT", "Tên đối tượng", "Loại", "Thread ID / UID");
            foreach (QuickTarget target in targets)
            {
                string typeLabel = target.ThreadType == ZaloThreadType.Group ? "Nhóm" : "Cá nhân";
                targetTable.AddRow(target.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), target.Name, typeLabel, target.TargetId);
            }
            targetTable.AddRow("0", "Nhập Thread ID hoặc SĐT thủ công", "-", "-");
            targetTable.Print(ConsoleColor.DarkGray, ConsoleColor.Cyan);

            System.Console.Write("👉 Chọn số thứ tự (hoặc dán ID/SĐT): ");
        }
        else
        {
            System.Console.Write("Nhập Thread ID (User ID hoặc Group ID): ");
        }

        string? inputChoice = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(inputChoice))
        {
            return;
        }

        if (int.TryParse(inputChoice, out int targetIndex) && targetIndex > 0)
        {
            QuickTarget? found = s_registry.FindByIndex(targetIndex);
            if (found != null)
            {
                targetId = found.TargetId;
                threadType = found.ThreadType;
                System.Console.WriteLine($"[THÔNG BÁO] Đã chọn đối tượng: {found.Name} ({threadType})");
            }
        }

        if (string.IsNullOrEmpty(targetId))
        {
            if (inputChoice != "0")
            {
                targetId = inputChoice;
            }
            else
            {
                System.Console.Write("Nhập Thread ID / SĐT: ");
                targetId = System.Console.ReadLine()?.Trim() ?? "";
            }

            if (string.IsNullOrEmpty(targetId))
            {
                return;
            }

            System.Console.Write("Loại hội thoại (1: Cá nhân, 2: Nhóm) [Mặc định 1]: ");
            string? tTypeStr = System.Console.ReadLine()?.Trim();
            threadType = tTypeStr == "2" ? ZaloThreadType.Group : ZaloThreadType.User;
        }

        System.Console.Write("Nhập nội dung tin nhắn: ");
        string? text = System.Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(text))
        {
            try
            {
                ZaloSendResult res = await ZaloWebClient.SendTextAsync(session, targetId, threadType, text, ct).ConfigureAwait(false);
                System.Console.WriteLine($"[THÀNH CÔNG] Gửi tin nhắn thành công! Mã MsgId: {res.MsgId}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[LỖI] Không thể gửi tin nhắn: {ex.Message}");
            }
        }
    }

    private static async Task HandleGetFriendsAsync(ZaloSession session, CancellationToken ct)
    {
        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang lấy danh sách bạn bè...");
            IReadOnlyList<ZaloFriendInfo> friends = await ZaloWebClient.GetAllFriendsAsync(session, count: 50, page: 1, ct: ct).ConfigureAwait(false);

            foreach (ZaloFriendInfo f in friends)
            {
                s_registry.AddOrUpdate(f.DisplayName, f.UserId, ZaloThreadType.User);
            }

            IReadOnlyList<QuickTarget> list = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
            System.Console.WriteLine($"[THÀNH CÔNG] Đã tải {list.Count} người bạn:");

            ConsoleTable friendsTable = new("STT", "Tên hiển thị", "User ID (UID)", "Số điện thoại");
            foreach (QuickTarget qt in list)
            {
                friendsTable.AddRow(qt.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), qt.Name, qt.TargetId, "Ẩn");
            }
            friendsTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể lấy danh sách bạn bè: {ex.Message}");
        }
    }

    private static async Task HandleFindUserAsync(ZaloSession session, CancellationToken ct)
    {
        System.Console.Write("Nhập SĐT cần tìm (ví dụ: 0987654321): ");
        string? phone = System.Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(phone))
        {
            try
            {
                ZaloUserProfile profile = await ZaloWebClient.FindUserByPhoneAsync(session, phone, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(profile.Uid))
                {
                    s_registry.AddOrUpdate(profile.DisplayName, profile.Uid, ZaloThreadType.User);
                }

                ConsoleTable profileTable = new("Thuộc tính", "Giá trị");
                profileTable.AddRow("Tên hiển thị", string.IsNullOrEmpty(profile.DisplayName) ? "(Trống)" : profile.DisplayName);
                profileTable.AddRow("User ID (UID)", string.IsNullOrEmpty(profile.Uid) ? "(Trống)" : profile.Uid);
                profileTable.AddRow("Đường dẫn Avatar", string.IsNullOrEmpty(profile.AvatarUrl) ? "(Không có)" : profile.AvatarUrl);
                profileTable.Print(ConsoleColor.DarkGreen, ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[LỖI] Tra cứu thông tin không thành công: {ex.Message}");
            }
        }
    }

    private static async Task<ZaloSessionMaterial?> LoadSessionAsync()
    {
        if (!File.Exists(ConsoleConstants.SessionFilePath))
        {
            return null;
        }
        try
        {
            string json = await File.ReadAllTextAsync(ConsoleConstants.SessionFilePath).ConfigureAwait(false);
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
        await File.WriteAllTextAsync(ConsoleConstants.SessionFilePath, json).ConfigureAwait(false);
    }
}
