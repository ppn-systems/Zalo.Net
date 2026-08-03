using System.Text;
using System.Text.Json;
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

        System.Console.WriteLine("=================================================");
        System.Console.WriteLine("        Zalo.Net - Live Client Test Suite        ");
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine();

        using ZaloWebClient client = new();
        ZaloSessionMaterial? material = await LoadSessionAsync().ConfigureAwait(false);

        if (material == null)
        {
            System.Console.WriteLine("[INFO] Chua tim thấy session cu. Bat dau luong dang nhap QR Code...");
            material = await PerformQrLoginLoopAsync(client).ConfigureAwait(false);
            if (material == null)
            {
                System.Console.WriteLine("[ERROR] Dang nhap bi huy hoac thoi gian thuc hien da het.");
                return;
            }
            await SaveSessionAsync(material).ConfigureAwait(false);
            System.Console.WriteLine("[SUCCESS] Da luu session vao session.json cho cac lan truy cap sau.");
        }
        else
        {
            System.Console.WriteLine("[INFO] Da tai session hop le tu session.json.");
        }

        System.Console.WriteLine("[INFO] Dang thiet lap ket noi toi Zalo Session...");
        using CancellationTokenSource cts = new();

        ZaloSession session;
        try
        {
            session = await ZaloWebClient.LoginWithSessionAsync(material, cts.Token).ConfigureAwait(false);
            System.Console.WriteLine($"[SUCCESS] Dang nhap thanh cong. Account UID: {session.Uid}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[ERROR] Khong the khoi tao session: {ex.Message}");
            System.Console.WriteLine("[INFO] Vui long xoa file session.json va thuc hien dang nhap lai.");
            if (File.Exists(ConsoleConstants.SessionFilePath))
            {
                File.Delete(ConsoleConstants.SessionFilePath);
            }
            return;
        }

        // Tap trung quan ly su kien WebSocket realtime
        client.MessageReceived += (sender, msgEvent) =>
        {
            if (!msgEvent.IsSelf && !string.IsNullOrEmpty(msgEvent.ThreadId))
            {
                s_registry.AddOrUpdate(msgEvent.DisplayName, msgEvent.ThreadId, msgEvent.ThreadType);
            }

            System.Console.ForegroundColor = msgEvent.IsSelf ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
            string scopeLabel = msgEvent.ThreadType == ZaloThreadType.Group ? "[NHOM]" : "[CA NHAN]";
            string senderName = msgEvent.IsSelf
                ? "Ban (Chinh minh)"
                : (!string.IsNullOrEmpty(msgEvent.DisplayName) ? $"{msgEvent.DisplayName} ({msgEvent.UidFrom})" : msgEvent.UidFrom);

            System.Console.WriteLine($"\n[EVENT{scopeLabel}] Tu: {senderName} | ThreadId: {msgEvent.ThreadId}");
            System.Console.WriteLine($"  Noi dung: {msgEvent.Content}");
            System.Console.ResetColor();
        };

        client.StatusChanged += (sender, status) =>
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[STATUS] Dynamic WebSocket: {status.Status} - {status.Reason}");
            System.Console.ResetColor();
        };

        _ = Task.Run(() => client.StartListenerAsync(session, cts.Token, (level, msg) => { }), cts.Token);

        await InteractiveMenuAsync(session, cts.Token).ConfigureAwait(false);
    }

    private static async Task<ZaloSessionMaterial?> PerformQrLoginLoopAsync(ZaloWebClient client)
    {
        int attempt = 1;
        while (true)
        {
            System.Console.WriteLine($"\n[INFO] (Lan {attempt}) Dang khoi tao phien QR Code login...");
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
                System.Console.WriteLine("-------------------------------------------------");
                System.Console.WriteLine($"[INFO] Ma QR Code da duoc ghi tai: {fullQrPath}");
                System.Console.WriteLine("[ACTION] Vui long mo file 'qrcode.png' va dung ung dung Zalo de quet.");
                System.Console.WriteLine("-------------------------------------------------");

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
                                System.Console.WriteLine("[STATUS] Da quet ma QR. Vui long bam [XAC NHAN] tren dien thoại.");
                                break;
                            case ZaloLoginStatus.Connected:
                                System.Console.WriteLine("[SUCCESS] Dang nhap thanh cong qua QR Code.");
                                return client.ConsumePendingMaterial(qrSession.SessionId);
                            case ZaloLoginStatus.Declined:
                                System.Console.WriteLine("[WARN] Tu choi dang nhap tren thiet bi dong ho.");
                                return null;
                            case ZaloLoginStatus.Expired:
                                if (!string.IsNullOrEmpty(state.ErrorMessage))
                                {
                                    System.Console.WriteLine($"[ERROR] Luong dang nhap giat doan: {state.ErrorMessage}");
                                    return null;
                                }
                                System.Console.WriteLine("[WARN] Phien QR Code da het han. Dang tao phien moi...");
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
                System.Console.WriteLine($"[ERROR] Xay ra loi luong QR: {ex.Message}");
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
            System.Console.WriteLine("================ MENU CHUCNANG ================");
            System.Console.WriteLine("1. Gui tin nhan van ban");
            System.Console.WriteLine("2. Danh sach ban be");
            System.Console.WriteLine("3. Tra cuu nguoi dung qua SDT");
            System.Console.WriteLine("4. Dang xuat khoi phien");
            System.Console.WriteLine("Q. Thoat khoi ung dung");
            System.Console.Write("Nhap lua chon: ");

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
                    System.Console.WriteLine("[INFO] Da xoa session.json. Khoi dong lai app de tao phien moi.");
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
            System.Console.WriteLine("\n--- DANH SACH HOI THOAI / BAN BE GẦN ĐAY ---");
            foreach (QuickTarget target in targets)
            {
                string scopeStr = target.ThreadType == ZaloThreadType.Group ? "[NHOM]" : "[CA NHAN]";
                System.Console.WriteLine($"  [{target.Index}] {target.Name} {scopeStr} (ID: {target.TargetId})");
            }
            System.Console.WriteLine("  [0] Nhap ThreadId / SDT thu cong");
            System.Console.Write("Lựa chọn chỉ số (hoặc nhap ID/SDT): ");
        }
        else
        {
            System.Console.Write("Nhap ThreadId (User ID hoac Group ID): ");
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
                System.Console.WriteLine($"[INFO] Da chon target: {found.Name} ({threadType})");
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
                System.Console.Write("Nhap ThreadId / SDT: ");
                targetId = System.Console.ReadLine()?.Trim() ?? "";
            }

            if (string.IsNullOrEmpty(targetId))
            {
                return;
            }

            System.Console.Write("Loai hoi thoai (1: User, 2: Group) [Mac dinh 1]: ");
            string? tTypeStr = System.Console.ReadLine()?.Trim();
            threadType = tTypeStr == "2" ? ZaloThreadType.Group : ZaloThreadType.User;
        }

        System.Console.Write("Nhap noi dung tin nhan: ");
        string? text = System.Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(text))
        {
            try
            {
                ZaloSendResult res = await ZaloWebClient.SendTextAsync(session, targetId, threadType, text, ct).ConfigureAwait(false);
                System.Console.WriteLine($"[SUCCESS] Gui tin nhan thành cong. MsgId: {res.MsgId}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ERROR] Khong the gui tin nhan: {ex.Message}");
            }
        }
    }

    private static async Task HandleGetFriendsAsync(ZaloSession session, CancellationToken ct)
    {
        try
        {
            System.Console.WriteLine("[INFO] Dang truyen nhan danh sach ban be...");
            IReadOnlyList<ZaloFriendInfo> friends = await ZaloWebClient.GetAllFriendsAsync(session, count: 50, page: 1, ct: ct).ConfigureAwait(false);
            System.Console.WriteLine($"[SUCCESS] Da tai {friends.Count} nguoi ban:");

            foreach (ZaloFriendInfo f in friends)
            {
                s_registry.AddOrUpdate(f.DisplayName, f.UserId, ZaloThreadType.User);
            }

            IReadOnlyList<QuickTarget> list = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
            foreach (QuickTarget qt in list)
            {
                System.Console.WriteLine($"  [{qt.Index}] {qt.Name} (UID: {qt.TargetId})");
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[ERROR] Khong the tai danh sach ban be: {ex.Message}");
        }
    }

    private static async Task HandleFindUserAsync(ZaloSession session, CancellationToken ct)
    {
        System.Console.Write("Nhap SDT can tim (vi du: 0987654321): ");
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
                System.Console.WriteLine($"[RESULT] Ten: {profile.DisplayName} | UID: {profile.Uid} | Avatar: {profile.AvatarUrl}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ERROR] Tra cuu khong thanh cong: {ex.Message}");
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
