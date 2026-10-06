using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Auth;

/// <summary>本機帳號（帳號密碼登入，ADR-0009）：Admin 建立、第一次登入必須改密碼、鎖定、停用、重設。</summary>
public class LocalAccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string InitialPassword = "initial-password-123";
    private const string NewPassword = "brand-new-password-456";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<AdminUserResponse> CreateLocalUserAsync(string account, UserRole role = UserRole.User)
    {
        using var admin = await factory.LoginAsync("local-admin", UserRole.Admin);
        using var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateLocalUserRequest(account, $"{account} 顯示名稱", $"{account}@example.com", role, InitialPassword), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminUserResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    internal static async Task<HttpResponseMessage> PasswordLoginAsync(HttpClient client, string account, string password)
    {
        // 與前端相同：先 GET 一個 /api 端點取得 XSRF token。
        await client.GetAsync(new Uri("/api/auth/providers", UriKind.Relative), TestContext.Current.CancellationToken);
        return await client.PostAsJsonAsync("/api/auth/password-login", new PasswordLoginRequest(account, password), TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString();

    [Fact]
    public async Task FirstLogin_RequiresPasswordChange_ThenEverythingWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await CreateLocalUserAsync("Local.Dana");
        Assert.Equal("local.dana", created.AccountName);
        Assert.Equal(AuthMethod.Local, created.AuthMethod);

        using var client = factory.CreateBrowserClient();
        using var login = await PasswordLoginAsync(client, "LOCAL.DANA", InitialPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(Json, ct);
        Assert.True(me!.MustChangePassword);
        Assert.Equal(AuthMethod.Local, me.AuthMethod);

        // 改密碼前：其他 API 一律 403。
        using var blocked = await client.GetAsync(new Uri("/api/projects", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", await ProblemCodeAsync(blocked));

        using var change = await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(InitialPassword, NewPassword), ct);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        using var allowed = await client.GetAsync(new Uri("/api/projects", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var fresh = factory.CreateBrowserClient();
        using var oldPassword = await PasswordLoginAsync(fresh, "local.dana", InitialPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await PasswordLoginAsync(fresh, "local.dana", NewPassword);
        Assert.False((await newPassword.Content.ReadFromJsonAsync<MeResponse>(Json, ct))!.MustChangePassword);
    }

    [Fact]
    public async Task WrongPassword_AndUnknownAccount_LookTheSame()
    {
        await CreateLocalUserAsync("local-erin");
        using var client = factory.CreateBrowserClient();

        using var wrong = await PasswordLoginAsync(client, "local-erin", "not-the-password-000");
        using var unknown = await PasswordLoginAsync(client, "no-such-account", "not-the-password-000");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ProblemCodeAsync(wrong));
        Assert.Equal("INVALID_CREDENTIALS", await ProblemCodeAsync(unknown));
    }

    [Fact]
    public async Task FiveFailures_LockTheAccount_EvenForTheRightPassword()
    {
        await CreateLocalUserAsync("local-frank");
        using var client = factory.CreateBrowserClient();
        for (var i = 0; i < LocalAccounts.MaxFailedAttempts; i++)
        {
            using var _ = await PasswordLoginAsync(client, "local-frank", $"wrong-password-{i:000000}");
        }

        using var locked = await PasswordLoginAsync(client, "local-frank", InitialPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await ProblemCodeAsync(locked));
    }

    [Fact]
    public async Task DisabledLocalUser_CannotLogIn()
    {
        var created = await CreateLocalUserAsync("local-gina");
        using var admin = await factory.LoginAsync("local-admin", UserRole.Admin);
        using var disable = await admin.PostAsync(new Uri($"/api/admin/users/{created.Id}/disable", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        using var client = factory.CreateBrowserClient();
        using var login = await PasswordLoginAsync(client, "local-gina", InitialPassword);

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsWeakPassword_InvalidAccount_AndDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await factory.LoginAsync("local-admin", UserRole.Admin);
        await CreateLocalUserAsync("local-henry");

        using var weak = await admin.PostAsJsonAsync("/api/admin/users", new CreateLocalUserRequest("local-weak", "Weak", null, UserRole.User, "short"), ct);
        using var invalid = await admin.PostAsJsonAsync("/api/admin/users", new CreateLocalUserRequest("bad account!", "Bad", null, UserRole.User, InitialPassword), ct);
        using var duplicate = await admin.PostAsJsonAsync("/api/admin/users", new CreateLocalUserRequest("LOCAL-HENRY", "Dup", null, UserRole.User, InitialPassword), ct);

        Assert.Equal("WEAK_PASSWORD", await ProblemCodeAsync(weak));
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(invalid));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task PasswordIsStoredAsSaltedHash()
    {
        var created = await CreateLocalUserAsync("local-iris");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var credential = await db.LocalCredentials.AsNoTracking().SingleAsync(c => c.UserId == created.Id, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(InitialPassword, credential.PasswordHash, StringComparison.Ordinal);
        Assert.True(credential.PasswordHash.Length > 60);
    }

    [Fact]
    public async Task AdminReset_ForcesPasswordChange_AndOnlyAppliesToLocalAccounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await CreateLocalUserAsync("local-jack");
        using var admin = await factory.LoginAsync("local-admin", UserRole.Admin);
        var devUser = await (await factory.LoginAsync("dev-not-local")).GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        using var reset = await admin.PostAsJsonAsync($"/api/admin/users/{created.Id}/reset-password", new ResetPasswordRequest("reset-password-789"), ct);
        using var resetDev = await admin.PostAsJsonAsync($"/api/admin/users/{devUser!.Id}/reset-password", new ResetPasswordRequest("reset-password-789"), ct);
        using var client = factory.CreateBrowserClient();
        using var login = await PasswordLoginAsync(client, "local-jack", "reset-password-789");

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal("NOT_LOCAL_ACCOUNT", await ProblemCodeAsync(resetDev));
        Assert.True((await login.Content.ReadFromJsonAsync<MeResponse>(Json, ct))!.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_ForNonLocalUser_IsRejected()
    {
        using var devUser = await factory.LoginAsync("dev-change-password");

        using var response = await devUser.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest("x", NewPassword), TestContext.Current.CancellationToken);

        Assert.Equal("NOT_LOCAL_ACCOUNT", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task PasswordLogin_RequiresAntiforgeryToken()
    {
        await CreateLocalUserAsync("local-kate");
        using var client = factory.CreateBrowserClient(sendXsrfHeader: false);

        using var response = await PasswordLoginAsync(client, "local-kate", InitialPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminCannotDisableThemselves()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await factory.LoginAsync("local-self-admin", UserRole.Admin);
        var me = await admin.GetFromJsonAsync<MeResponse>("/api/me", Json, ct);

        using var response = await admin.PostAsync(new Uri($"/api/admin/users/{me!.Id}/disable", UriKind.Relative), null, ct);

        Assert.Equal("CANNOT_DISABLE_SELF", await ProblemCodeAsync(response));
    }
}

/// <summary>每個來源 IP 的帳號密碼登入次數上限。</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override void Configure(IWebHostBuilder builder) => builder.UseSetting("Ymir:Auth:LocalAccounts:LoginAttemptsPerMinute", "3");
}

public class PasswordLoginRateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task TooManyAttemptsFromOneAddress_Return429()
    {
        using var client = factory.CreateBrowserClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await LocalAccountTests.PasswordLoginAsync(client, "nobody", "whatever-password-000");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}
