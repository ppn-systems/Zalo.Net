using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            ConsoleTable menuTable = new("STT", "TÊN CHỨC NĂNG");
            menuTable.AddRow("1", "Gửi tin nhắn văn bản (Cá nhân / Nhóm)");
            menuTable.AddRow("2", "Danh sách bạn bè");
            menuTable.AddRow("3", "Tra cứu thông tin người dùng qua SĐT");
            menuTable.AddRow("4", "Gửi hình ảnh đính kèm");
            menuTable.AddRow("5", "Tải lịch sử tin nhắn cũ");
            menuTable.AddRow("6", "Tạo nhóm chat mới");
            menuTable.AddRow("7", "Thêm thành viên vào nhóm");
            menuTable.AddRow("8", "Đăng xuất khỏi phiên hiện tại");
            menuTable.AddRow("Q", "Thoát khỏi chương trình");
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
                    await HandleGetFriendsAsync(session, ct).ConfigureAwait(false);
                    break;

                case "3":
                    await HandleFindUserAsync(session, ct).ConfigureAwait(false);
                    break;

                case "4":
                    await HandleSendImageAsync(session, ct).ConfigureAwait(false);
                    break;

                case "5":
                    await HandleGetHistoryAsync(session, ct).ConfigureAwait(false);
                    break;

                case "6":
                    await HandleCreateGroupAsync(session, ct).ConfigureAwait(false);
                    break;

                case "7":
                    await HandleAddUserToGroupAsync(session, ct).ConfigureAwait(false);
                    break;

                case "8":
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

    private static async Task HandleGetHistoryAsync(ZaloSession session, CancellationToken ct)
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
        catch (Exception ex)
        {
            System.Console.WriteLine($"[CẢNH BÁO] Không thể tự động lấy danh sách nhóm trực tuyến: {ex.Message}");
        }

        string targetId = "";
        List<QuickTarget> groups = [.. s_registry.GetAll().Where(t => t.ThreadType == ZaloThreadType.Group)];

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

            System.Console.Write("[NHẬP] Chọn số thứ tự nhóm: ");
            string? input = System.Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            if (int.TryParse(input, out int idx) && idx > 0)
            {
                QuickTarget? found = groups.FirstOrDefault(g => g.Index == idx);
                if (found != null)
                {
                    targetId = found.TargetId;
                }
            }

            if (string.IsNullOrEmpty(targetId) && input != "0")
            {
                targetId = input;
            }
        }

        if (string.IsNullOrEmpty(targetId))
        {
            System.Console.Write("[NHẬP] Nhập Group ID cần tải lịch sử: ");
            targetId = System.Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(targetId))
        {
            return;
        }

        try
        {
            System.Console.WriteLine("[THÔNG BÁO] Đang tải lịch sử tin nhắn nhóm...");
            JsonNode? history = await ZaloWebClient.GetOldMessagesAsync(session, targetId, ZaloThreadType.Group, count: 50, ct).ConfigureAwait(false);
            System.Console.WriteLine($"[THÀNH CÔNG] Đã tải thành công dữ liệu lịch sử tin nhắn nhóm:");
            if (history is not null)
            {
                System.Console.WriteLine(history.ToJsonString());
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[LỖI] Không thể tải lịch sử tin nhắn: {ex.Message}");
        }
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
