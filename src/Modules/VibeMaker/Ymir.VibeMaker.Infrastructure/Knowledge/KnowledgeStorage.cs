using System.Security.Cryptography;
using Ymir.VibeMaker.Application.Knowledge;

namespace Ymir.VibeMaker.Infrastructure.Knowledge;

/// <summary><c>Ymir:Knowledge</c>：知識庫 volume 的根目錄（API 自己的持久 volume，不是 Agent workspace，ADR-0014 §2）。</summary>
public sealed class KnowledgeStorageOptions
{
    public const string SectionName = "Ymir:Knowledge";

    public string? Root { get; set; }
}

/// <summary>知識庫的路徑只由 user id 與 project id 推導（不接受外部路徑）。</summary>
internal sealed record KnowledgePaths(string Root)
{
    public string ProjectDirectory(Guid userId, Guid projectId) =>
        Path.Combine(Root, userId.ToString("N"), projectId.ToString("N"));

    public string DocumentPath(Guid userId, Guid projectId, Guid documentId) =>
        Path.Combine(ProjectDirectory(userId, projectId), "documents", documentId.ToString("N"));

    public string IndexPath(Guid userId, Guid projectId) =>
        Path.Combine(ProjectDirectory(userId, projectId), "index.sqlite");
}

/// <summary>原始文件存在知識庫 volume（ADR-0014 §2）：先寫暫存檔、確認大小沒有超過上限才改名。</summary>
internal sealed class FileSystemKnowledgeStore(KnowledgePaths paths) : IKnowledgeFileStore
{
    public async Task<(long Size, string Sha256)?> SaveAsync(Guid userId, Guid projectId, Guid documentId, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var target = paths.DocumentPath(userId, projectId, documentId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".uploading";
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (file.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                    {
                        return null;
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temp, target, overwrite: true);
            return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public Stream OpenRead(Guid userId, Guid projectId, Guid documentId) =>
        File.OpenRead(paths.DocumentPath(userId, projectId, documentId));

    public void Delete(Guid userId, Guid projectId, Guid documentId) =>
        File.Delete(paths.DocumentPath(userId, projectId, documentId));
}
