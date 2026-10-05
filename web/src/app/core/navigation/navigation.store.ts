import { computed, inject, Injectable, signal } from '@angular/core';
import { forkJoin, Observable, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Conversation, Project } from '../api/api-types';
import { groupConversations } from './navigation';

/** 側邊欄的資料（專案與對話）。建立專案 / 對話、送出訊息後更新，讓側邊欄立即反映。 */
@Injectable({ providedIn: 'root' })
export class NavigationStore {
  private readonly api = inject(ApiService);

  readonly projects = signal<Project[]>([]);
  readonly conversations = signal<Conversation[]>([]);
  readonly groups = computed(() => groupConversations(this.projects(), this.conversations()));

  refresh(): Observable<unknown> {
    return forkJoin([this.api.listProjects(), this.api.listConversations()]).pipe(
      tap(([projects, conversations]) => {
        this.projects.set(projects);
        this.conversations.set(conversations);
      }),
    );
  }

  createProject(name: string): Observable<Project> {
    return this.api
      .createProject(name)
      .pipe(tap((project) => this.projects.update((list) => [project, ...list])));
  }

  createConversation(projectId: string | null, title: string): Observable<Conversation> {
    return this.api
      .createConversation(projectId, title)
      .pipe(tap((conversation) => this.upsert(conversation)));
  }

  /** 送出訊息後把對話移到最上面（後端也會更新 updatedAt）。 */
  touch(conversationId: string): void {
    const now = new Date().toISOString();
    this.conversations.update((list) =>
      list.map((c) => (c.id === conversationId ? { ...c, updatedAt: now } : c)),
    );
  }

  project(projectId: string | null | undefined): Project | undefined {
    return projectId ? this.projects().find((p) => p.id === projectId) : undefined;
  }

  clear(): void {
    this.projects.set([]);
    this.conversations.set([]);
  }

  private upsert(conversation: Conversation): void {
    this.conversations.update((list) => [
      conversation,
      ...list.filter((c) => c.id !== conversation.id),
    ]);
  }
}
