import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, Observable } from 'rxjs';
import { describeApiError } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { NavigationStore } from '../core/navigation/navigation.store';
import { normalizeTitle } from '../core/navigation/navigation-edits';
import { parseActiveRoute } from './active-route';
import { ThemeSwitcher } from '../shared/theme-switcher';

type ItemKind = 'conversation' | 'project';

/**
 * 登入後的主畫面（ChatGPT 式版面）：左側欄（新對話、專案、聊天、使用者），右側為對話內容。
 * 窄螢幕時側欄改為抽屜。
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    FormsModule,
    NgTemplateOutlet,
    ThemeSwitcher,
  ],
  host: {
    '(document:click)': 'closeMenu($event)',
    '(document:keydown.escape)': 'closeOverlays()',
    '(document:keydown.tab)': 'trapDrawerFocus($event)',
  },
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly store = inject(NavigationStore);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly projectInput = viewChild<ElementRef<HTMLInputElement>>('projectInput');
  private readonly sidebarToggle = viewChild<ElementRef<HTMLButtonElement>>('sidebarToggle');
  private readonly destroyRef = inject(DestroyRef);
  private readonly mobileQuery = globalThis.window?.matchMedia?.('(max-width: 768px)');
  protected readonly mobile = signal(this.mobileQuery?.matches ?? false);
  protected readonly sidebarCollapsed = signal(readSidebarCollapsed());
  protected readonly sidebarVisible = computed(() =>
    this.mobile() ? this.drawerOpen() : !this.sidebarCollapsed(),
  );

  protected readonly drawerOpen = signal(false);
  protected readonly creatingProject = signal(false);
  protected readonly projectName = signal('');
  protected readonly error = signal<string | null>(null);
  private readonly expanded = signal<ReadonlyMap<string, boolean>>(new Map());
  /** 開啟中的「⋯」選單與 inline 改名的項目，格式 `kind:id`。 */
  protected readonly menu = signal<string | null>(null);
  private readonly editing = signal<{ kind: ItemKind; id: string; original: string } | null>(null);
  protected readonly renameText = signal('');

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** 管理區有多個分頁，任何一頁都標示「管理」。 */
  protected readonly adminActive = computed(() => /^\/admin(\/|\?|$)/.test(this.url()));

  /** 目前所在的專案：專案頁本身，或所開啟對話的專案（自動展開）。 */
  private readonly activeProjectId = computed(() => {
    const route = parseActiveRoute(this.url());
    if (route.projectId) {
      return route.projectId;
    }
    const conversation = this.store.conversations().find((c) => c.id === route.conversationId);
    return conversation?.projectId ?? null;
  });

  ngOnInit(): void {
    const onResize = () => {
      this.mobile.set(this.mobileQuery?.matches ?? false);
      this.drawerOpen.set(false);
      this.menu.set(null);
    };
    this.mobileQuery?.addEventListener('change', onResize);
    this.destroyRef.onDestroy(() => this.mobileQuery?.removeEventListener('change', onResize));
    this.store.refresh().subscribe({ error: (e: unknown) => this.error.set(describeApiError(e)) });
    // 抽屜模式下，切換頁面後自動收起
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.drawerOpen.set(false));
  }

  protected toggleSidebar(): void {
    this.menu.set(null);
    if (this.mobile()) {
      if (this.drawerOpen()) {
        this.closeDrawer();
      } else {
        this.drawerOpen.set(true);
        afterNextRender(
          () => this.host.nativeElement.querySelector<HTMLAnchorElement>('.new-chat')?.focus(),
          { injector: this.injector },
        );
      }
      return;
    }
    this.sidebarCollapsed.update((collapsed) => !collapsed);
    try {
      localStorage.setItem('ymir.sidebarCollapsed', String(this.sidebarCollapsed()));
    } catch {
      /* 儲存不可用時仍可正常收合。 */
    }
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
    this.sidebarToggle()?.nativeElement.focus();
  }

  protected closeOverlays(): void {
    if (this.menu()) this.menu.set(null);
    else if (this.drawerOpen()) this.closeDrawer();
  }

  protected trapDrawerFocus(event: Event): void {
    if (!(event instanceof KeyboardEvent)) return;
    if (!this.mobile() || !this.drawerOpen()) return;
    const elements = [
      ...this.host.nativeElement.querySelectorAll<HTMLElement>(
        '.sidebar a[href], .sidebar button:not(:disabled), .sidebar input:not(:disabled)',
      ),
    ].filter((element) => element.getClientRects().length > 0);
    const target = event.shiftKey ? elements.at(-1) : elements[0];
    const boundary = event.shiftKey ? elements[0] : elements.at(-1);
    if (
      target &&
      (document.activeElement === boundary ||
        !this.host.nativeElement.querySelector('.sidebar')?.contains(document.activeElement))
    ) {
      event.preventDefault();
      target.focus();
    }
  }

  protected isExpanded(projectId: string): boolean {
    return this.expanded().get(projectId) ?? this.activeProjectId() === projectId;
  }

  protected toggle(projectId: string): void {
    const open = !this.isExpanded(projectId);
    this.expanded.update((set) => {
      const next = new Map(set);
      next.set(projectId, open);
      return next;
    });
  }

  protected startCreatingProject(): void {
    this.projectName.set('');
    this.creatingProject.set(true);
    afterNextRender(() => this.projectInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected createProject(): void {
    const name = this.projectName().trim();
    if (!name) {
      this.creatingProject.set(false);
      return;
    }
    this.store.createProject(name).subscribe({
      next: (project) => {
        this.creatingProject.set(false);
        void this.router.navigate(['/projects', project.id]);
      },
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  /** 點選單以外的地方就關閉選單。 */
  protected closeMenu(event: Event): void {
    if (!(event.target instanceof Element && event.target.closest('.menu'))) {
      this.menu.set(null);
    }
  }

  protected isMenuOpen(kind: ItemKind, id: string): boolean {
    return this.menu() === `${kind}:${id}`;
  }

  protected toggleMenu(kind: ItemKind, id: string, event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    const key = `${kind}:${id}`;
    this.menu.update((current) => (current === key ? null : key));
  }

  protected isEditing(kind: ItemKind, id: string): boolean {
    const editing = this.editing();
    return editing?.kind === kind && editing.id === id;
  }

  protected startRename(kind: ItemKind, id: string, name: string): void {
    this.menu.set(null);
    this.renameText.set(name);
    this.editing.set({ kind, id, original: name });
    afterNextRender(
      () => {
        const input = this.host.nativeElement.querySelector<HTMLInputElement>('input.rename');
        input?.focus();
        input?.select();
      },
      { injector: this.injector },
    );
  }

  protected cancelRename(): void {
    this.editing.set(null);
  }

  /** Enter 或離開輸入框時存檔；名稱沒變或空白就當作取消。 */
  protected commitRename(): void {
    const editing = this.editing();
    if (!editing) {
      return;
    }
    this.editing.set(null);
    const title = normalizeTitle(this.renameText());
    if (!title || title === editing.original) {
      return;
    }
    this.error.set(null);
    const request: Observable<unknown> =
      editing.kind === 'conversation'
        ? this.store.renameConversation(editing.id, title)
        : this.store.renameProject(editing.id, title);
    request.subscribe({ error: (e: unknown) => this.error.set(describeApiError(e)) });
  }

  /** 「刪除」是封存（資料與執行環境內的檔案都保留）；刪除正在看的項目時導回上一層。 */
  protected remove(kind: ItemKind, id: string, name: string): void {
    this.menu.set(null);
    const route = parseActiveRoute(this.url());
    if (kind === 'conversation') {
      if (!confirm(`刪除對話「${name}」？`)) {
        return;
      }
      const projectId = this.store.conversations().find((c) => c.id === id)?.projectId ?? null;
      this.error.set(null);
      this.store.archiveConversation(id).subscribe({
        next: () => {
          if (route.conversationId === id) {
            void this.router.navigate(projectId ? ['/projects', projectId] : ['/']);
          }
        },
        error: (e: unknown) => this.error.set(describeApiError(e)),
      });
      return;
    }

    const count = this.store.conversations().filter((c) => c.projectId === id).length;
    const detail = count > 0 ? `專案內的 ${count} 個對話會一起移除，` : '';
    if (!confirm(`刪除專案「${name}」？${detail}檔案仍保留在執行環境中。`)) {
      return;
    }
    const openConversation = this.store.conversations().find((c) => c.id === route.conversationId);
    this.error.set(null);
    this.store.archiveProject(id).subscribe({
      next: () => {
        if (route.projectId === id || openConversation?.projectId === id) {
          void this.router.navigate(['/']);
        }
      },
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  protected logout(): void {
    this.auth.logout().subscribe(() => {
      this.store.clear();
      void this.router.navigate(['/login']);
    });
  }
}

function readSidebarCollapsed(): boolean {
  try {
    return localStorage.getItem('ymir.sidebarCollapsed') === 'true';
  } catch {
    return false;
  }
}
