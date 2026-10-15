namespace Ymir.Platform.Messaging;

public enum ChatGroupStatus
{
    Active,
    Archived,
}

public enum ChatGroupRole
{
    Owner,
    Member,
}

public enum GroupSenderKind
{
    User,
    Bot,
}

/// <summary>Bot 回覆的狀態（ADR-0026 §2）：送出 <c>@bot</c> 時先建立 Pending，執行結束後更新為回答或失敗摘要。</summary>
public enum BotReplyStatus
{
    Pending,
    Completed,
    Failed,
}

/// <summary>
/// 群組聊天（ADR-0026 §1，延伸 ADR-0023）。屬於共用核心（schema <c>platform</c>）。
/// 成員資格一律在伺服器端以 <see cref="ChatGroupMember"/> 判斷；非成員、已封存的群組對外一律視為不存在。
/// </summary>
public sealed class ChatGroup
{
    public const int NameMaxLength = 100;

    /// <summary>成員上限（含擁有者）。</summary>
    public const int MaxMembers = 50;

    private ChatGroup()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid OwnerId { get; private set; }

    public ChatGroupStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChatGroup Create(Guid ownerId, string name, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = NormalizeName(name) ?? throw new ArgumentException("群組名稱不可空白或超過長度上限。", nameof(name)),
        OwnerId = ownerId,
        Status = ChatGroupStatus.Active,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>去掉前後空白；空白或超過 <see cref="NameMaxLength"/> 時回傳 null。</summary>
    public static string? NormalizeName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > NameMaxLength ? null : text;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = NormalizeName(name) ?? throw new ArgumentException("群組名稱不可空白或超過長度上限。", nameof(name));
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = ChatGroupStatus.Archived;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

public sealed class ChatGroupMember
{
    private ChatGroupMember()
    {
    }

    public Guid GroupId { get; private set; }

    public Guid UserId { get; private set; }

    public ChatGroupRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>讀到哪個時間點；之後別人送出的訊息（含 Bot）算未讀。</summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    public static ChatGroupMember Create(Guid groupId, Guid userId, ChatGroupRole role, DateTimeOffset now) => new()
    {
        GroupId = groupId,
        UserId = userId,
        Role = role,
        JoinedAt = now,
        // 加入前的訊息不算未讀。
        LastReadAt = now,
    };

    public void MarkRead(DateTimeOffset now)
    {
        if (LastReadAt is null || now > LastReadAt)
        {
            LastReadAt = now;
        }
    }
}

/// <summary>群組訊息：成員送出的訊息，或 <c>@bot</c> 的回覆（<see cref="GroupSenderKind.Bot"/>）。</summary>
public sealed class ChatGroupMessage
{
    private ChatGroupMessage()
    {
    }

    public Guid Id { get; private set; }

    public Guid GroupId { get; private set; }

    public GroupSenderKind SenderKind { get; private set; }

    /// <summary>成員訊息的寄件者；Bot 訊息為 null。</summary>
    public Guid? SenderId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Bot 訊息：對應的 Vibe Maker execution（跨模組只存 id，ADR-0001）。</summary>
    public Guid? ExecutionId { get; private set; }

    /// <summary>Bot 訊息：呼叫的成員。</summary>
    public Guid? InvokedById { get; private set; }

    public BotReplyStatus? BotStatus { get; private set; }

    public static ChatGroupMessage CreateUser(Guid groupId, Guid senderId, string body, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        GroupId = groupId,
        SenderKind = GroupSenderKind.User,
        SenderId = senderId,
        Body = DirectMessage.NormalizeBody(body) ?? throw new ArgumentException("訊息不可空白或超過長度上限。", nameof(body)),
        CreatedAt = now,
    };

    public static ChatGroupMessage CreateBotPending(Guid groupId, Guid invokedById, Guid executionId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        GroupId = groupId,
        SenderKind = GroupSenderKind.Bot,
        Body = string.Empty,
        CreatedAt = now,
        ExecutionId = executionId,
        InvokedById = invokedById,
        BotStatus = BotReplyStatus.Pending,
    };

    /// <summary>Bot 的回答過長時截斷（群組是對話，不是長文管道）。</summary>
    public void CompleteBot(string body, bool failed)
    {
        if (SenderKind != GroupSenderKind.Bot || BotStatus != BotReplyStatus.Pending)
        {
            return;
        }

        var text = body.Trim();
        Body = text.Length > BotBodyMaxLength ? text[..BotBodyMaxLength] + "…" : text;
        BotStatus = failed ? BotReplyStatus.Failed : BotReplyStatus.Completed;
    }

    /// <summary>Bot 回答的保存上限。</summary>
    public const int BotBodyMaxLength = 16000;
}
