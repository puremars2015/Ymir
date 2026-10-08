import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MakeTopic } from '../core/api/api-types';
import { ComposerSubmission } from '../core/make/make-command';
import { MakeTopicStore } from '../core/make/make-topic.store';
import { Composer } from './composer';

describe('Composer', () => {
  const topics: MakeTopic[] = [
    { id: 't1', name: '小工具架設', description: '單頁小工具', sortOrder: 10 },
    { id: 't2', name: '網站系統架設', description: null, sortOrder: 20 },
    { id: 't3', name: '建立簡報', description: '可編輯的 PPTX', sortOrder: 30 },
    { id: 't4', name: '建立公告 Word', description: '公司公告模板', sortOrder: 40 },
  ];

  async function setup() {
    const refresh = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MakeTopicStore,
          useValue: {
            topics: signal(topics),
            loading: signal(false),
            error: signal(null),
            refresh,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(Composer);
    const submissions: ComposerSubmission[] = [];
    fixture.componentInstance.submitted.subscribe((s) => submissions.push(s));
    const sent: string[] = [];
    fixture.componentInstance.submitted.subscribe((s) => sent.push(s.content));
    await fixture.whenStable();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    const type = async (value: string) => {
      textarea.value = value;
      textarea.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const press = async (init: KeyboardEventInit) => {
      textarea.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, ...init }),
      );
      await fixture.whenStable();
    };
    const host = fixture.nativeElement as HTMLElement;
    return { sent, submissions, type, press, textarea, host, refresh, fixture };
  }

  it('sends trimmed text on Enter and clears the input', async () => {
    const { sent, type, press, textarea } = await setup();
    await type('  hello  ');
    await press({});
    expect(sent).toEqual(['hello']);
    expect(textarea.value).toBe('');
  });

  it('offers both commands for slash and filters help prefixes', async () => {
    const { type, host, fixture, textarea, press, submissions } = await setup();
    await type('/');
    expect(host.querySelector('.help-hint')).not.toBeNull();
    expect(host.querySelector('.make-hint')).not.toBeNull();
    await type('/he');
    expect(host.querySelector('.make-hint')).toBeNull();
    host.querySelector<HTMLButtonElement>('.help-hint')!.click();
    await fixture.whenStable();
    expect(textarea.value).toBe('/help');
    await press({});
    expect(submissions).toEqual([{ content: '/help', makeTopicId: null, files: [] }]);
    expect(host.querySelector('.make-picker')).toBeNull();
  });

  it('does not send on Shift+Enter or while an IME is composing', async () => {
    const { sent, type, press } = await setup();
    await type('你好');
    await press({ shiftKey: true });
    await press({ isComposing: true });
    expect(sent).toEqual([]);
  });

  it('ignores blank input', async () => {
    const { sent, type, press } = await setup();
    await type('   ');
    await press({});
    expect(sent).toEqual([]);
  });

  it('shows topic buttons instead of sending a bare /make', async () => {
    const { sent, type, press, textarea, host, refresh } = await setup();
    await type(' /MAKE ');
    await press({});
    expect(sent).toEqual([]);
    expect(textarea.value).toBe('');
    expect(refresh).toHaveBeenCalled();
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('.topic'));
    expect(buttons.map((b) => b.querySelector('strong')?.textContent)).toEqual([
      '小工具架設',
      '網站系統架設',
      '建立簡報',
      '建立公告 Word',
    ]);
  });

  it('sends the chosen topic with its id', async () => {
    const { submissions, type, press, host, fixture } = await setup();
    await type('/make');
    await press({});
    host.querySelector<HTMLButtonElement>('.topic')!.click();
    await fixture.whenStable();
    expect(submissions).toEqual([{ content: '/make 小工具架設', makeTopicId: 't1', files: [] }]);
    expect(host.querySelector('.make-picker')).toBeNull();
  });

  it('sends /make with a description as a normal message', async () => {
    const { submissions, type, press } = await setup();
    await type('/make 一個計算機');
    await press({});
    expect(submissions).toEqual([{ content: '/make 一個計算機', makeTopicId: null, files: [] }]);
  });

  it.each([
    ['建立簡報', 't3'],
    ['建立公告 Word', 't4'],
  ])('sends the document topic %s using its configured id', async (name, id) => {
    const { submissions, type, press, host, fixture } = await setup();
    await type('/make');
    await press({});
    const button = Array.from(host.querySelectorAll<HTMLButtonElement>('.topic')).find(
      (candidate) => candidate.querySelector('strong')?.textContent === name,
    );
    button!.click();
    await fixture.whenStable();
    expect(submissions).toEqual([{ content: `/make ${name}`, makeTopicId: id, files: [] }]);
  });

  it('hints /make while typing a slash command and fills it on click', async () => {
    const { type, host, textarea, fixture } = await setup();
    await type('/m');
    const hint = host.querySelector<HTMLButtonElement>('.make-hint');
    expect(hint).not.toBeNull();
    hint!.click();
    await fixture.whenStable();
    expect(textarea.value).toBe('/make ');
  });

  it('attaches chosen files, can remove them, and sends files without text', async () => {
    const { submissions, host, fixture } = await setup();
    const input = host.querySelector<HTMLInputElement>('input[type=file]')!;
    const a = new File(['a'], 'a.png', { type: 'image/png' });
    const b = new File(['bb'], 'b.mp4', { type: 'video/mp4' });
    Object.defineProperty(input, 'files', { value: [a, b], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(
      Array.from(host.querySelectorAll('.attachment .name')).map((e) => e.textContent),
    ).toEqual(['a.png', 'b.mp4']);

    host.querySelector<HTMLButtonElement>('.attachment .remove')!.click();
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();

    expect(submissions).toEqual([
      { content: '請看看我附加的檔案。', makeTopicId: null, files: [b] },
    ]);
    expect(host.querySelector('.attachment')).toBeNull();
  });

  it('reports files that cannot be attached', async () => {
    const { host, fixture } = await setup();
    const input = host.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', {
      value: [new File([], 'empty.txt')],
      configurable: true,
    });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(host.querySelector('.notice')?.textContent).toContain('empty.txt：空檔案');
    expect(host.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
  });
});
