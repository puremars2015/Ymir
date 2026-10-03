using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Persistence;

namespace Ymir.IntegrationTests.Persistence;

/// <summary>驗證由資料庫保證的規則（SA §14、§9.1、開發規劃 §9），不靠應用程式先查再寫。</summary>
public class PersistenceConstraintTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private async Task<(VibeMakerDbContext Db, AsyncServiceScope Scope, Conversation Conversation)> ArrangeConversationAsync()
    {
        _ = factory.Server; // 啟動 host（含 migrate）
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeMakerDbContext>();
        var workspace = Workspace.Create(Guid.NewGuid(), "ws", Now);
        var conversation = Conversation.Create(workspace, "chat", Now);
        db.AddRange(workspace, conversation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (db, scope, conversation);
    }

    [Fact]
    public async Task OnlyOneActiveExecutionPerConversation()
    {
        var (db, scope, conversation) = await ArrangeConversationAsync();
        await using var _ = scope;
        var ct = TestContext.Current.CancellationToken;

        var first = Message.CreateUser(conversation.Id, "1", 1, Now);
        db.AddRange(first, AgentExecution.Queue(conversation, first, Guid.NewGuid(), Now));
        await db.SaveChangesAsync(ct);

        var second = Message.CreateUser(conversation.Id, "2", 2, Now);
        db.AddRange(second, AgentExecution.Queue(conversation, second, Guid.NewGuid(), Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task FinishedExecutionsDoNotBlockNewOnes()
    {
        var (db, scope, conversation) = await ArrangeConversationAsync();
        await using var _ = scope;
        var ct = TestContext.Current.CancellationToken;

        var first = Message.CreateUser(conversation.Id, "1", 1, Now);
        var execution = AgentExecution.Queue(conversation, first, Guid.NewGuid(), Now);
        db.AddRange(first, execution);
        await db.SaveChangesAsync(ct);
        execution.Cancel(null, Now);
        await db.SaveChangesAsync(ct);

        var second = Message.CreateUser(conversation.Id, "2", 2, Now);
        db.AddRange(second, AgentExecution.Queue(conversation, second, Guid.NewGuid(), Now));
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task ClientRequestIdIsUniquePerUser()
    {
        var (db, scope, conversation) = await ArrangeConversationAsync();
        await using var _ = scope;
        var ct = TestContext.Current.CancellationToken;
        var clientRequestId = Guid.NewGuid();

        var first = Message.CreateUser(conversation.Id, "1", 1, Now);
        var execution = AgentExecution.Queue(conversation, first, clientRequestId, Now);
        db.AddRange(first, execution);
        await db.SaveChangesAsync(ct);
        execution.Cancel(null, Now);
        await db.SaveChangesAsync(ct);

        var retry = Message.CreateUser(conversation.Id, "1", 2, Now);
        db.AddRange(retry, AgentExecution.Queue(conversation, retry, clientRequestId, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task EnumsAreStoredAsSaUpperCaseValues()
    {
        var (db, scope, conversation) = await ArrangeConversationAsync();
        await using var _ = scope;
        var ct = TestContext.Current.CancellationToken;
        var message = Message.CreateUser(conversation.Id, "1", 1, Now);
        db.AddRange(message, AgentExecution.Queue(conversation, message, Guid.NewGuid(), Now));
        await db.SaveChangesAsync(ct);

        var status = await db.Database
            .SqlQuery<string>($"SELECT status AS Value FROM vibemaker.agent_executions WHERE conversation_id = {conversation.Id}")
            .SingleAsync(ct);
        Assert.Equal("QUEUED", status);
    }

    [Fact]
    public async Task UserUpsert_IsKeyedByIssuerAndSubject()
    {
        _ = factory.Server;
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var ct = TestContext.Current.CancellationToken;

        var first = await users.UpsertOnLoginAsync(new ExternalIdentity("urn:test", "sub-1", "Alice", "alice", null, null), UserRole.User, ct);
        var renamed = await users.UpsertOnLoginAsync(new ExternalIdentity("urn:test", "sub-1", "Alice Chen", "alice.chen", null, null), UserRole.Admin, ct);

        Assert.Equal(first.Id, renamed.Id);
        Assert.Equal("alice.chen", renamed.AccountName);
        Assert.Equal(UserRole.User, renamed.Role); // 既有使用者的角色不被登入覆寫
    }
}
