import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Workspace } from '../../core/api/api-types';

/** Workspace 列表與建立。每個 Workspace 有自己的隔離執行環境與檔案（SA §6.1）。 */
@Component({
  selector: 'app-workspaces-page',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>我的 Workspace</h1>
    <form class="inline-form" (ngSubmit)="create()">
      <input
        name="name"
        [ngModel]="name()"
        (ngModelChange)="name.set($event)"
        placeholder="新 Workspace 名稱，例如：Todo App"
        aria-label="Workspace 名稱"
      />
      <button type="submit" [disabled]="!name().trim() || busy()">建立</button>
    </form>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }
    <ul class="list">
      @for (workspace of workspaces(); track workspace.id) {
        <li>
          <a [routerLink]="['/workspaces', workspace.id]">{{ workspace.name }}</a>
        </li>
      } @empty {
        <li class="muted">還沒有 Workspace，先建立一個。</li>
      }
    </ul>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspacesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly workspaces = signal<Workspace[]>([]);
  protected readonly name = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.listWorkspaces().subscribe({
      next: (workspaces) => this.workspaces.set(workspaces),
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }

  protected create(): void {
    this.busy.set(true);
    this.api.createWorkspace(this.name().trim()).subscribe({
      next: (workspace) => void this.router.navigate(['/workspaces', workspace.id]),
      error: (error: unknown) => {
        this.error.set(describeApiError(error));
        this.busy.set(false);
      },
    });
  }
}
