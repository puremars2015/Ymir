/** `/make` 指令（與後端 `MakePromptBuilder.ParseDescription` 規則一致）。 */
export const MAKE_COMMAND = '/make';

export type MakeCommand =
  | { kind: 'none' }
  /** 只有 `/make`：顯示主題按鈕，不送出。 */
  | { kind: 'picker' }
  /** `/make <描述>`：送出，由後端請 Agent 判斷適合的主題。 */
  | { kind: 'describe'; description: string };

export function parseMakeCommand(text: string): MakeCommand {
  const trimmed = text.trim();
  if (!trimmed.toLowerCase().startsWith(MAKE_COMMAND)) {
    return { kind: 'none' };
  }
  const rest = trimmed.slice(MAKE_COMMAND.length);
  // `/maker` 之類不算：/make 後面必須是結尾或空白
  if (rest.length > 0 && !/^\s/.test(rest)) {
    return { kind: 'none' };
  }
  const description = rest.trim();
  return description ? { kind: 'describe', description } : { kind: 'picker' };
}

/** 點主題按鈕時，對話紀錄顯示的文字。 */
export function makeTopicContent(topicName: string): string {
  return `${MAKE_COMMAND} ${topicName}`;
}

/** 正在輸入斜線指令（例如 `/`、`/ma`）時顯示 `/make` 提示。 */
export function showsMakeHint(text: string): boolean {
  const value = text.trimStart().toLowerCase();
  return value.startsWith('/') && !/\s/.test(value) && MAKE_COMMAND.startsWith(value);
}

/** 新對話標題：`/make <描述>` 用描述，只有 `/make` 時回傳 null（由呼叫端決定）。 */
export function stripMakeCommand(text: string): string {
  const command = parseMakeCommand(text);
  if (command.kind === 'describe') {
    return command.description;
  }
  return command.kind === 'picker' ? '' : text;
}

export interface ComposerSubmission {
  content: string;
  makeTopicId: string | null;
  /** 使用者附加的檔案；送出時才上傳（見 core/attachments）。 */
  files: File[];
}
