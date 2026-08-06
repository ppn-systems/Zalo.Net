// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;

namespace Zalo.Net.Bot.Context;

/// <summary>
/// Encapsulates the execution context of an incoming Zalo event message and provides fluent reply helpers.
/// </summary>
public sealed class ZaloBotContext
{
    /// <summary>Gets the incoming message event data.</summary>
    public ZaloMessageEvent Message { get; }

    /// <summary>Gets the active Zalo session.</summary>
    public ZaloSession Session { get; }

    /// <summary>Gets the underlying Zalo Web Client instance.</summary>
    public IZaloClient Client { get; }

    /// <summary>Gets the target thread ID for replies.</summary>
    public string ThreadId => this.Message.ThreadId;

    /// <summary>Gets the thread type (User or Group).</summary>
    public ZaloThreadType ThreadType => this.Message.ThreadType;

    /// <summary>Gets the sender's User ID.</summary>
    public string SenderUid => this.Message.UidFrom;

    /// <summary>Gets the message text content.</summary>
    public string Content => this.Message.Content?.ToString() ?? string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloBotContext"/> class.
    /// </summary>
    public ZaloBotContext(ZaloMessageEvent message, ZaloSession session, IZaloClient client)
    {
        this.Message = message ?? throw new ArgumentNullException(nameof(message));
        this.Session = session ?? throw new ArgumentNullException(nameof(session));
        this.Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Sends a text reply to the current thread.</summary>
    public async Task<ZaloSendResult> ReplyTextAsync(string text, CancellationToken ct = default)
    {
        CookieStore cookies = CookieStore.FromJson(this.Session.Material.CookiesJson);
        using ZaloHttpClient http = new(this.Session.Material.UserAgent, cookies, this.Session.Proxy);
        string msgId = await MessageApis.SendTextAsync(http, this.Session, this.ThreadId, this.ThreadType, text, ct).ConfigureAwait(false);
        return new ZaloSendResult(msgId);
    }

    /// <summary>Quotes (replies to) the current message with new text.</summary>
    public async Task<string> ReplyQuoteAsync(string text, CancellationToken ct = default)
    {
        CookieStore cookies = CookieStore.FromJson(this.Session.Material.CookiesJson);
        using ZaloHttpClient http = new(this.Session.Material.UserAgent, cookies, this.Session.Proxy);
        return await MessageApis.SendQuoteAsync(
            http, this.Session, this.ThreadId, this.ThreadType, text,
            quoteMsgId: this.Message.MsgId,
            quoteCliMsgId: this.Message.CliMsgId,
            quoteSenderUid: this.Message.UidFrom,
            quoteContent: this.Message.Content?.ToString() ?? string.Empty,
            quoteTs: 0,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>Sends a bank card payload for payment/transfer.</summary>
    public async Task ReplyBankCardAsync(string binBank, string accountNumber, string accountName, CancellationToken ct = default)
    {
        using ZaloWebClient client = new(this.Session.Proxy);
        await client.SendBankCardAsync(this.Session, this.ThreadId, this.ThreadType, binBank, accountNumber, accountName, ct).ConfigureAwait(false);
    }

    /// <summary>Sends a sticker into the current thread.</summary>
    public async Task ReplyStickerAsync(int stickerId, int cateId, int stickerType = 1, CancellationToken ct = default)
    {
        CookieStore cookies = CookieStore.FromJson(this.Session.Material.CookiesJson);
        using ZaloHttpClient http = new(this.Session.Material.UserAgent, cookies, this.Session.Proxy);
        await StickerApis.SendStickerAsync(http, this.Session, this.ThreadId, stickerId, cateId, stickerType, this.ThreadType, ct).ConfigureAwait(false);
    }

    /// <summary>Sends an image into the current thread.</summary>
    public async Task ReplyPhotoAsync(byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default)
    {
        CookieStore cookies = CookieStore.FromJson(this.Session.Material.CookiesJson);
        using ZaloHttpClient http = new(this.Session.Material.UserAgent, cookies, this.Session.Proxy);
        _ = await AttachmentApis.SendImageAttachmentAsync(http, this.Session, this.ThreadId, this.ThreadType, imageBytes, fileName, caption, ct).ConfigureAwait(false);
    }

    /// <summary>Adds a reaction icon to the current message.</summary>
    public async Task AddReactionAsync(ZaloReactionType reaction, CancellationToken ct = default)
    {
        CookieStore cookies = CookieStore.FromJson(this.Session.Material.CookiesJson);
        using ZaloHttpClient http = new(this.Session.Material.UserAgent, cookies, this.Session.Proxy);
        await ReactionApis.AddReactionAsync(http, this.Session, this.ThreadId, this.Message.MsgId, this.Message.CliMsgId, this.ThreadType, reaction, ct).ConfigureAwait(false);
    }
}
