// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;
using Zalo.Net.Mcp.Data;

namespace Zalo.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Zalo Messaging operations: Sending text, stickers, files, attachments,
/// reactions, message undo, and querying history/search with isolated database per account.
/// </summary>
[McpServerToolType]
public sealed class MessageTools(ZaloSessionManager sessionManager)
{
    private readonly ZaloSessionManager _sessionManager = sessionManager;

    private (ZaloSession Session, MessageRepository Repository) ResolveAccount(string? accountUid)
    {
        this._sessionManager.EnsureAuthenticated(accountUid);
        ZaloAccountContext account = this._sessionManager.GetAccount(accountUid)!;
        return (account.Session, account.Repository);
    }

    [McpServerTool(Name = "zalo_send_text")]
    [Description("Send a text message to a user or group chat on Zalo.")]
    public async Task<string> SendTextAsync(
        [Description("Recipient User ID or Group ID (threadId)")] string threadId,
        [Description("Conversation type: 'User' (direct) or 'Group' (group chat)")] string threadType,
        [Description("Text message content to send")] string text,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, MessageRepository repository) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        ZaloSendResult result = await ZaloWebClient.SendTextAsync(session, threadId, type, text, ct).ConfigureAwait(false);

        // Record outgoing message in isolated SQLite database for this account
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
        await repository.SaveMessageAsync(outgoingMsg, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = result.MsgId, thread_id = threadId, from_uid = session.Uid };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_broadcast_message")]
    [Description("Broadcast the same message to multiple recipients or group chats on Zalo.")]
    public async Task<string> BroadcastMessageAsync(
        [Description("Comma-separated list of recipient User IDs or Group IDs")] string threadIdsCsv,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Broadcast message content")] string text,
        [Description("Delay between sends in milliseconds (default 1000ms)")] int delayMs = 1000,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(threadIdsCsv);

        (ZaloSession session, _) = this.ResolveAccount(accountUid);

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
    [Description("Send a Zalo sticker into a conversation.")]
    public async Task<string> SendStickerAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Sticker ID")] int stickerId,
        [Description("Sticker category ID (cateId)")] int cateId,
        [Description("Sticker type (default 1)")] int stickerType = 1,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        await ZaloWebClient.SendStickerAsync(session, threadId, stickerId, cateId, stickerType, type, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, sticker_id = stickerId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_send_file")]
    [Description("Send an attachment or document file to a Zalo conversation.")]
    public async Task<string> SendFileAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Absolute file path on local disk")] string filePath,
        [Description("Optional file caption text")] string? caption = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
        {
            return JsonSerializer.Serialize(new { error = $"File does not exist at path: {filePath}" });
        }

        (ZaloSession session, _) = this.ResolveAccount(accountUid);

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
    [Description("Send bank account details card for funds transfer via Zalo.")]
    public async Task<string> SendBankCardAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Bank BIN code (e.g. '970458' for TPBank, '970436' for Vietcombank, '970422' for MBBank)")] string binBank,
        [Description("Bank account number")] string accountNumber,
        [Description("Bank account holder name")] string accountName,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        using ZaloWebClient client = new(session.Proxy);
        await client.SendBankCardAsync(session, threadId, type, binBank, accountNumber, accountName, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, bin_bank = binBank, account_number = accountNumber, account_name = accountName };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_send_contact_card")]
    [Description("Send a contact recommendation card into a conversation.")]
    public async Task<string> SendContactCardAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("User ID of the recommended contact")] string targetUserId,
        [Description("Optional phone number")] string? phoneNumber = null,
        [Description("Optional QR code profile URL")] string? qrCodeUrl = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        using ZaloWebClient client = new(session.Proxy);
        await client.SendContactCardAsync(session, threadId, type, targetUserId, phoneNumber, qrCodeUrl, ct).ConfigureAwait(false);

        var response = new { status = "success", thread_id = threadId, contact_user_id = targetUserId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_quote_message")]
    [Description("Quote (reply to) a specific message in a Zalo conversation.")]
    public async Task<string> QuoteMessageAsync(
        [Description("Recipient User ID or Group ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Reply message text content")] string text,
        [Description("MsgId of the message being quoted")] string quoteMsgId,
        [Description("CliMsgId of the message being quoted")] string quoteCliMsgId,
        [Description("Sender User ID of the message being quoted")] string quoteSenderUid,
        [Description("Content of the message being quoted")] string quoteContent,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        string msgId = await ZaloWebClient.SendQuoteMessageAsync(
            session, threadId, type, text, quoteMsgId, quoteCliMsgId, quoteSenderUid, quoteContent, ct: ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = msgId, thread_id = threadId };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_react_message")]
    [Description("Add an emoji reaction (Haha, Like, Heart, Wow, Cry, Angry, Kiss, Love, Dislike) to a message.")]
    public async Task<string> ReactMessageAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Target message MsgId")] string msgId,
        [Description("Target message CliMsgId")] string cliMsgId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Reaction type: 'Haha', 'Like', 'Heart', 'Wow', 'Cry', 'Angry', 'Kiss', 'Love', 'Dislike'")] string reaction,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

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
    [Description("Recall (undo/delete) a previously sent Zalo message.")]
    public async Task<string> RecallMessageAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Target message MsgId to recall")] string msgId,
        [Description("Target message CliMsgId to recall")] string cliMsgId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        await ZaloWebClient.UndoMessageAsync(session, threadId, msgId, cliMsgId, type, ct).ConfigureAwait(false);

        var response = new { status = "success", msg_id = msgId, action = "recalled" };
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "zalo_search_messages")]
    [Description("Search historical messages in local SQLite database by keyword.")]
    public async Task<string> SearchMessagesAsync(
        [Description("Keyword to search within message content")] string keyword,
        [Description("Maximum number of results to return (default 50)")] int limit = 50,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this._sessionManager.GetAccount(accountUid)?.Repository ?? this._sessionManager.Repository;
        IReadOnlyList<SavedMessage> results = await repo.SearchMessagesAsync(keyword, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(results);
    }

    [McpServerTool(Name = "zalo_get_chat_history")]
    [Description("Get recent chat message history from local SQLite database (sub-millisecond query).")]
    public async Task<string> GetChatHistoryAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Maximum number of recent messages to retrieve (default 50)")] int limit = 50,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        MessageRepository repo = this._sessionManager.GetAccount(accountUid)?.Repository ?? this._sessionManager.Repository;
        IReadOnlyList<SavedMessage> history = await repo.GetChatHistoryAsync(threadId, limit, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(history);
    }

    [McpServerTool(Name = "zalo_send_image")]
    [Description("Send a photo or image from a local file path or web URL into a Zalo conversation.")]
    public async Task<string> SendImageAsync(
        [Description("Conversation ID (ThreadId)")] string threadId,
        [Description("Conversation type: 'User' or 'Group'")] string threadType,
        [Description("Local file path (e.g. C:\\image.png) or image HTTP URL")] string imagePathOrUrl,
        [Description("Optional image caption text")] string? caption = null,
        [Description("Target Zalo account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (ZaloSession session, _) = this.ResolveAccount(accountUid);

        ZaloThreadType type = Enum.TryParse<ZaloThreadType>(threadType, ignoreCase: true, out ZaloThreadType parsedType)
            ? parsedType
            : ZaloThreadType.User;

        byte[] bytes;
        string fileName = "image.png";

        if (imagePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using HttpClient dlClient = new();
            bytes = await dlClient.GetByteArrayAsync(imagePathOrUrl, ct).ConfigureAwait(false);
            fileName = Path.GetFileName(new Uri(imagePathOrUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "image.png";
            }
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

        CookieStore cookies = CookieStore.FromJson(session.Material.CookiesJson);
        using ZaloHttpClient zaloHttp = new(session.Material.UserAgent, cookies, session.Proxy);
        ZaloSendResult result = await AttachmentApis.SendImageAttachmentAsync(zaloHttp, session, threadId, type, bytes, fileName, caption, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", msg_id = result.MsgId, thread_id = threadId, file_name = fileName, size_bytes = bytes.Length });
    }

    [McpServerTool(Name = "zalo_download_attachment")]
    [Description("Download an image, document, or audio attachment from Zalo to local disk.")]
    public async Task<string> DownloadAttachmentAsync(
        [Description("URL of the attachment/file to download")] string fileUrl,
        [Description("Optional destination file name (defaults to URL filename)")] string? outputFileName = null,
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

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"zalo_download_{DateTime.UtcNow.Ticks}.bin";
        }
        string savePath = Path.Combine(downloadsDir, fileName);

        using HttpClient http = new();
        byte[] data = await http.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(savePath, data, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { status = "success", saved_path = savePath, file_name = fileName, size_bytes = data.Length });
    }

    /// <summary>Overload for calling without accountUid.</summary>
    public Task<string> SendImageAsync(string threadId, string threadType, string imagePathOrUrl, string? caption, CancellationToken ct) =>
        this.SendImageAsync(threadId, threadType, imagePathOrUrl, caption, accountUid: null, ct);
}
