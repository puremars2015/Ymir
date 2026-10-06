import { computed, inject, Injectable, signal } from '@angular/core';
import { forkJoin, Observable, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Conversation, Project } from '../api/api-types';
import { groupConversations } from './navigation';
import {
  renameConversationIn,
  renameProjectIn,
  withoutConversation,
  withoutProject,
} from './navigation-edits';

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

  /** 專案改名或更新設定後同步側邊欄。 */
  replaceProject(project: Project): void {
    this.projects.update((list) => list.map((p) => (p.id === project.id ? project : p)));
  }

  /** 改名：先更新畫面，失敗時還原。 */
  renameConversation(conversationId: string, title: string): Observable<Conversation> {
    const before = this.conversations();
    this.conversations.set(renameConversationIn(before, conversationId, title));
    return this.api.renameConversation(conversationId, title).pipe(
      tap({
        next: (conversation) =>
          this.conversations.update((list) =>
            list.map((c) => (c.id === conversation.id ? conversation : c)),
          ),
        error: () => this.conversations.set(before),
      }),
    );
  }

  renameProject(projectId: string, name: string): Observable<Project> {
    const before = this.projects();
    this.projects.set(renameProjectIn(before, projectId, name));
    return this.api.updateProject(projectId, { name }).pipe(
      tap({
        next: (project) => this.replaceProject(project),
        error: () => this.projects.set(before),
      }),
    );
  }

  /** 「刪除」對話（封存）：先從側邊欄移除，失敗時還原。 */
  archiveConversation(conversationId: string): Observable<void> {
    const before = this.conversations();
    this.conversations.set(withoutConversation(before, conversationId));
    return this.api
      .archiveConversation(conversationId)
      .pipe(tap({ error: () => this.conversations.set(before) }));
  }

  /** 「刪除」專案（封存）：專案內的對話一起移除。 */
  archiveProject(projectId: string): Observable<void> {
    const projects = this.projects();
    const conversations = this.conversations();
    const next = withoutProject(projects, conversations, projectId);
    this.projects.set(next.projects);
    this.conversations.set(next.conversations);
    return this.api.archiveProject(projectId).pipe(
      tap({
        error: () => {
          this.projects.set(projects);
          this.conversations.set(conversations);
        },
      }),
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
