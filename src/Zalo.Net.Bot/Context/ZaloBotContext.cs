// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;

namespace Zalo.Net.Bot.Context;

/// <summary>
/// Encapsulates the execution context of an incoming Zalo event message and provides fluent reply helpers.
/// </summary>
public sealed class ZaloBotContext
{
    private readonly string _content;

    /// <summary>Gets the incoming message event data.</summary>
    public ZaloMessageEvent Message { get; }

    /// <summary>Gets the active Zalo session.</summary>
    public ZaloSession Session { get; }

    /// <summary>Gets the underlying Zalo Web Client instance.</summary>
    public IZaloClient Client { get; }

    /// <summary>Gets or sets optional service provider for dependency injection.</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>Gets user-defined contextual data dictionary for middleware pipeline state sharing.</summary>
    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    /// <summary>Gets the target thread ID for replies.</summary>
    public string ThreadId => this.Message.ThreadId;

    /// <summary>Gets the thread type (User or Group).</summary>
    public ZaloThreadType ThreadType => this.Message.ThreadType;

    /// <summary>Gets the sender's User ID.</summary>
    public string SenderUid => this.Message.UidFrom;

    /// <summary>Gets the cached zero-allocation cookie store for the active session.</summary>
    public CookieStore CookieStore => CookieStore.ForMaterial(this.Session.Material);

    /// <summary>Gets the message text content.</summary>
    public string Content => this._content;

    /// <summary>Gets the parsed command trigger (e.g. <c>/ping</c>, <c>!help</c>, <c>.echo</c>), or null if not a command.</summary>
    public string? Command { get; }

    /// <summary>Gets the raw argument string following the command trigger, trimmed.</summary>
    public string RawArguments { get; }

    /// <summary>Gets the tokenized command arguments, supporting quoted segments.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloBotContext"/> class.
    /// </summary>
    public ZaloBotContext(ZaloMessageEvent message, ZaloSession session, IZaloClient client, IServiceProvider? services = null)
    {
        this.Message = message ?? throw new ArgumentNullException(nameof(message));
        this.Session = session ?? throw new ArgumentNullException(nameof(session));
        this.Client = client ?? throw new ArgumentNullException(nameof(client));
        this.Services = services;

        this._content = message.Content?.ToString() ?? string.Empty;

        string trimmed = this._content.Trim();
        if (trimmed.Length > 0 && (trimmed[0] is '/' or '!' or '.'))
        {
            int spaceIdx = trimmed.IndexOf(' ', StringComparison.Ordinal);
            if (spaceIdx < 0)
            {
                this.Command = trimmed;
                this.RawArguments = string.Empty;
                this.Arguments = [];
            }
            else
            {
                this.Command = trimmed[..spaceIdx];
                string rawArgs = trimmed[(spaceIdx + 1)..].Trim();
                this.RawArguments = rawArgs;
                this.Arguments = ParseArguments(rawArgs);
            }
        }
        else
        {
            this.Command = null;
            this.RawArguments = string.Empty;
            this.Arguments = [];
        }
    }

    private static IReadOnlyList<string> ParseArguments(string rawArgs)
    {
        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            return [];
        }

        List<string> tokens = [];
        StringBuilder sb = new();
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < rawArgs.Length; i++)
        {
            char c = rawArgs[i];
            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                    tokens.Add(sb.ToString());
                    _ = sb.Clear();
                }
                else
                {
                    _ = sb.Append(c);
                }
            }
            else
            {
                if (c is '"' or '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        _ = sb.Clear();
                    }
                }
                else
                {
                    _ = sb.Append(c);
                }
            }
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    private ZaloHttpClient CreateHttp() =>
        new(this.Session.Material.UserAgent, CookieStore.ForMaterial(this.Session.Material), this.Session.Proxy);

    /// <summary>Sends a text reply to the current thread.</summary>
    public async Task<ZaloSendResult> ReplyTextAsync(string text, CancellationToken ct = default)
    {
        using ZaloHttpClient http = this.CreateHttp();
        string msgId = await MessageApis.SendTextAsync(http, this.Session, this.ThreadId, this.ThreadType, text, ct).ConfigureAwait(false);
        return new ZaloSendResult(msgId);
    }

    /// <summary>Quotes (replies to) the current message with new text.</summary>
    public async Task<string> ReplyQuoteAsync(string text, CancellationToken ct = default)
    {
        using ZaloHttpClient http = this.CreateHttp();
        return await MessageApis.SendQuoteAsync(
            http, this.Session, this.ThreadId, this.ThreadType, text,
            quoteMsgId: this.Message.MsgId,
            quoteCliMsgId: this.Message.CliMsgId,
            quoteSenderUid: this.Message.UidFrom,
            quoteContent: this._content,
            quoteTs: 0,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>Sends a bank card payload for payment/transfer.</summary>
    public Task ReplyBankCardAsync(string binBank, string accountNumber, string accountName, CancellationToken ct = default) =>
        this.Client.SendBankCardAsync(this.Session, this.ThreadId, this.ThreadType, binBank, accountNumber, accountName, ct);

    /// <summary>Sends a contact card into the current thread.</summary>
    public Task ReplyContactCardAsync(string userId, string? phoneNumber = null, string? qrCodeUrl = null, CancellationToken ct = default) =>
        this.Client.SendContactCardAsync(this.Session, this.ThreadId, this.ThreadType, userId, phoneNumber, qrCodeUrl, ct);

    /// <summary>Sends a sticker into the current thread.</summary>
    public async Task ReplyStickerAsync(int stickerId, int cateId, int stickerType = 1, CancellationToken ct = default)
    {
        using ZaloHttpClient http = this.CreateHttp();
        await StickerApis.SendStickerAsync(http, this.Session, this.ThreadId, stickerId, cateId, stickerType, this.ThreadType, ct).ConfigureAwait(false);
    }

    /// <summary>Sends an image into the current thread.</summary>
    public async Task ReplyPhotoAsync(byte[] imageBytes, string fileName, string? caption = null, CancellationToken ct = default)
    {
        using ZaloHttpClient http = this.CreateHttp();
        _ = await AttachmentApis.SendImageAttachmentAsync(http, this.Session, this.ThreadId, this.ThreadType, imageBytes, fileName, caption, ct).ConfigureAwait(false);
    }

    /// <summary>Adds a reaction icon to the current message.</summary>
    public async Task AddReactionAsync(ZaloReactionType reaction, CancellationToken ct = default)
    {
        using ZaloHttpClient http = this.CreateHttp();
        await ReactionApis.AddReactionAsync(http, this.Session, this.ThreadId, this.Message.MsgId, this.Message.CliMsgId, this.ThreadType, reaction, ct).ConfigureAwait(false);
    }
}
