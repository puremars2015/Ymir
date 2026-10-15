namespace Ymir.VibeMaker.Domain;

/// <summary>個人註冊的 HTTP MCP；憑證欄位只保存密文（ADR-0028）。</summary>
public sealed class PersonalMcpServer
{
    private PersonalMcpServer() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string? ProtectedToken { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PersonalMcpServer Create(Guid userId, string name, string url, string? protectedToken, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = name,
        Url = url,
        ProtectedToken = protectedToken,
        Enabled = true,
        CreatedAt = now,
    };

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
