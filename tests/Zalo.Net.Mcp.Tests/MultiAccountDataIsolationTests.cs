// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using Zalo.Net.Contracts;
using Zalo.Net.Mcp.Data;
using Zalo.Net.Mcp.Tools;

namespace Zalo.Net.Mcp.Tests;

/// <summary>
/// Verifies complete physical data isolation between multiple Zalo accounts and non-blocking concurrent performance.
/// </summary>
public sealed class MultiAccountDataIsolationTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly ZaloDatabase _masterDb;
    private readonly MessageRepository _masterRepo;

    public MultiAccountDataIsolationTests()
    {
        this._testBaseDir = Path.Combine(Path.GetTempPath(), "zalo-mcp-isolation-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(this._testBaseDir);

        string masterDbPath = Path.Combine(this._testBaseDir, "zalo_master.db");
        this._masterDb = new ZaloDatabase(masterDbPath);
        this._masterDb.Initialize();
        this._masterRepo = new MessageRepository(this._masterDb);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(this._testBaseDir, recursive: true);
        }
        catch (IOException) { }
    }

    private static ZaloMessageEvent CreateMessage(string msgId, string threadId, string senderUid, string text)
    {
        return new ZaloMessageEvent(
            MsgId: msgId,
            CliMsgId: msgId,
            MsgType: "chat.text",
            UidFrom: senderUid,
            IdTo: "recipient",
            DisplayName: "Sender " + senderUid,
            ThreadId: threadId,
            ThreadType: ZaloThreadType.User,
            TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            Content: text,
            Attachments: null,
            IsSelf: false);
    }

    [Fact]
    public async Task MultiAccount_PhysicalDatabases_CompletelyIsolateMessagesAndSearch()
    {
        // Arrange: Account Alpha and Account Beta with separate databases
        ZaloDatabase dbAlpha = ZaloDatabase.ForAccount("user_alpha", this._testBaseDir);
        dbAlpha.Initialize();
        MessageRepository repoAlpha = new(dbAlpha);

        ZaloDatabase dbBeta = ZaloDatabase.ForAccount("user_beta", this._testBaseDir);
        dbBeta.Initialize();
        MessageRepository repoBeta = new(dbBeta);

        // Act: Save distinct confidential information into each account
        await repoAlpha.SaveMessageAsync(CreateMessage("msg_a1", "thread_1", "user_alpha", "Dự án bí mật Alpha mã số AL-9988"), CancellationToken.None);
        await repoBeta.SaveMessageAsync(CreateMessage("msg_b1", "thread_2", "user_beta", "Dự án mật Beta tài liệu BE-1122"), CancellationToken.None);

        // Assert: Alpha search only finds Alpha data
        IReadOnlyList<SavedMessage> alphaResults = await repoAlpha.SearchMessagesAsync("Alpha", 50, CancellationToken.None);
        _ = Assert.Single(alphaResults);
        Assert.Equal("msg_a1", alphaResults[0].MsgId);

        IReadOnlyList<SavedMessage> alphaLeakCheck = await repoAlpha.SearchMessagesAsync("Beta", 50, CancellationToken.None);
        Assert.Empty(alphaLeakCheck);

        // Assert: Beta search only finds Beta data
        IReadOnlyList<SavedMessage> betaResults = await repoBeta.SearchMessagesAsync("Beta", 50, CancellationToken.None);
        _ = Assert.Single(betaResults);
        Assert.Equal("msg_b1", betaResults[0].MsgId);

        IReadOnlyList<SavedMessage> betaLeakCheck = await repoBeta.SearchMessagesAsync("Alpha", 50, CancellationToken.None);
        Assert.Empty(betaLeakCheck);
    }

    [Fact]
    public async Task MultiAccount_EntityExtraction_CompletelyIsolatesBankCardsAndPhoneNumbers()
    {
        // Arrange
        ZaloDatabase dbAlpha = ZaloDatabase.ForAccount("user_alpha", this._testBaseDir);
        dbAlpha.Initialize();
        MessageRepository repoAlpha = new(dbAlpha);

        ZaloDatabase dbBeta = ZaloDatabase.ForAccount("user_beta", this._testBaseDir);
        dbBeta.Initialize();
        MessageRepository repoBeta = new(dbBeta);

        // Act: Ingest messages containing sensitive entities (Bank account, Phone number)
        await repoAlpha.SaveMessageAsync(CreateMessage("msg_a_stk", "thread_1", "user_alpha", "Vui lòng chuyển khoản STK 970458123456789 ngân hàng TPBank"), CancellationToken.None);
        await repoBeta.SaveMessageAsync(CreateMessage("msg_b_stk", "thread_2", "user_beta", "Số tài khoản nhận tiền 970436987654321 Vietcombank"), CancellationToken.None);

        // Assert: Extracted entities are fully isolated
        IReadOnlyList<ExtractedEntity> entitiesAlpha = await repoAlpha.GetExtractedEntitiesAsync("bank_card", 50, null, CancellationToken.None);
        _ = Assert.Single(entitiesAlpha);
        Assert.Contains("970458123456789", entitiesAlpha[0].Value, StringComparison.Ordinal);

        IReadOnlyList<ExtractedEntity> entitiesBeta = await repoBeta.GetExtractedEntitiesAsync("bank_card", 50, null, CancellationToken.None);
        _ = Assert.Single(entitiesBeta);
        Assert.Contains("970436987654321", entitiesBeta[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiAccount_ConcurrentWrites_ExecuteInParallelWithoutLockContention()
    {
        // Arrange: Multiple account databases
        ZaloDatabase db1 = ZaloDatabase.ForAccount("user_concur_1", this._testBaseDir);
        db1.Initialize();
        MessageRepository repo1 = new(db1);

        ZaloDatabase db2 = ZaloDatabase.ForAccount("user_concur_2", this._testBaseDir);
        db2.Initialize();
        MessageRepository repo2 = new(db2);

        // Act: Concurrently write 50 messages to both databases in parallel
        List<Task> writeTasks = [];
        for (int i = 0; i < 50; i++)
        {
            int index = i;
            writeTasks.Add(Task.Run(async () =>
            {
                await repo1.SaveMessageAsync(CreateMessage($"msg_p1_{index}", "t1", "u1", $"Parallel test message 1 - {index}"), CancellationToken.None);
                await repo2.SaveMessageAsync(CreateMessage($"msg_p2_{index}", "t2", "u2", $"Parallel test message 2 - {index}"), CancellationToken.None);
            }));
        }

        await Task.WhenAll(writeTasks);

        // Assert: All 50 messages persisted in each isolated database
        IReadOnlyList<SavedMessage> history1 = await repo1.GetChatHistoryAsync("t1", 100, CancellationToken.None);
        IReadOnlyList<SavedMessage> history2 = await repo2.GetChatHistoryAsync("t2", 100, CancellationToken.None);

        Assert.Equal(50, history1.Count);
        Assert.Equal(50, history2.Count);
    }

    [Fact]
    public async Task MultiAccount_SessionManager_ListSwitchAndManageAccounts()
    {
        // Arrange
        using ZaloSessionManager sessionManager = new(this._masterRepo);

        ZaloSessionMaterial mat1 = new(
            CookiesJson: "[]",
            SecretKey: "key-1",
            Imei: "imei-1",
            Uid: "user_sm_1",
            UserAgent: "TestUA");

        ZaloSessionMaterial mat2 = new(
            CookiesJson: "[]",
            SecretKey: "key-2",
            Imei: "imei-2",
            Uid: "user_sm_2",
            UserAgent: "TestUA");

        // Save materials into master sessions
        await this._masterRepo.SaveSessionMaterialAsync(mat1.Uid, mat1, CancellationToken.None);
        await this._masterRepo.SaveSessionMaterialAsync(mat2.Uid, mat2, CancellationToken.None);

        // Setup AccountContexts directly
        ZaloDatabase db1 = ZaloDatabase.ForAccount(mat1.Uid, this._testBaseDir);
        db1.Initialize();
        MessageRepository repo1 = new(db1);
        MessageIngestPipeline ingest1 = new(repo1);
        ZaloSession s1 = new(mat1, mat1.Uid, ["wss://dummy.ws"], new Dictionary<string, string[]>(), 30000);
        ZaloAccountContext ctx1 = new(mat1.Uid, "User Alpha", null, s1, new ZaloWebClient(), db1, repo1, ingest1);

        ZaloDatabase db2 = ZaloDatabase.ForAccount(mat2.Uid, this._testBaseDir);
        db2.Initialize();
        MessageRepository repo2 = new(db2);
        MessageIngestPipeline ingest2 = new(repo2);
        ZaloSession s2 = new(mat2, mat2.Uid, ["wss://dummy.ws"], new Dictionary<string, string[]>(), 30000);
        ZaloAccountContext ctx2 = new(mat2.Uid, "User Beta", null, s2, new ZaloWebClient(), db2, repo2, ingest2);

        // Ingest a unique message in each account
        await repo1.SaveMessageAsync(CreateMessage("m1", "t1", mat1.Uid, "Data Alpha"), CancellationToken.None);
        await repo2.SaveMessageAsync(CreateMessage("m2", "t2", mat2.Uid, "Data Beta"), CancellationToken.None);

        // Register accounts directly in SessionManager
        sessionManager.AddAccountContext(ctx1);
        sessionManager.AddAccountContext(ctx2);

        AuthTools authTools = new(sessionManager);
        SmartTools smartTools = new(sessionManager);

        // Test list accounts
        string listJson = await authTools.ListAccountsAsync(CancellationToken.None);
        Assert.Contains(mat1.Uid, listJson, StringComparison.Ordinal);
        Assert.Contains(mat2.Uid, listJson, StringComparison.Ordinal);

        // Act: Test tools routing with explicit accountUid
        string alphaResultJson = await smartTools.SmartSearchAsync("Alpha", 10, mat1.Uid, CancellationToken.None);
        Assert.Contains("Data Alpha", alphaResultJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Beta", alphaResultJson, StringComparison.Ordinal);

        string betaResultJson = await smartTools.SmartSearchAsync("Beta", 10, mat2.Uid, CancellationToken.None);
        Assert.Contains("Data Beta", betaResultJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Alpha", betaResultJson, StringComparison.Ordinal);

        // Test switch account
        string switchJson = await authTools.SwitchAccountAsync(mat2.Uid, CancellationToken.None);
        Assert.Contains("true", switchJson, StringComparison.Ordinal);
        Assert.Equal(mat2.Uid, sessionManager.ActiveAccountUid);

        // Default query without accountUid should now route to mat2 (active account)
        string activeDefaultJson = await smartTools.SmartSearchAsync("Beta", 10, null, CancellationToken.None);
        Assert.Contains("Data Beta", activeDefaultJson, StringComparison.Ordinal);
    }
}
