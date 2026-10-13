/**
 * 管理介面的模型名稱（ADR-0020 補充）：管理員要知道模型由哪個 provider 提供，顯示為「名稱 (provider)」。
 * 使用者的模型選單直接用 `/api/models` 的名稱：後端不回傳 provider，只在同名模型時才把 provider 併入名稱。
 */
export function withProvider(displayName: string, provider?: string | null): string {
  const name = provider?.trim();
  return name ? `${displayName} (${name})` : displayName;
}
