/**
 * 小工具在 iframe 內開啟的網址（ADR-0029）：只接受 http / https 的絕對網址，而且不能是 Ymir 自己的來源，
 * 避免任何情況下把 Agent 產生的頁面放在 Ymir 網域上執行。不符合時回傳 null。
 */
export function toolFrameUrl(
  redirectUrl: string | null | undefined,
  platformOrigin: string,
): string | null {
  if (!redirectUrl) return null;
  let url: URL;
  try {
    url = new URL(redirectUrl);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
  if (url.origin === platformOrigin) return null;
  return url.toString();
}
