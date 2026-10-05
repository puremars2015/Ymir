export interface ActiveRoute {
  conversationId: string | null;
  projectId: string | null;
}

/** 從網址取出目前開啟的對話或專案（側邊欄用來標示與自動展開）。 */
export function parseActiveRoute(url: string): ActiveRoute {
  const path = url.split(/[?#]/)[0];
  const conversation = /^\/c\/([^/]+)/.exec(path);
  const project = /^\/projects\/([^/]+)/.exec(path);
  return { conversationId: conversation?.[1] ?? null, projectId: project?.[1] ?? null };
}
