export const HELP_COMMAND = '/help';

/** 只在輸入完整指令或其前綴時提示，不攔截一般訊息。 */
export function showsHelpHint(text: string): boolean {
  const value = text.trimStart().toLowerCase();
  return value.startsWith('/') && !/\s/.test(value) && HELP_COMMAND.startsWith(value);
}
