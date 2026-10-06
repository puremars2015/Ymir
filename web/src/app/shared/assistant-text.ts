import { Pipe, PipeTransform } from '@angular/core';

/** 模型可能把思考內容包在文字內；歷史與串流使用相同的顯示過濾。 */
@Pipe({ name: 'assistantText' })
export class AssistantText implements PipeTransform {
  transform(text: string, streaming = false): string {
    let visible = text.replace(/<(think|thinking)\s*>[\s\S]*?(?:<\/\1\s*>|$)/gi, '');
    // SSE 可能將開頭標籤拆成多個片段，尚未收齊前也不顯示。
    if (streaming) {
      visible = visible.replace(/<[^<>]*$/, (partial) =>
        ['<think>', '<thinking>'].some((tag) => tag.startsWith(partial.trimEnd().toLowerCase()))
          ? ''
          : partial,
      );
    }
    return visible.trim();
  }
}
