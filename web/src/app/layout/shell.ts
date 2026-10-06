import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { describeApiError } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { NavigationStore } from '../core/navigation/navigation.store';
import { parseActiveRoute } from './active-route';

/**
 * 登入後的主畫面（ChatGPT 式版面）：左側欄（新對話、專案、聊天、使用者），右側為對話內容。
 * 窄螢幕時側欄改為抽屜。
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly store = inject(NavigationStore);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly projectInput = viewChild<ElementRef<HTMLInputElement>>('projectInput');

  protected readonly drawerOpen = signal(false);
  protected readonly creatingProject = signal(false);
  protected readonly projectName = signal('');
  protected readonly error = signal<string | null>(null);
  private readonly expanded = signal<ReadonlySet<string>>(new Set());

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
    this.store.refresh().subscribe({ error: (e: unknown) => this.error.set(describeApiError(e)) });
    // 抽屜模式下，切換頁面後自動收起
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.drawerOpen.set(false));
  }

  protected isExpanded(projectId: string): boolean {
    return this.expanded().has(projectId) || this.activeProjectId() === projectId;
  }

  protected toggle(projectId: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(projectId)) {
        next.delete(projectId);
      } else {
        next.add(projectId);
      }
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

  protected logout(): void {
    this.auth.logout().subscribe(() => {
      this.store.clear();
      void this.router.navigate(['/login']);
    });
  }
}
