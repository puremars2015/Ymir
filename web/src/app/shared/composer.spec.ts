import { TestBed } from '@angular/core/testing';
import { Composer } from './composer';

describe('Composer', () => {
  async function setup() {
    const fixture = TestBed.createComponent(Composer);
    const sent: string[] = [];
    fixture.componentInstance.submitted.subscribe((text) => sent.push(text));
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
    return { sent, type, press, textarea };
  }

  it('sends trimmed text on Enter and clears the input', async () => {
    const { sent, type, press, textarea } = await setup();
    await type('  hello  ');
    await press({});
    expect(sent).toEqual(['hello']);
    expect(textarea.value).toBe('');
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
});
