using System.Text;
using System.Text.Json;
using Zalo.Net;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;

namespace Zalo.Console;

internal record RecentMessageInfo(
    string ThreadId,
    ZaloThreadType ThreadType,
    string MsgId,
    string CliMsgId,
    string SenderUid,
    string SenderName,
    string Content,
    DateTime Timestamp,
    long TimestampMs = 0
);

internal static class Program
{
    private static readonly TargetRegistry s_registry = new();
    private static readonly List<RecentMessageInfo> s_recentMessages = [];
    private static readonly System.Threading.Lock s_messageLock = new();

    private static void AddRecentMessage(RecentMessageInfo msg)
    {
        lock (s_messageLock)
        {
            s_recentMessages.Insert(0, msg);
            if (s_recentMessages.Count > 20)
            {
                s_recentMessages.RemoveAt(s_recentMessages.Count - 1);
            }
        }
    }

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

            string senderName = msgEvent.IsSelf
                ? "Bạn (Chính mình)"
                : (!string.IsNullOrEmpty(msgEvent.DisplayName) ? msgEvent.DisplayName : msgEvent.UidFrom);

            string msgContent = msgEvent.Content?.ToString() ?? "";
            if (!string.IsNullOrEmpty(msgEvent.MsgId))
            {
                _ = long.TryParse(msgEvent.TimestampMs, out long tsMs);
                AddRecentMessage(new RecentMessageInfo(
                    msgEvent.ThreadId,
                    msgEvent.ThreadType,
                    msgEvent.MsgId,
                    msgEvent.CliMsgId,
                    msgEvent.UidFrom,
                    senderName,
                    msgContent,
                    DateTime.Now,
                    tsMs
                ));
            }

            System.Console.ForegroundColor = msgEvent.IsSelf ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
            string scopeLabel = msgEvent.ThreadType == ZaloThreadType.Group ? "[NHÓM]" : "[CÁ NHÂN]";
            System.Console.WriteLine($"\n[TIN NHẮN MỚI {scopeLabel}] Từ: {senderName} ({msgEvent.UidFrom}) | MsgID: {msgEvent.MsgId}");
            System.Console.WriteLine($"  Nội dung: {msgContent}");
            System.Console.ResetColor();

