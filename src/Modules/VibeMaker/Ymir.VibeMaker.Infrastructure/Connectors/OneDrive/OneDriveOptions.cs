namespace Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

/// <summary><c>Ymir:Connectors:OneDrive</c>（ADR-0013）。</summary>
public sealed class OneDriveOptions
{
    public const string SectionName = "Ymir:Connectors:OneDrive";

    /// <summary>Microsoft Graph 的位址；只有測試替身（Fake OIDC 的 <c>/graph/v1.0</c>）才需要改。</summary>
    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0");
}
