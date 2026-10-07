import { ArtifactGroup } from '../api/api-types';
export const artifactDownloadUrl = (
  conversationId: string,
  executionId: string,
  path: string,
): string =>
  `/api/conversations/${conversationId}/artifacts/${executionId}/download?path=${encodeURIComponent(path)}`;
export const artifactArchiveUrl = (conversationId: string, executionId: string): string =>
  `/api/conversations/${conversationId}/artifacts/${executionId}/archive`;
/** 多檔只提供完整成果 ZIP，單檔直接下載；沒有成果不回傳下載入口。 */
export function artifactDelivery(
  group: ArtifactGroup,
  conversationId: string,
): { url: string; label: string } | null {
  if (group.files.length === 0) return null;
  if (group.files.length > 1)
    return {
      url: artifactArchiveUrl(conversationId, group.executionId),
      label: `下載完整成果（ZIP，${group.files.length} 個檔案）`,
    };
  const file = group.files[0];
  return {
    url: artifactDownloadUrl(conversationId, group.executionId, file.path),
    label: `下載 ${file.path.split('/').pop()}`,
  };
}
