using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ymir.Api.Endpoints;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.Auth;
using Ymir.Platform.Users;

namespace Ymir.IntegrationTests.Admin;

/// <summary>Admin 使用者管理（ADR-0027）：設定顯示名稱、變更角色（含訪客）、刪除帳號。</summary>
public class UserManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rename_IsKeptAfterNextLogin()
    {
        using var admin = await factory.LoginAsync("um-rename-admin", UserRole.Admin);
        using var member = await factory.LoginAsync("um-rename-member");
        var memberId = await UserIdAsync(member);

        using var renamed = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/display-name", new RenameUserRequest("  王小明  "), Ct);
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("王小明", (await renamed.Content.ReadFromJsonAsync<AdminUserResponse>(JsonDefaults.Options, Ct))!.DisplayName);

        // 既有的 cookie 在下一個請求就換成新名稱；重新登入也不會被登入來源的名稱覆寫。
        Assert.Equal("王小明", (await MeAsync(member)).GetProperty("displayName").GetString());
        using var again = await factory.LoginAsync("um-rename-member");
        Assert.Equal("王小明", (await MeAsync(again)).GetProperty("displayName").GetString());

        using var blank = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/display-name", new RenameUserRequest("   "), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var missing = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/display-name", new RenameUserRequest("x"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Guest_CannotUseMessagingOrGroups_ButCanUseTheRest()
    {
        using var admin = await factory.LoginAsync("um-guest-admin", UserRole.Admin);
        using var member = await factory.LoginAsync("um-guest-member");
        using var guest = await factory.LoginAsync("um-guest-guest");
        var guestId = await UserIdAsync(guest);

        using var changed = await admin.PutAsJsonAsync($"/api/admin/users/{guestId}/role", new ChangeUserRoleRequest(UserRole.Guest), Ct);
        changed.EnsureSuccessStatusCode();
        Assert.Equal(UserRole.Guest, (await changed.Content.ReadFromJsonAsync<AdminUserResponse>(JsonDefaults.Options, Ct))!.Role);

        // 下一個請求起生效（不必重新登入）
        Assert.Equal("Guest", (await MeAsync(guest)).GetProperty("role").GetString());
        foreach (var path in new[] { "/api/direct-messages/contacts", "/api/chat-groups", "/api/direct-messages/events" })
        {
            using var denied = await guest.GetAsync(new Uri(path, UriKind.Relative), Ct);
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, $"{path} → {(int)denied.StatusCode}");
        }

        using var projects = await guest.GetAsync(new Uri("/api/projects", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);

        // 其他人看不到、也傳不了訊息給訪客，不能把訪客加進群組
        var contacts = await member.GetFromJsonAsync<List<DirectContactResponse>>("/api/direct-messages/contacts", JsonDefaults.Options, Ct);
        Assert.DoesNotContain(contacts!, c => c.UserId == guestId);
        using var dm = await member.PostAsJsonAsync($"/api/direct-messages/{guestId}", new SendDirectMessageRequest("hi"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, dm.StatusCode);
        using var group = await member.PostAsJsonAsync("/api/chat-groups", new CreateChatGroupRequest("含訪客", [guestId]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, group.StatusCode);

        using var restored = await admin.PutAsJsonAsync($"/api/admin/users/{guestId}/role", new ChangeUserRoleRequest(UserRole.User), Ct);
        restored.EnsureSuccessStatusCode();
        using var allowed = await guest.GetAsync(new Uri("/api/chat-groups", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_PromotesDevAccounts_ButNotSelf()
    {
        using var admin = await factory.LoginAsync("um-role-admin", UserRole.Admin);
        using var member = await factory.LoginAsync("um-role-member");
        var adminId = await UserIdAsync(admin);
        var memberId = await UserIdAsync(member);

        using var promoted = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/role", new ChangeUserRoleRequest(UserRole.Admin), Ct);
        promoted.EnsureSuccessStatusCode();
        using var adminPage = await member.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, adminPage.StatusCode);

        using var self = await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new ChangeUserRoleRequest(UserRole.User), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("CANNOT_CHANGE_OWN_ROLE", await ProblemCodeAsync(self));

        using var empty = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/role", new ChangeUserRoleRequest(null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Delete_SignsOutHidesAndFreesTheLocalAccountName()
    {
        using var admin = await factory.LoginAsync("um-delete-admin", UserRole.Admin);
        var adminId = await UserIdAsync(admin);
        var created = await CreateLocalAsync(admin, "um-delete-local");

        using var client = factory.CreateBrowserClient();
        using var login = await LocalAccountTests.PasswordLoginAsync(client, "um-delete-local", "initial-password-123");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var deleted = await admin.DeleteAsync(new Uri($"/api/admin/users/{created.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // 既有 cookie 立即失效、舊密碼不能再登入、不再出現在清單
        using var after = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        using var fresh = factory.CreateBrowserClient();
        using var relogin = await LocalAccountTests.PasswordLoginAsync(fresh, "um-delete-local", "initial-password-123");
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
        var list = await admin.GetFromJsonAsync<List<AdminUserResponse>>("/api/admin/users?search=um-delete-local", JsonDefaults.Options, Ct);
        Assert.DoesNotContain(list!, u => u.Id == created.Id);

        // 已刪除視為不存在
        using var again = await admin.DeleteAsync(new Uri($"/api/admin/users/{created.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        using var enable = await admin.PostAsync(new Uri($"/api/admin/users/{created.Id}/enable", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, enable.StatusCode);

        // 帳號名稱釋放，可以重新建立成新的使用者
        var recreated = await CreateLocalAsync(admin, "um-delete-local");
        Assert.NotEqual(created.Id, recreated.Id);

        using var self = await admin.DeleteAsync(new Uri($"/api/admin/users/{adminId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("CANNOT_DELETE_SELF", await ProblemCodeAsync(self));
    }

    private static async Task<AdminUserResponse> CreateLocalAsync(HttpClient admin, string account)
    {
        using var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateLocalUserRequest(account, account, null, UserRole.User, "initial-password-123"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminUserResponse>(JsonDefaults.Options, Ct))!;
    }

    private static async Task<JsonElement> MeAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/me", Ct);

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await MeAsync(client)).GetProperty("id").GetGuid();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
