namespace Ymir.Maintenance;

/// <summary>
/// 跨程序的生命週期鎖（ADR-0025 §4）：runtime host 建立 / 啟動 / 刪除使用者的 Agent container 時持有該使用者的鎖，
/// 維護服務刪除 container 前以不等待的方式取同一把鎖，取不到就略過；部署腳本以 <c>flock deploy.lock</c> 持有部署鎖，
/// 維護服務刪除 image 期間也持有它，避免刪掉正在部署的 image。
/// <para>
/// 實作是 advisory <c>flock(LOCK_EX)</c>：.NET 在 Unix 以 <see cref="FileShare.None"/> 開檔時會取 exclusive flock，
/// 與 <c>flock(1)</c> 指令互通。鎖檔只用來上鎖、沒有內容；以唯讀開啟，所以不同帳號（runtime host 的 ymir、維護服務的 root）
/// 建立的鎖檔都能共用（建立時權限 0644）。
/// </para>
/// </summary>
public static class LifecycleLock
{
    public const string DeployLockFileName = "deploy.lock";

    public static string UserLockFileName(Guid userId) => $"user-{userId:N}.lock";

    /// <summary>不等待：鎖被其他程序持有時回傳 null。目錄不存在或沒有權限時拋出 <see cref="LifecycleLockUnavailableException"/>。</summary>
    public static IDisposable? TryAcquire(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (fileName.Contains('/', StringComparison.Ordinal) || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Lock file name must not contain path separators.", nameof(fileName));
        }

        if (!Directory.Exists(directory))
        {
            throw new LifecycleLockUnavailableException("Lifecycle lock directory does not exist.");
        }

        var path = Path.Combine(directory, fileName);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (FileNotFoundException)
            {
                if (TryCreate(path) is { } created)
                {
                    return created;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new LifecycleLockUnavailableException("Lifecycle lock file is not accessible.", ex);
            }
            catch (IOException)
            {
                // 其他程序持有 flock（EWOULDBLOCK）。
                return null;
            }
        }

        return null;
    }

    /// <summary>等待直到取得鎖（每 200 ms 重試一次）。</summary>
    public static async Task<IDisposable> AcquireAsync(string directory, string fileName, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryAcquire(directory, fileName) is { } handle)
            {
                return handle;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
    }

    private static FileStream? TryCreate(string path)
    {
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            }

            return new FileStream(path, options);
        }
        catch (IOException)
        {
            // 其他程序剛好同時建立：回到開啟既有檔案的流程。
            return null;
        }
    }
}

public sealed class LifecycleLockUnavailableException : Exception
{
    public LifecycleLockUnavailableException()
    {
    }

    public LifecycleLockUnavailableException(string message)
        : base(message)
    {
    }

    public LifecycleLockUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
