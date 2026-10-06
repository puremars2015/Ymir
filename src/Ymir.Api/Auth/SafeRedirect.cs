namespace Ymir.Api.Auth;

/// <summary>登入後的導回位址只接受站內相對路徑，避免 open redirect（SA §12）。</summary>
public static class SafeRedirect
{
    public static string LocalPathOrRoot(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Length > 2048)
        {
            return "/";
        }

        // 必須以單一 "/" 開頭；"//host"、"/\host" 會被瀏覽器當成其他網站。不允許控制字元。
        var isLocal = returnUrl[0] == '/'
            && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            && !returnUrl.Any(char.IsControl);
        return isLocal ? returnUrl : "/";
    }
}
