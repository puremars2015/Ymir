import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { PersonalMcpApi, PersonalMcpInfo } from './personal-mcp-api';
import { PersonalMcpCard } from './personal-mcp-card';

describe('PersonalMcpCard', () => {
  const server: PersonalMcpInfo = {
    id: 'one',
    name: 'my-tools',
    url: 'https://tools.example.com/mcp',
    enabled: true,
    hasCredential: true,
    createdAt: '2026-10-15T00:00:00Z',
  };
  function setup(allowed = true) {
    const api = {
      list: vi.fn(() => of({ allowed, servers: [server] })),
      add: vi.fn(() => of(server)),
      setEnabled: vi.fn(() => of(server)),
      remove: vi.fn(() => of<void>(undefined)),
      test: vi.fn(() => of({ success: true, message: 'initialize 成功' })),
    };
    TestBed.configureTestingModule({ providers: [{ provide: PersonalMcpApi, useValue: api }] });
    const fixture = TestBed.createComponent(PersonalMcpCard);
    return { api, fixture, element: fixture.nativeElement as HTMLElement };
  }
  function button(element: HTMLElement, text: string) {
    return [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!;
  }
  function fill(element: HTMLElement, name: string, value: string) {
    const input = element.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }
  it('allows registering without exposing stored credentials and clears entered tokens immediately', async () => {
    const { api, fixture, element } = setup();
    await fixture.whenStable();
    expect(element.textContent).toContain('已儲存憑證（不回顯）');
    fill(element, 'mcpName', 'new-tools');
    fill(element, 'mcpUrl', 'https://new.example.com/mcp');
    fill(element, 'mcpToken', 'private-token');
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    expect(api.add).toHaveBeenCalledWith(
      'new-tools',
      'https://new.example.com/mcp',
      'private-token',
    );
    expect(element.querySelector<HTMLInputElement>('input[name="mcpToken"]')!.value).toBe('');
    expect(element.textContent).not.toContain('private-token');
  });
  it('keeps disable and remove available after capability revocation, but blocks testing and addition', async () => {
    const { api, fixture, element } = setup(false);
    await fixture.whenStable();
    expect(element.querySelector('form')).toBeNull();
    expect(button(element, '測試連線').disabled).toBe(true);
    expect(button(element, '停用').disabled).toBe(false);
    button(element, '停用').click();
    await fixture.whenStable();
    expect(api.setEnabled).toHaveBeenCalledWith('one', false);
    button(element, '移除').click();
    await fixture.whenStable();
    expect(api.remove).toHaveBeenCalledWith('one');
  });
  it('does not optimistically claim a failed toggle succeeded', async () => {
    const { api, fixture, element } = setup();
    api.setEnabled.mockReturnValue(throwError(() => new Error('failed')));
    await fixture.whenStable();
    button(element, '停用').click();
    await fixture.whenStable();
    expect(button(element, '停用')).toBeDefined();
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
  });
  it('blocks duplicate actions while a test is pending and cancels on destroy', async () => {
    const { api, fixture, element } = setup();
    const pending = new Subject<{ success: boolean; message: string }>();
    api.test.mockReturnValue(pending);
    await fixture.whenStable();
    button(element, '測試連線').click();
    await fixture.whenStable();
    expect(button(element, '移除').disabled).toBe(true);
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });
});