            if (!string.IsNullOrEmpty(msgEvent.RawJson))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkYellow;
                System.Console.WriteLine($"[RAW WS MSG JSON]\n{msgEvent.RawJson}\n");
                System.Console.ResetColor();
            }
        };

        client.StatusChanged += (sender, status) =>
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"[TRẠNG THÁI WS] {status.Status} - {status.Reason}");
            System.Console.ResetColor();
        };

        _ = Task.Run(() => client.StartListenerAsync(session, cts.Token, (level, msg) =>
        {
            if (level >= ZaloLogLevel.Information || msg.Contains("601", StringComparison.Ordinal) || msg.Contains("FILE", StringComparison.OrdinalIgnoreCase))
            {
                System.Console.WriteLine($"[WS LOG] {level}: {msg}");
            }
        }), cts.Token);

        await InteractiveMenuAsync(session, cts.Token).ConfigureAwait(false);
    }

    private static void PrintHeader()
    {
        ConsoleTable headerTable = new("ZALO.NET - CLIENT SAMPLE SUITE");
        headerTable.AddRow("Bộ thư viện C# .NET 10 chính thức cho Zalo Client API & Realtime WebSocket");
        headerTable.Print(ConsoleColor.Cyan, ConsoleColor.Yellow);
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
                ConsoleTable qrTable = new("THÔNG TIN MÃ QR CODE");
                qrTable.AddRow($"Đường dẫn file: {fullQrPath}");
                qrTable.AddRow("Yêu cầu: Đóng cửa sổ ảnh cũ (nếu có) và mở file 'qrcode.png' để quét.");
                qrTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

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
            ConsoleTable menuTable = new("STT", "CHỨC NĂNG HỆ THỐNG ZALO CRM");
            menuTable.AddRow("--", "[TIN NHẮN & TRUYỀN THÔNG]");
            menuTable.AddRow("1", "  Gửi tin nhắn văn bản (Cá nhân / Nhóm)");
            menuTable.AddRow("2", "  Gửi hình ảnh đính kèm");
            menuTable.AddRow("3", "  Gửi File / Hóa đơn / Hợp đồng đính kèm");
            menuTable.AddRow("4", "  Giám sát tin nhắn Realtime (WebSocket CRM)");
            menuTable.AddRow("--", "[TÍNH NĂNG TƯƠNG TÁC TIN NHẮN (INTERACTION)]");
            menuTable.AddRow("14", " Thu hồi tin nhắn (Undo / Recall)");
            menuTable.AddRow("15", " Trả lời / Trích dẫn tin nhắn (Quote / Reply)");
            menuTable.AddRow("16", " Thả cảm xúc / Reaction (Heart/Like/Haha...)");
            menuTable.AddRow("17", " Gửi Sticker / Nhãn dán");
            menuTable.AddRow("--", "[QUẢN LÝ BẠN BÈ & KHÁCH HÀNG (CRM)]");
            menuTable.AddRow("5", "  Danh sách bạn bè");
            menuTable.AddRow("6", "  Tra cứu thông tin người dùng qua SĐT");
            menuTable.AddRow("7", "  Gửi lời mời kết bạn (qua SĐT / UID)");
            menuTable.AddRow("8", "  Đặt biệt danh / Gán Alias Khách hàng");
            menuTable.AddRow("9", "  Chặn / Bỏ chặn Spammer");
            menuTable.AddRow("--", "[QUẢN LÝ NHÓM CHAT (GROUP CRM)]");
            menuTable.AddRow("10", " Tạo nhóm chat mới");
            menuTable.AddRow("11", " Thêm thành viên vào nhóm");
            menuTable.AddRow("12", " Xóa thành viên khỏi nhóm");
            menuTable.AddRow("--", "[HỆ THỐNG]");
            menuTable.AddRow("13", " Đăng xuất khỏi phiên hiện tại");
            menuTable.AddRow("Q", "  Thoát khỏi chương trình");
            menuTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            System.Console.Write("[NHẬP] Lựa chọn của bạn: ");
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
                    await HandleSendImageAsync(session, ct).ConfigureAwait(false);
                    break;

                case "3":
                    await HandleSendFileAsync(session, ct).ConfigureAwait(false);
                    break;

                case "4":
                    await HandleLiveStreamAsync(ct).ConfigureAwait(false);
                    break;

                case "5":
                    await HandleGetFriendsAsync(session, ct).ConfigureAwait(false);
                    break;

                case "6":
                    await HandleFindUserAsync(session, ct).ConfigureAwait(false);
                    break;

                case "7":
                    await HandleSendFriendRequestAsync(session, ct).ConfigureAwait(false);
                    break;

                case "8":
                    await HandleChangeAliasAsync(session, ct).ConfigureAwait(false);
                    break;

                case "9":
                    await HandleBlockUserAsync(session, ct).ConfigureAwait(false);
                    break;

                case "10":
                    await HandleCreateGroupAsync(session, ct).ConfigureAwait(false);
                    break;

                case "11":
                    await HandleAddUserToGroupAsync(session, ct).ConfigureAwait(false);
                    break;

                case "12":
                    await HandleRemoveUserFromGroupAsync(session, ct).ConfigureAwait(false);
                    break;

                case "13":
                    if (File.Exists(ConsoleConstants.SessionFilePath))
                    {
                        File.Delete(ConsoleConstants.SessionFilePath);
                    }
                    System.Console.WriteLine("[THÔNG BÁO] Đã xóa file session.json. Khởi động lại ứng dụng để quét mã mới.");
                    return;

                case "14":
                    await HandleUndoMessageAsync(session, ct).ConfigureAwait(false);
                    break;

                case "15":
                    await HandleSendQuoteMessageAsync(session, ct).ConfigureAwait(false);
                    break;

                case "16":
                    await HandleAddReactionAsync(session, ct).ConfigureAwait(false);
                    break;

                case "17":
                    await HandleSendStickerAsync(session, ct).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
    }

    private static (string TargetId, ZaloThreadType ThreadType) ResolveTargetSelection()
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

            System.Console.Write("[NHẬP] Chọn số thứ tự (hoặc dán ID/SĐT): ");
        }
        else
        {
            System.Console.Write("[NHẬP] Nhập Thread ID (User ID hoặc Group ID): ");
        }

        string? inputChoice = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(inputChoice))
        {
            return ("", ZaloThreadType.User);
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
                System.Console.Write("[NHẬP] Nhập Thread ID / SĐT: ");
                targetId = System.Console.ReadLine()?.Trim() ?? "";
            }

            if (string.IsNullOrEmpty(targetId))
            {
                return ("", ZaloThreadType.User);
            }

            System.Console.Write("[NHẬP] Loại hội thoại (1: Cá nhân, 2: Nhóm) [Mặc định 1]: ");
            string? tTypeStr = System.Console.ReadLine()?.Trim();
            threadType = tTypeStr == "2" ? ZaloThreadType.Group : ZaloThreadType.User;
        }

        return (targetId, threadType);
    }

    private static RecentMessageInfo? ResolveMessageSelection(string promptMessage)
    {
        List<RecentMessageInfo> list;
        lock (s_messageLock)
        {
            list = [.. s_recentMessages];
        }

        if (list.Count == 0)
        {
            return null;
        }

        System.Console.WriteLine($"\n[DANH SÁCH TIN NHẮN GẦN ĐÂY] ({promptMessage})");
        ConsoleTable msgTable = new("STT", "Người gửi", "MsgID", "Nội dung", "Thời gian");
        for (int i = 0; i < list.Count; i++)
        {
            RecentMessageInfo m = list[i];
            string contentSnippet = m.Content.Length > 30 ? $"{m.Content[..30]}..." : m.Content;
            msgTable.AddRow(
                (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                m.SenderName,
                m.MsgId,
                string.IsNullOrEmpty(contentSnippet) ? "(Tin nhắn hệ thống / đính kèm)" : contentSnippet,
                m.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        msgTable.AddRow("0", "Nhập ID thủ công", "-", "-", "-");
        msgTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

        System.Console.Write($"[NHẬP] Chọn số thứ tự tin nhắn (Mặc định [1]): ");
        string? choiceStr = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(choiceStr) || choiceStr == "1")
        {
            RecentMessageInfo first = list[0];
            System.Console.WriteLine($"[THÔNG BÁO] Đã chọn tin nhắn gần nhất: MsgID {first.MsgId} từ {first.SenderName}");
            return first;
        }

        if (int.TryParse(choiceStr, out int idx) && idx >= 1 && idx <= list.Count)
        {
            RecentMessageInfo selected = list[idx - 1];
            System.Console.WriteLine($"[THÔNG BÁO] Đã chọn tin nhắn STT {idx}: MsgID {selected.MsgId} từ {selected.SenderName}");
            return selected;
        }

        return null;
    }

    private static async Task HandleSendMessageAsync(ZaloSession session, CancellationToken ct)
    {
        (string targetId, ZaloThreadType threadType) = ResolveTargetSelection();
        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }

        System.Console.Write("[NHẬP] Nhập nội dung tin nhắn: ");
        string? text = System.Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                ZaloSendResult res = await ZaloWebClient.SendTextAsync(session, targetId, threadType, text, ct).ConfigureAwait(false);
                System.Console.WriteLine($"[THÀNH CÔNG] Gửi tin nhắn thành công! Mã MsgId: {res.MsgId}");
                AddRecentMessage(new RecentMessageInfo(
                    targetId,
                    threadType,
                    res.MsgId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    session.Uid,
                    "Bạn (Chính mình)",
                    text,
                    DateTime.Now
                ));
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[LỖI] Không thể gửi tin nhắn: {ex.Message}");
            }
        }
    }

    private static async Task HandleSendImageAsync(ZaloSession session, CancellationToken ct)
    {
        (string targetId, ZaloThreadType threadType) = ResolveTargetSelection();
        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }

        System.Console.Write("[NHẬP] Đường dẫn file ảnh (mặc định 'qrcode.png'): ");
        string? imagePath = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(imagePath))
        {
            imagePath = ConsoleConstants.QrCodeFilePath;
        }

        if (!File.Exists(imagePath))
        {
            System.Console.WriteLine($"[LỖI] Khôn tìm thấy file ảnh tại đường dẫn: {imagePath}");
            return;
        }

        System.Console.Write("[NHẬP] Nội dung chú thích (Caption): ");
        string? caption = System.Console.ReadLine()?.Trim();

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang tải ảnh lên và gửi tin nhắn...");
            byte[] bytes = await File.ReadAllBytesAsync(imagePath, ct).ConfigureAwait(false);
            ZaloSendResult res = await ZaloWebClient.SendAttachmentAsync(session, targetId, threadType, bytes, Path.GetFileName(imagePath), caption, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã gửi ảnh thành công! Mã MsgId: {res.MsgId}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể gửi ảnh: {ex.Message}");
        }
    }

    private static Task HandleLiveStreamAsync(CancellationToken ct)
    {
        _ = ct;
        System.Console.WriteLine("\n[HỆ THỐNG GIÁM SÁT REALTIME CRM]");
        System.Console.WriteLine("[THÔNG BÁO] Hệ thống đang nhận và lưu vết tin nhắn thời gian thực qua luồng mã hóa WebSocket...");
        System.Console.WriteLine("[HƯỚNG DẪN] Mọi tin nhắn Cá nhân / Nhóm đến/đi đều được hiển thị trực tiếp trên màn hình. Bấm phím [Enter] để quay lại menu chính.\n");
        _ = System.Console.ReadLine();
        return Task.CompletedTask;
    }

    private static async Task HandleCreateGroupAsync(ZaloSession session, CancellationToken ct)
    {
        System.Console.Write("[NHẬP] Tên nhóm mới muốn tạo: ");
        string? groupName = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(groupName))
        {
            return;
        }

        List<QuickTarget> friends = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
        List<string> memberIds = [];

        if (friends.Count > 0)
        {
            System.Console.WriteLine("\n[CHỌN THÀNH VIÊN NHÓM]");
            ConsoleTable memberTable = new("STT", "Tên bạn bè", "User ID (UID)");
            foreach (QuickTarget f in friends)
            {
                memberTable.AddRow(f.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Name, f.TargetId);
            }
            memberTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);
            System.Console.Write("[NHẬP] Nhập các số STT thành viên (phân cách bằng dấu phẩy, ví dụ: 1,2): ");
            string? selStr = System.Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(selStr))
            {
                string[] parts = selStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (string p in parts)
                {
                    if (int.TryParse(p, out int idx))
                    {
                        QuickTarget? found = friends.FirstOrDefault(f => f.Index == idx);
                        if (found != null)
                        {
                            memberIds.Add(found.TargetId);
                        }
                    }
                    else if (!string.IsNullOrEmpty(p))
                    {
                        memberIds.Add(p);
                    }
                }
            }
        }

        if (memberIds.Count == 0)
        {
            System.Console.Write("[NHẬP] Nhập UID thành viên (phân cách bằng dấu phẩy): ");
            string? uidsStr = System.Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(uidsStr))
            {
                memberIds.AddRange(uidsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (memberIds.Count == 0)
        {
            System.Console.WriteLine("[LỖI] Nhóm cần ít nhất 1 thành viên.");
            return;
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang tạo nhóm chat mới...");
            ZaloGroupCreateResult res = await ZaloWebClient.CreateGroupAsync(session, groupName, memberIds, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Tạo nhóm thành công! ID Nhóm mới: {res.GroupId}");
            if (!string.IsNullOrEmpty(res.GroupId))
            {
                s_registry.AddOrUpdate(groupName, res.GroupId, ZaloThreadType.Group);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể tạo nhóm: {ex.Message}");
        }
    }

    private static async Task HandleAddUserToGroupAsync(ZaloSession session, CancellationToken ct)
    {
        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang tự động tải danh sách các nhóm chat...");
            IReadOnlyList<ZaloGroupInfo> remoteGroups = await ZaloWebClient.GetAllGroupsAsync(session, ct).ConfigureAwait(false);
            foreach (ZaloGroupInfo g in remoteGroups)
            {
                s_registry.AddOrUpdate(g.Name, g.GroupId, ZaloThreadType.Group);
            }
        }
        catch { }

        List<QuickTarget> groups = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.Group)];
        string groupId = "";

        if (groups.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH NHÓM CỦA BẠN]");
            ConsoleTable groupTable = new("STT", "Tên nhóm", "Group ID");
            foreach (QuickTarget g in groups)
            {
                groupTable.AddRow(g.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Name, g.TargetId);
            }
            groupTable.AddRow("0", "Nhập Group ID thủ công", "-");
            groupTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            System.Console.Write("[NHẬP] Chọn số thứ tự nhóm cần thêm thành viên: ");
            string? input = System.Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int idx) && idx > 0)
            {
                QuickTarget? found = groups.FirstOrDefault(g => g.Index == idx);
                if (found != null)
                {
                    groupId = found.TargetId;
                }
            }
            if (string.IsNullOrEmpty(groupId) && input != "0")
            {
                groupId = input ?? "";
            }
        }

        if (string.IsNullOrEmpty(groupId))
        {
            System.Console.Write("[NHẬP] Nhập Group ID: ");
            groupId = System.Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(groupId))
        {
            return;
        }

        List<QuickTarget> friends = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
        if (friends.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH BẠN BÈ]");
            ConsoleTable friendTable = new("STT", "Tên bạn bè", "User ID (UID)");
            foreach (QuickTarget f in friends)
            {
                friendTable.AddRow(f.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Name, f.TargetId);
            }
            friendTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);
        }

        System.Console.Write("[NHẬP] Nhập STT hoặc UID người dùng cần thêm (phân cách bằng dấu phẩy nếu nhiều UID): ");
        string? userIdsInput = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(userIdsInput))
        {
            return;
        }

        List<string> selectedUids = [];
        string[] parts = userIdsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string p in parts)
        {
            if (int.TryParse(p, out int uIdx))
            {
                QuickTarget? f = friends.FirstOrDefault(x => x.Index == uIdx);
                if (f != null)
                {
                    selectedUids.Add(f.TargetId);
                    continue;
                }
            }
            selectedUids.Add(p);
        }

        if (selectedUids.Count == 0)
        {
            return;
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang gửi yêu cầu thêm thành viên vào nhóm...");
            await ZaloWebClient.AddUserToGroupAsync(session, groupId, selectedUids, ct).ConfigureAwait(false);
            System.Console.WriteLine("[THÀNH CÔNG] Đã thêm thành viên vào nhóm thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể thêm thành viên vào nhóm: {ex.Message}");
        }
    }

    private static async Task HandleRemoveUserFromGroupAsync(ZaloSession session, CancellationToken ct)
    {
        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang tự động tải danh sách các nhóm chat...");
            IReadOnlyList<ZaloGroupInfo> remoteGroups = await ZaloWebClient.GetAllGroupsAsync(session, ct).ConfigureAwait(false);
            foreach (ZaloGroupInfo g in remoteGroups)
            {
                s_registry.AddOrUpdate(g.Name, g.GroupId, ZaloThreadType.Group);
            }
        }
        catch { }

        List<QuickTarget> groups = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.Group)];
        string groupId = "";

        if (groups.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH NHÓM CỦA BẠN]");
            ConsoleTable groupTable = new("STT", "Tên nhóm", "Group ID");
            foreach (QuickTarget g in groups)
            {
                groupTable.AddRow(g.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Name, g.TargetId);
            }
            groupTable.AddRow("0", "Nhập Group ID thủ công", "-");
            groupTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            System.Console.Write("[NHẬP] Chọn số thứ tự nhóm cần xóa thành viên: ");
            string? input = System.Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int idx) && idx > 0)
            {
                QuickTarget? found = groups.FirstOrDefault(g => g.Index == idx);
                if (found != null)
                {
                    groupId = found.TargetId;
                }
            }
            if (string.IsNullOrEmpty(groupId) && input != "0")
            {
                groupId = input ?? "";
            }
        }

        if (string.IsNullOrEmpty(groupId))
        {
            System.Console.Write("[NHẬP] Nhập Group ID: ");
            groupId = System.Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(groupId))
        {
            return;
        }

        List<QuickTarget> friends = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
        if (friends.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH GỢI Ý]");
            ConsoleTable friendTable = new("STT", "Tên bạn bè", "User ID (UID)");
            foreach (QuickTarget f in friends)
            {
                friendTable.AddRow(f.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Name, f.TargetId);
            }
            friendTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);
        }

        System.Console.Write("[NHẬP] Nhập STT hoặc UID người dùng cần xóa khỏi nhóm (phân cách bằng dấu phẩy nếu nhiều UID): ");
        string? userIdsInput = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(userIdsInput))
        {
            return;
        }

        List<string> selectedUids = [];
        string[] parts = userIdsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string p in parts)
        {
            if (int.TryParse(p, out int uIdx))
            {
                QuickTarget? f = friends.FirstOrDefault(x => x.Index == uIdx);
                if (f != null)
                {
                    selectedUids.Add(f.TargetId);
                    continue;
                }
            }
            selectedUids.Add(p);
        }

        if (selectedUids.Count == 0)
        {
            return;
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang gửi yêu cầu xóa thành viên khỏi nhóm...");
            await ZaloWebClient.RemoveUserFromGroupAsync(session, groupId, selectedUids, ct).ConfigureAwait(false);
            System.Console.WriteLine("[THÀNH CÔNG] Đã xóa thành viên khỏi nhóm thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể xóa thành viên khỏi nhóm: {ex.Message}");
        }
    }

    private static async Task HandleSendFriendRequestAsync(ZaloSession session, CancellationToken ct)
    {
        System.Console.Write("[NHẬP] Nhập SĐT hoặc User ID (UID) người dùng muốn kết bạn: ");
        string? input = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        string userId = input;
        if (!input.All(char.IsDigit) || input.Length < 15)
        {
            try
            {
                System.Console.WriteLine("[THÔNG BÁO] Đang tìm kiếm thông tin người dùng qua SĐT...");
                ZaloUserProfile profile = await ZaloWebClient.FindUserByPhoneAsync(session, input, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(profile.Uid))
                {
                    userId = profile.Uid;
                    System.Console.WriteLine($"[THÔNG BÁO] Đã tìm thấy người dùng: {profile.DisplayName} (UID: {userId})");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[CẢNH BÁO] Không thể tìm UID qua SĐT: {ex.Message}");
            }
        }

        System.Console.Write("[NHẬP] Nhập lời nhắn kết bạn (mặc định: 'Xin chào, mình kết bạn nhé!'): ");
        string? msg = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(msg))
        {
            msg = "Xin chào, mình kết bạn nhé!";
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang gửi lời mời kết bạn...");
            await ZaloWebClient.SendFriendRequestAsync(session, userId, msg, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã gửi lời mời kết bạn thành công tới UID: {userId}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể gửi lời mời kết bạn: {ex.Message}");
        }
    }

    private static async Task HandleChangeAliasAsync(ZaloSession session, CancellationToken ct)
    {
        List<QuickTarget> friends = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.User)];
        string userId = "";
        if (friends.Count > 0)
        {
            System.Console.WriteLine("\n[DANH SÁCH BẠN BÈ / KHÁCH HÀNG]");
            ConsoleTable friendTable = new("STT", "Tên hiển thị", "User ID (UID)");
            foreach (QuickTarget f in friends)
            {
                friendTable.AddRow(f.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Name, f.TargetId);
            }
            friendTable.Print(ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            System.Console.Write("[NHẬP] Chọn STT hoặc nhập UID khách hàng cần đặt biệt danh: ");
            string? input = System.Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int idx) && idx > 0)
            {
                QuickTarget? found = friends.FirstOrDefault(f => f.Index == idx);
                if (found != null)
                {
                    userId = found.TargetId;
                }
            }
            if (string.IsNullOrEmpty(userId))
            {
                userId = input ?? "";
            }
        }
        else
        {
            System.Console.Write("[NHẬP] Nhập User ID (UID) khách hàng: ");
            userId = System.Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        System.Console.Write("[NHẬP] Nhập Biệt danh / Ghi chú mới (ví dụ: '[KH VIP] Anh Nam - VinHomes'): ");
        string? alias = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(alias))
        {
            return;
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang cập nhật biệt danh cho khách hàng...");
            await ZaloWebClient.ChangeFriendAliasAsync(session, userId, alias, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã cập nhật biệt danh '{alias}' thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể cập nhật biệt danh: {ex.Message}");
        }
    }

    private static async Task HandleBlockUserAsync(ZaloSession session, CancellationToken ct)
    {
        System.Console.Write("[NHẬP] Nhập User ID (UID) cần thao tác (Chặn/Bỏ chặn): ");
        string? userId = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        System.Console.Write("[NHẬP] Bạn muốn (1) Chặn hay (2) Bỏ chặn? Nhập 1 hoặc 2: ");
        string? action = System.Console.ReadLine()?.Trim();

        try
        {
            if (action == "1")
            {
                System.Console.WriteLine("[THÔNG BÁO] Đang chặn người dùng...");
                await ZaloWebClient.BlockUserAsync(session, userId, ct).ConfigureAwait(false);
                System.Console.WriteLine("[THÀNH CÔNG] Đã chặn người dùng thành công!");
            }
            else if (action == "2")
            {
                System.Console.WriteLine("[THÔNG BÁO] Đang bỏ chặn người dùng...");
                await ZaloWebClient.UnblockUserAsync(session, userId, ct).ConfigureAwait(false);
                System.Console.WriteLine("[THÀNH CÔNG] Đã bỏ chặn người dùng thành công!");
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Thao tác thất bại: {ex.Message}");
        }
    }

    private static async Task HandleSendFileAsync(ZaloSession session, CancellationToken ct)
    {
        (string targetId, ZaloThreadType threadType) = ResolveTargetSelection();
        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }

        System.Console.Write("[NHẬP] Đường dẫn file (PDF, Docx, Zip, Excel...): ");
        string? filePath = System.Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            System.Console.WriteLine("[LỖI] Đường dẫn file không hợp lệ hoặc file không tồn tại.");
            return;
        }

        System.Console.Write("[NHẬP] Lời nhắn / Ghi chú đính kèm (nếu có): ");
        string? caption = System.Console.ReadLine()?.Trim();

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang gửi file tài liệu...");
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            string fileName = Path.GetFileName(filePath);
            _ = await ZaloWebClient.SendAttachmentAsync(session, targetId, threadType, fileBytes, fileName, caption, ct).ConfigureAwait(false);
            System.Console.WriteLine("[THÀNH CÔNG] Đã gửi file đính kèm thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể gửi file: {ex.Message}");
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
        System.Console.Write("[NHẬP] Nhập SĐT cần tìm (ví dụ: 0987654321): ");
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

    private static async Task HandleUndoMessageAsync(ZaloSession session, CancellationToken ct)
    {
        RecentMessageInfo? targetMsg = ResolveMessageSelection("Chọn tin nhắn muốn THU HỒI");
        string targetId = targetMsg?.ThreadId ?? "";
        ZaloThreadType threadType = targetMsg?.ThreadType ?? ZaloThreadType.User;
        string msgId = targetMsg?.MsgId ?? "";
        string cliMsgId = targetMsg?.CliMsgId ?? "";

        if (targetMsg == null)
        {
            (targetId, threadType) = ResolveTargetSelection();
            if (string.IsNullOrEmpty(targetId)) return;

            System.Console.Write("[NHẬP] Nhập Message ID (msgId): ");
            msgId = System.Console.ReadLine()?.Trim() ?? "";
            System.Console.Write("[NHẬP] Nhập Client Message ID (cliMsgId): ");
            cliMsgId = System.Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(msgId) || string.IsNullOrEmpty(cliMsgId)) return;

        try
        {
            await ZaloWebClient.UndoMessageAsync(session, targetId, msgId, cliMsgId, threadType, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã thu hồi tin nhắn ({msgId}) thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Thu hồi tin nhắn thất bại: {ex.Message}");
        }
    }

    private static async Task HandleSendQuoteMessageAsync(ZaloSession session, CancellationToken ct)
    {
        RecentMessageInfo? targetMsg = ResolveMessageSelection("Chọn tin nhắn muốn TRÍCH DẪN TRẢ LỜI");
        string targetId = targetMsg?.ThreadId ?? "";
        ZaloThreadType threadType = targetMsg?.ThreadType ?? ZaloThreadType.User;
        string quoteMsgId = targetMsg?.MsgId ?? "";
        string quoteCliMsgId = targetMsg?.CliMsgId ?? "";
        string quoteSenderUid = targetMsg?.SenderUid ?? "";
        string quoteContent = targetMsg?.Content ?? "";

        if (threadType == ZaloThreadType.User && (string.IsNullOrEmpty(quoteSenderUid) || quoteSenderUid == "0" || quoteSenderUid == "Bạn (Chính mình)"))
        {
            quoteSenderUid = targetId;
        }

        if (targetMsg == null)
        {
            (targetId, threadType) = ResolveTargetSelection();
            if (string.IsNullOrEmpty(targetId)) return;

            System.Console.Write("[NHẬP] Nhập Message ID tin nhắn gốc (msgId): ");
            quoteMsgId = System.Console.ReadLine()?.Trim() ?? "";
            System.Console.Write("[NHẬP] Nhập Client Message ID tin nhắn gốc (cliMsgId): ");
            quoteCliMsgId = System.Console.ReadLine()?.Trim() ?? "";
            System.Console.Write("[NHẬP] Nhập UID người gửi tin nhắn gốc (quoteSenderUid): ");
            quoteSenderUid = System.Console.ReadLine()?.Trim() ?? "";
            System.Console.Write("[NHẬP] Nhập nội dung tin nhắn gốc (trích dẫn): ");
            quoteContent = System.Console.ReadLine()?.Trim() ?? "";
        }

        System.Console.Write("[NHẬP] Nhập nội dung tin nhắn trả lời của bạn: ");
        string? text = System.Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(quoteMsgId) || string.IsNullOrEmpty(quoteCliMsgId) || string.IsNullOrEmpty(quoteSenderUid)) return;

        long quoteTs = targetMsg?.TimestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        System.Console.WriteLine($"[DEBUG QUOTE PARAMS] targetId={targetId}, threadType={threadType}, quoteMsgId={quoteMsgId}, quoteCliMsgId={quoteCliMsgId}, quoteSenderUid={quoteSenderUid}, quoteTs={quoteTs}, quoteContent={quoteContent}");

        try
        {
            string sentMsgId = await ZaloWebClient.SendQuoteMessageAsync(session, targetId, threadType, text, quoteMsgId, quoteCliMsgId, quoteSenderUid, quoteContent, quoteTs, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã gửi tin nhắn trích dẫn trả lời! (MsgID: {sentMsgId})");
            AddRecentMessage(new RecentMessageInfo(targetId, threadType, sentMsgId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), session.Uid, "Bạn (Chính mình)", text, DateTime.Now, quoteTs));
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Gửi tin nhắn trích dẫn thất bại: {ex.Message}");
        }
    }

    private static async Task HandleAddReactionAsync(ZaloSession session, CancellationToken ct)
    {
        RecentMessageInfo? targetMsg = ResolveMessageSelection("Chọn tin nhắn muốn THẢ CẢM XÚC");
        string targetId = targetMsg?.ThreadId ?? "";
        ZaloThreadType threadType = targetMsg?.ThreadType ?? ZaloThreadType.User;
        string msgId = targetMsg?.MsgId ?? "";
        string cliMsgId = targetMsg?.CliMsgId ?? "";

        if (targetMsg == null)
        {
            (targetId, threadType) = ResolveTargetSelection();
            if (string.IsNullOrEmpty(targetId)) return;

            System.Console.Write("[NHẬP] Nhập Message ID (msgId): ");
            msgId = System.Console.ReadLine()?.Trim() ?? "";
            System.Console.Write("[NHẬP] Nhập Client Message ID (cliMsgId): ");
            cliMsgId = System.Console.ReadLine()?.Trim() ?? "";
        }

        System.Console.WriteLine("\n[DANH SÁCH BỘ BIỂU CẢM (REACTION)]");
        System.Console.WriteLine(" 1: Heart ❤️  | 2: Like 👍 | 3: Haha 😄 | 4: Wow 😮");
        System.Console.WriteLine(" 5: Cry 😢    | 6: Angry 😡| 7: Love 😍 | 8: Rose 🌹");
        System.Console.Write("[NHẬP] Chọn số tương ứng [Mặc định 1 - ❤️]: ");
        string? reactChoice = System.Console.ReadLine()?.Trim();

        ZaloReactionType reaction = reactChoice switch
        {
            "1" => ZaloReactionType.Heart,
            "2" => ZaloReactionType.Like,
            "3" => ZaloReactionType.Haha,
            "4" => ZaloReactionType.Wow,
            "5" => ZaloReactionType.Cry,
            "6" => ZaloReactionType.Angry,
            "7" => ZaloReactionType.Love,
            "8" => ZaloReactionType.Rose,
            _ => ZaloReactionType.Heart
        };

        if (string.IsNullOrEmpty(msgId) || string.IsNullOrEmpty(cliMsgId)) return;

        try
        {
            await ZaloWebClient.AddReactionAsync(session, targetId, msgId, cliMsgId, threadType, reaction, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã thả cảm xúc {reaction} cho tin nhắn (MsgID: {msgId}) thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Thả cảm xúc thất bại: {ex.Message}");
        }
    }

    private static async Task HandleSendStickerAsync(ZaloSession session, CancellationToken ct)
    {
        (string targetId, ZaloThreadType threadType) = ResolveTargetSelection();
        if (string.IsNullOrEmpty(targetId)) return;

        System.Console.WriteLine("\n[GỢI Ý STICKER ZALO PHỔ BIẾN]");
        System.Console.WriteLine(" 1: Ami Béo đáng yêu (Sticker ID: 24068, Cate ID: 622)");
        System.Console.WriteLine(" 2: Mèo Cười Vui (Sticker ID: 24069, Cate ID: 622)");
        System.Console.WriteLine(" 3: Sticker Tự Nhập ID");
        System.Console.Write("[NHẬP] Chọn sticker [Mặc định 1]: ");
        string? stChoice = System.Console.ReadLine()?.Trim();

        int stickerId = 24068;
        int cateId = 622;
        int stickerType = 1;

        if (stChoice == "2")
        {
            stickerId = 24069;
            cateId = 622;
        }
        else if (stChoice == "3")
        {
            System.Console.Write("[NHẬP] Nhập Sticker ID (ví dụ: 24068): ");
            _ = int.TryParse(System.Console.ReadLine()?.Trim(), out stickerId);
            System.Console.Write("[NHẬP] Nhập Category ID (cateId, ví dụ: 622): ");
            _ = int.TryParse(System.Console.ReadLine()?.Trim(), out cateId);
            System.Console.Write("[NHẬP] Nhập Sticker Type (ví dụ: 1): ");
            _ = int.TryParse(System.Console.ReadLine()?.Trim(), out stickerType);
        }

        if (stickerId <= 0) return;

        try
        {
            await ZaloWebClient.SendStickerAsync(session, targetId, stickerId, cateId, stickerType > 0 ? stickerType : 1, threadType, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã gửi sticker (ID: {stickerId}) thành công!");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Gửi sticker thất bại: {ex.Message}");
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
