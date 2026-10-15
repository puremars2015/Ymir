import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { OneDriveBrowserPage } from '../../core/api/api-types';
import { OneDriveBrowser } from './onedrive-browser';

describe('OneDriveBrowser', () => {
  const page: OneDriveBrowserPage = {
    folderId: 'root',
    parentItemId: null,
    items: [
      { id: 'folder', name: '文件', isFolder: true, size: 0, lastModified: '2026-10-15T00:00:00Z' },
    ],
  };

  function setup() {
    const api = { browseOneDrive: vi.fn(() => of(page)), searchOneDrive: vi.fn(() => of([])) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
    return api;
  }

  it('browses root and opens a folder without downloading files', async () => {
    const api = setup();
    const fixture = TestBed.createComponent(OneDriveBrowser);
    await fixture.whenStable();
    expect(api.browseOneDrive).toHaveBeenCalledWith(undefined);
    const element = fixture.nativeElement as HTMLElement;
    const folder = [...element.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('文件'),
    )!;
    folder.click();
    expect(api.browseOneDrive).toHaveBeenLastCalledWith('folder');
    expect(element.textContent).toContain('瀏覽不會將檔案交給 Agent');
  });

  it('shows an error without exposing a stale file list', async () => {
    const api = setup();
    api.browseOneDrive.mockReturnValue(throwError(() => new Error('unavailable')));
    const fixture = TestBed.createComponent(OneDriveBrowser);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
    expect(element.textContent).not.toContain('📁 文件');
  });

  it('cancels in-flight requests when the browser closes', async () => {
    const api = setup();
    const pending = new Subject<OneDriveBrowserPage>();
    api.browseOneDrive.mockReturnValue(pending);
    const fixture = TestBed.createComponent(OneDriveBrowser);
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });
});
