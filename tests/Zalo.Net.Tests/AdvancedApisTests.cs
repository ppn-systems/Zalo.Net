// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Zalo.Net.Auth;
using Zalo.Net.Contracts;
using Zalo.Net.Endpoints;

namespace Zalo.Net.Tests;

public sealed class AdvancedApisTests
{
    [Fact]
    public void Hosts_GetCandidates_IncludesBoardLabelQuickMessageAndAlias()
    {
        string[] boardHosts = ZaloConstants.Hosts.GetCandidates("group_board");
        Assert.Contains(ZaloConstants.Hosts.GroupBoard, boardHosts);

        string[] labelHosts = ZaloConstants.Hosts.GetCandidates("label");
        Assert.Contains(ZaloConstants.Hosts.Label, labelHosts);

        string[] qmHosts = ZaloConstants.Hosts.GetCandidates("quick_message");
        Assert.Contains(ZaloConstants.Hosts.QuickMessage, qmHosts);

        string[] aliasHosts = ZaloConstants.Hosts.GetCandidates("alias");
        Assert.Contains(ZaloConstants.Hosts.Alias, aliasHosts);
    }

    [Fact]
    public void ZaloSession_GetServiceUrl_ResolvesFromServiceMap()
    {
        Dictionary<string, string[]> map = new()
        {
            ["group_board"] = ["https://custom-board.chat.zalo.me"],
            ["empty"] = []
        };
        ZaloSessionMaterial mat = new("[]", "dGVzdC1zZWNyZXQta2V5LTI0Ynl0ZXM=", "imei", "uid", "ua");
        ZaloSession session = new(mat, "uid", [], map, 20000);

        Assert.Equal("https://custom-board.chat.zalo.me", session.GetServiceUrl("group_board"));
        Assert.Null(session.GetServiceUrl("empty"));
        Assert.Null(session.GetServiceUrl("missing"));
    }

    [Fact]
    public void ZaloJsonContext_SerializesAllAdvancedModels_AotSafe()
    {
        ZaloGroupLinkDetail link = new("https://zalo.me/g/abc123", 1700000000, true);
        string linkJson = JsonSerializer.Serialize(link, ZaloJsonContext.Default.ZaloGroupLinkDetail);
        Assert.Contains("abc123", linkJson, StringComparison.Ordinal);

        ZaloPollDetail poll = new(
            PollId: 99,
            CreatorId: "u1",
            Question: "Lunch?",
            Closed: false,
            AllowMultiChoices: true,
            AllowAddNewOption: false,
            IsAnonymous: false,
            HideVotePreview: false,
            TotalVotes: 3,
            CreatedTime: 100,
            ExpiredTime: 200,
            Options: [new ZaloPollOption(1, "Pho", 3, true, ["u1"])]);
        string pollJson = JsonSerializer.Serialize(poll, ZaloJsonContext.Default.ZaloPollDetail);
        Assert.Contains("Pho", pollJson, StringComparison.Ordinal);

        ZaloBoardItem note = new("n1", 1, "Meeting notes", "u1", 100, 100);
        string noteJson = JsonSerializer.Serialize(note, ZaloJsonContext.Default.ZaloBoardItem);
        Assert.Contains("Meeting notes", noteJson, StringComparison.Ordinal);

        ZaloReminderItem rem = new("r1", "g1", true, "Standup", "u1", "⏰", 1000, ZaloReminderRepeatMode.Daily, 900);
        string remJson = JsonSerializer.Serialize(rem, ZaloJsonContext.Default.ZaloReminderItem);
        Assert.Contains("Standup", remJson, StringComparison.Ordinal);

        ZaloLabelItem label = new(1, "VIP", "vip", "#FF0000", "⭐", ["u1", "u2"], 100);
        string labelJson = JsonSerializer.Serialize(label, ZaloJsonContext.Default.ZaloLabelItem);
        Assert.Contains("VIP", labelJson, StringComparison.Ordinal);

        ZaloQuickMessage qm = new(5, "hi", "Xin chào quý khách!", 0, 100, 100);
        string qmJson = JsonSerializer.Serialize(qm, ZaloJsonContext.Default.ZaloQuickMessage);
        Assert.Contains("Xin ch", qmJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateHiddenConversPin_ValidatesFourDigitPin()
    {
        ZaloSessionMaterial mat = new("[]", "dGVzdC1zZWNyZXQta2V5LTI0Ynl0ZXM=", "imei", "uid", "ua");
        ZaloSession session = new(mat, "uid", [], new Dictionary<string, string[]>(), 20000);
        using ZaloHttpClient http = new("ua");

        _ = await Assert.ThrowsAsync<ArgumentException>(() =>
            ConversationApis.UpdateHiddenConversPinAsync(http, session, "12ab"));
        _ = await Assert.ThrowsAsync<ArgumentException>(() =>
            ConversationApis.UpdateHiddenConversPinAsync(http, session, "12345"));
    }
}
