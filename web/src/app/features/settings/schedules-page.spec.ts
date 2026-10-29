import { TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { ModelStore } from '../../core/models/model.store';
import { ScheduledTask } from '../../core/api/api-types';
import { SchedulesPage } from './schedules-page';

const task: ScheduledTask = {
  id: 'task',
  conversationId: 'chat',
  kind: 'Reminder',
  prompt: '交報告',
  modelId: null,
  thinkingLevel: null,
  cronExpression: '0 9 * * 1-5',
  timeZone: 'Asia/Taipei',
  status: 'Active',
  nextRunAt: '2027-01-01T01:00:00Z',
  lastRunAt: null,
  lastResult: null,
  consecutiveFailures: 0,
  version: 'v1',
};
function setup() {
  const api = {
    listConversations: vi.fn(() => of([{ id: 'chat', title: '我的對話' }])),
    listScheduledTasks: vi.fn(() => of([task])),
    saveScheduledTask: vi.fn(() => of(task)),
    changeScheduledTask: vi.fn(() => of(task)),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ApiService, useValue: api },
      { provide: ModelStore, useValue: { load: vi.fn(), models: signal([]) } },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: new Map([['conversationId', 'chat']]) } },
      },
    ],
  });
  const fixture = TestBed.createComponent(SchedulesPage);
  const element = fixture.nativeElement as HTMLElement;
  return { api, fixture, element };
}
function button(el: HTMLElement, text: string): HTMLButtonElement {
  return [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!;
}
describe('SchedulesPage', () => {
  it('edits a recurring reminder and submits its concurrency version', async () => {
    const { api, fixture, element } = setup();
    await fixture.whenStable();
    button(element, '編輯').click();
    await fixture.whenStable();
    const prompt = element.querySelector<HTMLTextAreaElement>('textarea')!;
    prompt.value = '更新提醒';
    prompt.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    expect(api.saveScheduledTask).toHaveBeenCalledWith(
      expect.objectContaining({
        conversationId: 'chat',
        prompt: '更新提醒',
        cronExpression: '0 9 * * 1-5',
        timeZone: 'Asia/Taipei',
        kind: 'Reminder',
      }),
      task,
    );
    expect(element.textContent).toContain('排程已儲存');
  });
  it('reports a rejected pause and preserves server state', async () => {
    const { api, fixture, element } = setup();
    api.changeScheduledTask.mockReturnValue(throwError(() => new Error('conflict')));
    await fixture.whenStable();
    button(element, '暫停').click();
    await fixture.whenStable();
    expect(api.changeScheduledTask).toHaveBeenCalledWith(task, 'pause');
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
    expect(element.textContent).toContain('啟用');
  });
});
