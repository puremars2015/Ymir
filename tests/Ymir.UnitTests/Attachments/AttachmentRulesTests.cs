using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Application.Files;
using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Attachments;

public class AttachmentRulesTests
{
    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\報告 v2.docx", "報告 v2.docx")]
    [InlineData(".env", "env")]
    [InlineData("a<b>:c?.txt", "a_b__c_.txt")]
    [InlineData("tab\tname.txt", "tab_name.txt")]
    [InlineData("  spaced.md  ", "spaced.md")]
    [InlineData("", null)]
    [InlineData("...", null)]
    [InlineData("dir/", null)]
    [InlineData("node_modules", null)]
    [InlineData(null, null)]
    public void SanitizeFileName_KeepsOnlyASafeLastSegment(string? input, string? expected) =>
        Assert.Equal(expected, AttachmentRules.SanitizeFileName(input));

    [Fact]
    public void SanitizeFileName_TruncatesLongNames_KeepingTheExtension()
    {
        var name = AttachmentRules.SanitizeFileName(new string('a', 300) + ".png")!;

        Assert.Equal(120, name.Length);
        Assert.EndsWith(".png", name, StringComparison.Ordinal);
    }

    [Fact]
    public void StoragePath_IsUnderUploads_AndSafe()
    {
        var id = Guid.Parse("0a1b2c3d-0000-0000-0000-0000a1b2c3d4");

        var path = AttachmentRules.StoragePath(id, "照片 1.png");

        Assert.Equal("uploads/a1b2c3d4-照片 1.png", path);
        Assert.True(WorkspacePathRules.IsSafeRelativePath(path));
    }

    [Fact]
    public void StoragePath_DiffersForIdsCreatedAtTheSameTime()
    {
        var now = DateTimeOffset.UnixEpoch;
        var paths = Enumerable.Range(0, 50).Select(_ => AttachmentRules.StoragePath(Guid.CreateVersion7(now), "a.png")).ToHashSet();

        Assert.Equal(50, paths.Count);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "x.bin", "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "x", "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "x.gif", "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "x.webp", "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x68 }, "fake.png", "application/octet-stream")]
    [InlineData(new byte[] { 0x3C, 0x73 }, "icon.svg", "image/svg+xml")]
    [InlineData(new byte[] { 0, 0, 0, 24 }, "clip.MP4", "video/mp4")]
    [InlineData(new byte[] { 1 }, "data.unknown", "application/octet-stream")]
    public void DetectContentType_TrustsMagicBytesForImages(byte[] header, string fileName, string expected) =>
        Assert.Equal(expected, AttachmentRules.DetectContentType(header, fileName));

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("video/mp4", false)]
    public void IsInlineImage_OnlyRasterFormats(string contentType, bool expected) =>
        Assert.Equal(expected, AttachmentRules.IsInlineImage(contentType));

    [Fact]
    public void AppendToPrompt_ListsAttachmentPaths()
    {
        var now = DateTimeOffset.UnixEpoch;
        var attachments = new[]
        {
            MessageAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a.png", "uploads/1-a.png", "image/png", 2048, now),
            MessageAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "b.mp4", "uploads/2-b.mp4", "video/mp4", 3 * 1024 * 1024, now),
        };

        var prompt = AttachmentRules.AppendToPrompt("做成網站", attachments);

        Assert.StartsWith("做成網站\n\n---\n", prompt, StringComparison.Ordinal);
        Assert.Contains("- uploads/1-a.png（image/png，2 KB）", prompt, StringComparison.Ordinal);
        Assert.Contains("- uploads/2-b.mp4（video/mp4，3 MB）", prompt, StringComparison.Ordinal);
        Assert.Equal("原文", AttachmentRules.AppendToPrompt("原文", []));
    }

    [Fact]
    public void Attachment_CanOnlyBeAttachedOnce()
    {
        var attachment = MessageAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a.txt", "uploads/1-a.txt", "text/plain", 1, DateTimeOffset.UnixEpoch);
        attachment.AttachTo(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => attachment.AttachTo(Guid.NewGuid()));
    }
}
