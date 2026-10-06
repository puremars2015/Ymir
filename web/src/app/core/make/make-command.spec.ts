import {
  makeTopicContent,
  parseMakeCommand,
  showsMakeHint,
  stripMakeCommand,
} from './make-command';

describe('/make command', () => {
  it('recognizes a bare /make (case and spaces do not matter)', () => {
    expect(parseMakeCommand('/make')).toEqual({ kind: 'picker' });
    expect(parseMakeCommand('  /MAKE  ')).toEqual({ kind: 'picker' });
  });

  it('extracts the description', () => {
    expect(parseMakeCommand('/make 一個計算機')).toEqual({
      kind: 'describe',
      description: '一個計算機',
    });
    expect(parseMakeCommand('/Make\n多行\n描述 ')).toEqual({
      kind: 'describe',
      description: '多行\n描述',
    });
  });

  it('ignores other text', () => {
    for (const text of ['hello', '/maker x', '/mak', 'make x', '幫我 /make x']) {
      expect(parseMakeCommand(text)).toEqual({ kind: 'none' });
    }
  });

  it('builds the history text for a topic button', () => {
    expect(makeTopicContent('小工具架設')).toBe('/make 小工具架設');
  });

  it('shows the hint while typing a slash command', () => {
    expect(showsMakeHint('/')).toBe(true);
    expect(showsMakeHint('/ma')).toBe(true);
    expect(showsMakeHint('/make')).toBe(true);
    expect(showsMakeHint('/make ')).toBe(false);
    expect(showsMakeHint('/x')).toBe(false);
    expect(showsMakeHint('hello')).toBe(false);
    expect(showsMakeHint('')).toBe(false);
  });

  it('strips the command for conversation titles', () => {
    expect(stripMakeCommand('/make 小工具架設')).toBe('小工具架設');
    expect(stripMakeCommand('/make')).toBe('');
    expect(stripMakeCommand('一般訊息')).toBe('一般訊息');
  });
});
