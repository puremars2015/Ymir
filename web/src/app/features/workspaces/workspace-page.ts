import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Conversation, Workspace } from '../../core/api/api-types';

/** 單一 Workspace：對話列表與建立。 */
@Component({
  selector: 'app-workspace-page',
  imports: [FormsModule, RouterLink],
  template: `
    <nav class="breadcrumb"><a routerLink="/">Workspaces</a> / {{ workspace()?.name }}</nav>
    <h1>{{ workspace()?.name }}</h1>
    <form class="inline-form" (ngSubmit)="create()">
      <input
        name="title"
        [ngModel]="title()"
        (ngModelChange)="title.set($event)"
        placeholder="新對話標題"
        aria-label="對話標題"
      />
      <button type="submit" [disabled]="!title().trim() || busy()">新對話</button>
    </form>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }
    <ul class="list">
      @for (conversation of conversations(); track conversation.id) {
        <li>
          <a [routerLink]="['/conversations', conversation.id]">{{ conversation.title }}</a>
        </li>
      } @empty {
        <li class="muted">還沒有對話。</li>
      }
    </ul>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspacePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly workspaceId = input.required<string>();
  protected readonly workspace = signal<Workspace | null>(null);
  protected readonly conversations = signal<Conversation[]>([]);
  protected readonly title = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const onError = (error: unknown) => this.error.set(describeApiError(error));
    this.api
      .getWorkspace(this.workspaceId())
      .subscribe({ next: (w) => this.workspace.set(w), error: onError });
    this.api
      .listConversations(this.workspaceId())
      .subscribe({ next: (c) => this.conversations.set(c), error: onError });
  }

  protected create(): void {
    this.busy.set(true);
    this.api.createConversation(this.workspaceId(), this.title().trim()).subscribe({
      next: (conversation) => void this.router.navigate(['/conversations', conversation.id]),
      error: (error: unknown) => {
        this.error.set(describeApiError(error));
        this.busy.set(false);
      },
    });
  }
}
