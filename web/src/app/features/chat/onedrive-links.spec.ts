import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { OneDriveLinkedItem, OneDriveStatus } from '../../core/api/api-types';
import { OneDriveLinks } from './onedrive-links';

describe('OneDriveLinks', () => {
  const link: OneDriveLinkedItem = {
    id: 'link',
    name: 'budget.txt',
    localPath: 'onedrive/123-budget.txt',
    isFolder: false,
    readOnly: false,
    state: 'Pending',
    conflictCount: 0,
    lastDownloadedAt: null,
    lastError: null,
    writeBackEnabled: false,
  };
  const connected: OneDriveStatus = {
    allowed: true,
    available: true,
    state: 'Connected',
    account: 'fixture@example.test',
    rootPath: null,
    connectedAt: null,
    lastError: null,
  };

  function setup(status = connected) {
    const api = {
      getOneDriveStatus: vi.fn(() => of(status)),
      getOneDriveLinks: vi.fn(() => of<OneDriveLinkedItem[]>([link])),
      addOneDriveLink: vi.fn(() => of(link)),
      removeOneDriveLink: vi.fn(() => of<void>(undefined)),
      setOneDriveLinkReadOnly: vi.fn(() => of(link)),
      browseOneDrive: vi.fn(() =>
        of({
          folderId: 'root',
          parentItemId: null,
          items: [
            { id: 'cloud-item', name: 'new.txt', isFolder: false, size: 5, lastModified: '' },
          ],
        }),
      ),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: ApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(OneDriveLinks);
    fixture.componentRef.setInput('conversationId', 'first');
    return { api, fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('allows selecting a file without a legacy sync root and states writeback is disabled', async () => {
    const { api, fixture, element } = setup();
    await fixture.whenStable();
    expect(element.textContent).toContain('尚未自動寫回雲端');
    [...element.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('從 OneDrive 加入'))!
      .click();
    await fixture.whenStable();
    element.querySelector<HTMLButtonElement>('button[aria-label="連結 new.txt"]')!.click();
    await fixture.whenStable();
    expect(api.addOneDriveLink).toHaveBeenCalledWith('first', 'cloud-item', false);
  });

  it('keeps unlink available when capability is revoked', async () => {
    const { api, fixture, element } = setup({ ...connected, allowed: false });
    await fixture.whenStable();
    expect(element.textContent).toContain('budget.txt');
    expect(element.textContent).not.toContain('從 OneDrive 加入');
    [...element.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('取消連結'))!
      .click();
    await fixture.whenStable();
    expect(api.removeOneDriveLink).toHaveBeenCalledWith('first', 'link');
  });

  it('does not display an unsuccessful read-only toggle as applied', async () => {
    const { api, fixture, element } = setup();
    api.setOneDriveLinkReadOnly.mockReturnValue(throwError(() => new Error('failed')));
    await fixture.whenStable();
    const checkbox = element.querySelector<HTMLInputElement>(
      'input[aria-label="budget.txt 唯讀"]',
    )!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(checkbox.checked).toBe(false);
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('cancels old-scope mutations and clears old names when the conversation changes', async () => {
    const { api, fixture, element } = setup();
    const pending = new Subject<void>();
    api.removeOneDriveLink.mockReturnValue(pending);
    await fixture.whenStable();
    [...element.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('取消連結'))!
      .click();
    expect(pending.observed).toBe(true);
    api.getOneDriveLinks.mockReturnValue(of([]));
    fixture.componentRef.setInput('conversationId', 'second');
    await fixture.whenStable();
    expect(pending.observed).toBe(false);
    expect(element.textContent).not.toContain('budget.txt');
    expect(api.getOneDriveLinks).toHaveBeenLastCalledWith('second');
  });
});
