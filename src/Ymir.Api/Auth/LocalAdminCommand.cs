using Ymir.Platform.Users;

namespace Ymir.Api.Auth;

/// <summary>
/// 建立本機 Admin 帳號的管理指令（ADR-0009），用於還沒有任何 Admin 時：
/// <c>dotnet Ymir.Api.dll create-local-admin &lt;帳號&gt; [顯示名稱]</c>，密碼從 stdin 讀取（不出現在程序參數與 shell 歷史）。
/// 容器內：<c>podman exec -it ymir-api dotnet Ymir.Api.dll create-local-admin admin</c>。
/// </summary>
internal sealed record LocalAdminCommand(string Account, string? DisplayName, string[] RemainingArgs)
{
    public const string Name = "create-local-admin";

    public static LocalAdminCommand? TryParse(string[] args)
    {
        if (args.Length == 0 || args[0] != Name)
        {
            return null;
        }

        if (args.Length < 2 || args[1].StartsWith('-'))
        {
            throw new ArgumentException($"Usage: {Name} <account> [display name]");
        }

        var hasDisplayName = args.Length > 2 && !args[2].StartsWith('-');
        return new LocalAdminCommand(args[1], hasDisplayName ? args[2] : null, args[(hasDisplayName ? 3 : 2)..]);
    }

    public async Task<int> RunAsync(IServiceProvider services)
    {
        Console.Write("Password (at least 12 characters): ");
        var password = Console.ReadLine() ?? string.Empty;
        Console.Write("Confirm password: ");
        if ((Console.ReadLine() ?? string.Empty) != password)
        {
            await Console.Error.WriteLineAsync("Passwords do not match.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        var result = await accounts.CreateAsync(new CreateLocalAccount(Account, DisplayName ?? Account, null, UserRole.Admin, password), CancellationToken.None);
        if (!result.Succeeded)
        {
            await Console.Error.WriteLineAsync($"Failed: {result.Outcome}");
            return 1;
        }

        // 初始密碼：第一次登入後必須變更。
        Console.WriteLine($"Created local admin '{result.User!.AccountName}'. The password must be changed at first sign-in.");
        return 0;
    }
}
