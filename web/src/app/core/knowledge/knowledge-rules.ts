import {
  KnowledgeAnswer,
  KnowledgeDocument,
  KnowledgeDocumentStatus,
  ModelOption,
} from '../api/api-types';

/** 知識庫文件狀態的顯示文字（ADR-0014 §7）。 */
export function knowledgeStatusLabel(status: KnowledgeDocumentStatus): string {
  switch (status) {
    case 'Pending':
      return '等待處理';
    case 'Indexing':
      return '建立索引中';
    case 'Ready':
      return '可查詢';
    case 'Failed':
      return '失敗';
    default:
      return '已移除';
  }
}

/** 還有文件在處理中：前端定期重新讀取。 */
export function hasPendingDocuments(documents: readonly KnowledgeDocument[]): boolean {
  return documents.some((d) => d.status === 'Pending' || d.status === 'Indexing');
}

/** 上傳前的檢查：副檔名與大小；回傳錯誤訊息或 null。 */
export function validateKnowledgeFile(
  file: { name: string; size: number },
  supportedExtensions: readonly string[],
  maxBytes: number,
): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!supportedExtensions.includes(extension)) {
    return `「${file.name}」的格式不支援（可用：${supportedExtensions.join('、')}）。`;
  }

  if (file.size > maxBytes) {
    return `「${file.name}」超過 ${Math.floor(maxBytes / 1024 / 1024)} MB。`;
  }

  return null;
}

/** 管理員允許用於知識庫問答的模型（ADR-0014 §8）。 */
export function knowledgeModels(models: readonly ModelOption[]): ModelOption[] {
  return models.filter((m) => m.allowKnowledgeBase);
}

/** 問答結果的說明文字：資料不足、模型未獲允許時的提示；有回答時為 null。 */
export function knowledgeAnswerNotice(answer: KnowledgeAnswer): string | null {
  if (answer.insufficientData) {
    return '文件中沒有足夠的資料回答這個問題。';
  }

  if (!answer.modelAllowed) {
    return '這個模型沒有被允許用於知識庫問答，以下只列出相關段落。';
  }

  return null;
}
