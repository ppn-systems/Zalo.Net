// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Messaging, Sticker, Attachments, Bank Cards, Contact Cards, and Local SQLite Search.
/// </summary>
[McpServerToolType]
public sealed class MessageTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    [McpServerTool(Name = "zalo_send_text")]
    [Description("Gửi tin nhắn văn bản đến người dùng cá nhân hoặc nhóm chat trên Zalo.")]
    public async Task<string> SendTextAsync(
        [Description("ID của người nhận (User ID) hoặc ID của nhóm chat (Group ID)")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' (cá nhân) hoặc 'Group' (nhóm chat)")] string threadType,
        [Description("Nội dung tin nhắn cần gửi")] string text,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        ZaloSendResult result = await ZaloWebClient.SendTextAsync(session, threadId, type, text, ct).ConfigureAwait(false);

        // Record outgoing message in SQLite database
        ZaloMessageEvent outgoingMsg = new(
            MsgId: result.MsgId,
            CliMsgId: result.MsgId,
            MsgType: "text",
            UidFrom: session.Uid,
            IdTo: threadId,
            DisplayName: "Self",
            ThreadId: threadId,
            ThreadType: type,
            TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            Content: text,
            Attachments: null,
            IsSelf: true
        );
        await this._sessionManager.Repository.SaveMessageAsync(outgoingMsg, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = result.MsgId, thread_id = threadId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_broadcast_message")]
    [Description("Gửi cùng một nội dung tin nhắn đến danh sách nhiều người nhận hoặc nhóm chat Zalo.")]
    public async Task<string> BroadcastMessageAsync(
        [Description("Danh sách ID người nhận hoặc Group ID (phân cách bằng dấu phẩy)")] string threadIdsCsv,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Nội dung tin nhắn phát thông báo")] string text,
        [Description("Thời gian chờ giữa các lần gửi (ms, mặc định 1000ms)")] int delayMs = 1000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(threadIdsCsv);

        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        string[] targets = threadIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int successCount = 0;
        List<string> sentMsgIds = [];

        foreach (string target in targets)
        {
            try
            {
                ZaloSendResult res = await ZaloWebClient.SendTextAsync(session, target, type, text, ct).ConfigureAwait(false);
                successCount++;
                sentMsgIds.Add(res.MsgId);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // Continue broadcast to next targets
            }
        }

        return JsonSerializer.Serialize(new { status = "success", total_targets = targets.Length, success_count = successCount, sent_msg_ids = sentMsgIds });
    }

    [McpServerTool(Name = "zalo_send_sticker")]
    [Description("Gửi nhãn dán (Sticker) Zalo vào cuộc trò chuyện.")]
    public async Task<string> SendStickerAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("ID của Sticker")] int stickerId,
        [Description("ID của danh mục Sticker (Category ID)")] int cateId,
        [Description("Loại Sticker (mặc định 1)")] int stickerType = 1,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        await ZaloWebClient.SendStickerAsync(session, threadId, stickerId, cateId, stickerType, type, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, sticker_id = stickerId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_send_file")]
    [Description("Gửi tệp tin đính kèm hoặc hình ảnh vào cuộc trò chuyện Zalo.")]
    public async Task<string> SendFileAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Đường dẫn tuyệt đối đến file trên đĩa cục bộ")] string filePath,
        [Description("Chú thích đi kèm tệp tin (tùy chọn)")] string? caption = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
        {
            return JsonSerializer.Serialize(new { error = $"Tệp tin không tồn tại tại đường dẫn: {filePath}" });
        }

        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        string fileName = Path.GetFileName(filePath);

        ZaloSendResult result = await ZaloWebClient.SendAttachmentAsync(session, threadId, type, fileBytes, fileName, caption, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = result.MsgId, thread_id = threadId, file_name = fileName };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_send_bank_card")]
    [Description("Gửi thông tin thẻ/tài khoản ngân hàng để nhận chuyển khoản qua Zalo.")]
    public async Task<string> SendBankCardAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Mã BIN ngân hàng (VD: '970458' cho TPBank, '970436' cho Vietcombank, '970422' cho MBBank)")] string binBank,
        [Description("Số tài khoản ngân hàng")] string accountNumber,
        [Description("Tên chủ tài khoản ngân hàng")] string accountName,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        using ZaloWebClient client = new(session.Proxy);
        await client.SendBankCardAsync(session, threadId, type, binBank, accountNumber, accountName, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, bin_bank = binBank, account_number = accountNumber, account_name = accountName };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_send_contact_card")]
    [Description("Gửi danh thiếp giới thiệu người dùng Zalo khác vào cuộc trò chuyện.")]
    public async Task<string> SendContactCardAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("User ID của danh thiếp được giới thiệu")] string targetUserId,
        [Description("Số điện thoại (tùy chọn)")] string? phoneNumber = null,
        [Description("QR Code Profile URL (tùy chọn)")] string? qrCodeUrl = null,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        using ZaloWebClient client = new(session.Proxy);
        await client.SendContactCardAsync(session, threadId, type, targetUserId, phoneNumber, qrCodeUrl, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, contact_user_id = targetUserId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_quote_message")]
    [Description("Trích dẫn (trả lời/reply) một tin nhắn cụ thể trong cuộc trò chuyện Zalo.")]
    public async Task<string> QuoteMessageAsync(
        [Description("ID của người nhận hoặc nhóm chat")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Nội dung tin nhắn phản hồi mới")] string text,
        [Description("MsgId của tin nhắn được trích dẫn")] string quoteMsgId,
        [Description("CliMsgId của tin nhắn được trích dẫn")] string quoteCliMsgId,
        [Description("User ID của người gửi tin nhắn được trích dẫn")] string quoteSenderUid,
        [Description("Nội dung của tin nhắn được trích dẫn")] string quoteContent,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        string msgId = await ZaloWebClient.SendQuoteMessageAsync(
            session, threadId, type, text, quoteMsgId, quoteCliMsgId, quoteSenderUid, quoteContent, ct: ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = msgId, thread_id = threadId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_react_message")]
    [Description("Thả cảm xúc biểu tượng (Haha, Like, Heart, Wow, Cry, Angry, Kiss, Love, Dislike) vào tin nhắn.")]
    public async Task<string> ReactMessageAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("MsgId của tin nhắn cần thả cảm xúc")] string msgId,
        [Description("CliMsgId của tin nhắn")] string cliMsgId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Loại cảm xúc: 'Haha', 'Like', 'Heart', 'Wow', 'Cry', 'Angry', 'Kiss', 'Love', 'Dislike'")] string reaction,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        ZaloReactionType reactType = Enum.TryParse<ZaloReactionType>(reaction, ignoreCase: true, out ZaloReactionType parsedReact)
            ? parsedReact
            : ZaloReactionType.Like;

        await ZaloWebClient.AddReactionAsync(session, threadId, msgId, cliMsgId, type, reactType, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = msgId, reaction = reactType.ToString() };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_recall_message")]
    [Description("Thu hồi (hủy) tin nhắn Zalo đã gửi.")]
    public async Task<string> RecallMessageAsync(
        [Description("ID của cuộc trò chuyện")] string threadId,
        [Description("MsgId của tin nhắn cần thu hồi")] string msgId,
        [Description("CliMsgId của tin nhắn cần thu hồi")] string cliMsgId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        await ZaloWebClient.UndoMessageAsync(session, threadId, msgId, cliMsgId, type, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = msgId, action = "recalled" };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_search_messages")]
    [Description("Tìm kiếm tin nhắn cũ trong cơ sở dữ liệu Local SQLite theo từ khóa.")]
    public async Task<string> SearchMessagesAsync(
        [Description("Từ khóa cần tìm kiếm trong nội dung tin nhắn")] string keyword,
        [Description("Số lượng kết quả tối đa cần lấy (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        IReadOnlyList<SavedMessage> results = await this._sessionManager.Repository.SearchMessagesAsync(keyword, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(results);
    }

    [McpServerTool(Name = "zalo_get_chat_history")]
    [Description("Lấy lịch sử trò chuyện mới nhất của một cuộc trò chuyện từ Local SQLite Database (tốc độ siêu nhanh).")]
    public async Task<string> GetChatHistoryAsync(
        [Description("ID của cuộc trò chuyện (ThreadId)")] string threadId,
        [Description("Số lượng tin nhắn gần nhất cần lấy (mặc định 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        IReadOnlyList<SavedMessage> history = await this._sessionManager.Repository.GetChatHistoryAsync(threadId, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(history);
    }

    [McpServerTool(Name = "zalo_send_image")]
    [Description("Gửi hình ảnh (photo/image) từ đường dẫn file local hoặc URL vào cuộc trò chuyện Zalo.")]
    public async Task<string> SendImageAsync(
        [Description("ID của cuộc trò chuyện (ThreadId)")] string threadId,
        [Description("Loại cuộc trò chuyện: 'User' hoặc 'Group'")] string threadType,
        [Description("Đường dẫn file local (ví dụ: C:\\image.png) hoặc URL hình ảnh")] string imagePathOrUrl,
        [Description("Chú thích đi kèm hình ảnh (tùy chọn)")] string? caption = null,
        CancellationToken ct = default)
    {
        this._sessionManager.EnsureAuthenticated();
        ZaloSession session = this._sessionManager.ActiveSession!;

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        byte[] bytes;
        string fileName = "image.png";

        if (imagePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using HttpClient http = new();
            bytes = await http.GetByteArrayAsync(imagePathOrUrl, ct).ConfigureAwait(false);
            fileName = Path.GetFileName(new Uri(imagePathOrUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "image.png";
        }
        else
        {
            if (!File.Exists(imagePathOrUrl))
            {
                throw new FileNotFoundException($"Local image file not found: {imagePathOrUrl}");
            }
            bytes = await File.ReadAllBytesAsync(imagePathOrUrl, ct).ConfigureAwait(false);
            fileName = Path.GetFileName(imagePathOrUrl);
        }

        using ZaloWebClient client = new(session.Proxy);
        await client.SendImageAsync(session, threadId, type, bytes, fileName, caption, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", thread_id = threadId, file_name = fileName, size_bytes = bytes.Length });
    }

    [McpServerTool(Name = "zalo_download_attachment")]
    [Description("Tải về hình ảnh, tệp tin PDF, tài liệu hoặc file ghi âm thoại từ Zalo về máy.")]
    public async Task<string> DownloadAttachmentAsync(
        [Description("URL của đính kèm/file cần tải")] string fileUrl,
        [Description("Tên file lưu lại trên máy (tùy chọn, mặc định lấy từ URL)")] string? outputFileName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

        string downloadsDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Downloads")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        _ = Directory.CreateDirectory(downloadsDir);

        string fileName = !string.IsNullOrWhiteSpace(outputFileName)
            ? outputFileName
            : Path.GetFileName(new Uri(fileUrl).AbsolutePath);

        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"zalo_download_{DateTime.UtcNow.Ticks}.bin";
        string savePath = Path.Combine(downloadsDir, fileName);

        using HttpClient http = new();
        byte[] data = await http.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(savePath, data, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", saved_path = savePath, file_name = fileName, size_bytes = data.Length });
    }
}
