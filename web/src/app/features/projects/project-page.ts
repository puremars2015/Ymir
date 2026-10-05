import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Project } from '../../core/api/api-types';
import { ChatStarter } from '../../core/navigation/chat-starter.service';
import { NavigationStore } from '../../core/navigation/navigation.store';
import { Composer } from '../../shared/composer';

/**
 * 專案頁（ADR-0007）：專案是使用者執行環境內的一個檔案群組，同一專案的對話共用檔案。
 * 在這裡輸入即在此專案開新對話，下方列出專案內的對話。
 */
@Component({
  selector: 'app-project-page',
  imports: [Composer, RouterLink, DatePipe],
  template: `
    <section class="project">
      @if (project(); as p) {
        <h1><span aria-hidden="true">📁</span> {{ p.name }}</h1>
        <p class="muted">這個專案的對話共用同一組檔案。</p>
        <app-composer
          class="composer"
          [placeholder]="'在「' + p.name + '」開始新對話'"
          [disabled]="busy()"
          (submitted)="start(p, $event)"
        />
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }
        <h2>對話</h2>
        <ul class="conversations">
          @for (conversation of conversations(); track conversation.id) {
            <li>
              <a [routerLink]="['/c', conversation.id]">
                <span class="title">{{ conversation.title }}</span>
                <span class="muted when">{{ conversation.updatedAt | date: 'M/d HH:mm' }}</span>
              </a>
            </li>
          } @empty {
            <li class="muted empty">還沒有對話，從上面的輸入框開始。</li>
          }
        </ul>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .project {
      max-width: 46rem;
      margin: 0 auto;
    }
    h1 {
      font-size: 1.5rem;
      margin-bottom: 0.25rem;
    }
    h2 {
      font-size: 0.875rem;
      color: var(--text-muted);
      margin: 2rem 0 0.5rem;
    }
    .composer {
      display: block;
      margin-top: 1.25rem;
    }
    .conversations {
      list-style: none;
      margin: 0;
      padding: 0;
      border-top: 1px solid var(--border);
      a {
        display: flex;
        justify-content: space-between;
        gap: 1rem;
        padding: 0.875rem 0.5rem;
        border-bottom: 1px solid var(--border);
        color: var(--text);
        text-decoration: none;
        &:hover {
          background: var(--surface-muted);
        }
      }
      .title {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .when {
        flex-shrink: 0;
        font-size: 0.8125rem;
      }
    }
    .empty {
      padding: 1rem 0.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectPage {
  private readonly api = inject(ApiService);
  private readonly store = inject(NavigationStore);
  private readonly starter = inject(ChatStarter);

  readonly projectId = input.required<string>();
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** 專案資料以 API 為準（直接開網址時側邊欄可能還沒載入）。 */
  protected readonly project = toSignal(
    toObservable(this.projectId).pipe(
      switchMap((id) =>
        this.api.getProject(id).pipe(
          catchError((e: unknown) => {
            this.error.set(describeApiError(e));
            return of(null);
          }),
        ),
      ),
    ),
    { initialValue: null },
  );

  protected readonly conversations = computed(
    () =>
      this.store.groups().projects.find((g) => g.project.id === this.projectId())?.conversations ??
      [],
  );

  protected start(project: Project, prompt: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.starter.start(project.id, prompt).subscribe({
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }
}
