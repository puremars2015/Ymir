import { AssistantText } from './assistant-text';

describe('AssistantText', () => {
  const pipe = new AssistantText();

  it('hides multiple reasoning blocks in saved replies and retains the answer', () => {
    expect(pipe.transform('<think>private</think>\n<think>more</think>\n已完成。')).toBe(
      '已完成。',
    );
    expect(pipe.transform('前文<THINKING>private</THINKING>後文')).toBe('前文後文');
  });

  it('does not flash reasoning or partial tags at any streaming boundary', () => {
    const text = '<think>private reasoning</think>回答';
    for (let length = 1; length <= text.length; length++) {
      const visible = pipe.transform(text.slice(0, length), true);
      expect(visible).not.toMatch(/private|reasoning|[<>]|think/);
    }
    expect(pipe.transform(text, true)).toBe('回答');
  });

  it('hides an unfinished reasoning block after visible text', () => {
    expect(pipe.transform('回答<think>private')).toBe('回答');
    expect(pipe.transform('回答<thi', true)).toBe('回答');
  });

  it('keeps ordinary text and HTML examples', () => {
    expect(pipe.transform('請使用 <div>內容</div>，1 < 2。')).toBe(
      '請使用 <div>內容</div>，1 < 2。',
    );
    expect(pipe.transform('範例 <')).toBe('範例 <');
  });
});
