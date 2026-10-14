namespace Ymir.Api.Auth;

/// <summary>登入方式的設定（區段 <c>Ymir:Auth</c>，ADR-0009）。</summary>
public sealed class YmirAuthOptions
{
    public const string SectionName = "Ymir:Auth";

    public OidcLoginOptions Oidc { get; set; } = new();

    public LocalAccountLoginOptions LocalAccounts { get; set; } = new();

    public GoogleLoginOptions Google { get; set; } = new();
}

/// <summary>企業帳號（Entra ID）OIDC 登入。設定 <see cref="Authority"/> 與 <see cref="ClientId"/> 才啟用。</summary>
public sealed class OidcLoginOptions
{
    /// <summary>例如 <c>https://login.microsoftonline.com/{tenant-id}/v2.0</c>。</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>只放在部署 secret（<c>deploy/api/.env</c>），不得進版控。</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Entra app role 的值；token 的 <c>roles</c> 含此值就是 Admin，否則是 User（每次登入同步）。</summary>
    public string AdminRole { get; set; } = "Ymir.Admin";

    /// <summary>
    /// 使用者唯一識別的 claim。Entra 用 <c>oid</c>（跨應用程式不變）；<c>sub</c> 是每個應用程式不同的 pairwise 值。
    /// </summary>
    public string SubjectClaim { get; set; } = "oid";

    /// <summary>
    /// 管理介面設定 Entra 時，authority 由此 host 與 Tenant ID 組成（ADR-0010）；只能在部署設定修改。
    /// Development 可設成 Fake OIDC 的 loopback http。
    /// </summary>
    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    /// <summary>登入按鈕上的名稱。</summary>
    public string DisplayName { get; set; } = "公司帳號";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>本機帳號（帳號密碼）登入；帳號由 Admin 建立。</summary>
public sealed class LocalAccountLoginOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>每個來源 IP 每分鐘可嘗試帳號密碼登入的次數（另有每個帳號連續失敗 5 次鎖定 15 分鐘）。</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;
}

/// <summary>
/// Google 帳號 SSO（OIDC，ADR-0023）。設定 <see cref="ClientId"/> 與 <see cref="ClientSecret"/> 才啟用；
/// 只能在部署設定修改（不經管理介面）。Google 帳號任何人都能申請，所以必須設定 <see cref="AllowedDomains"/>
/// 或 <see cref="AllowedEmails"/> 至少一項，否則拒絕啟動。
/// </summary>
public sealed class GoogleLoginOptions
{
    /// <summary>Google 的 OIDC issuer；只有測試與 Development 會改成 Fake OIDC 的 <c>{root}/google</c>。</summary>
    public string Authority { get; set; } = GoogleLogin.Issuer;

    public string? ClientId { get; set; }

    /// <summary>只放在部署 secret（<c>deploy/api/.env</c>），不得進版控。</summary>
    public string? ClientSecret { get; set; }

    /// <summary>允許的 Google Workspace 網域，以 id_token 的 <c>hd</c> claim 比對（個人 Gmail 沒有 <c>hd</c>）。</summary>
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>個別允許的 Google 帳號 email（例如沒有 Workspace 的外部協作者）；必須是 Google 已驗證的 email。</summary>
    public List<string> AllowedEmails { get; set; } = [];

    /// <summary>這些 email 登入時為 Admin，其他人為 User；每次登入同步（與 Entra app role 相同的語意）。</summary>
    public List<string> AdminEmails { get; set; } = [];

    /// <summary>登入按鈕上的名稱。</summary>
    public string DisplayName { get; set; } = "Google 帳號";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
