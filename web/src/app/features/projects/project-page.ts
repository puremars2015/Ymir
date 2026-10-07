import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  OnInit,
  signal,
  untracked,
} from '@angular/core';
import { ComposerSubmission } from '../../core/make/make-command';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Project } from '../../core/api/api-types';
import {
  isSystemPromptTooLong,
  resolveModel,
  SYSTEM_PROMPT_MAX_LENGTH,
} from '../../core/models/model-selection';
import { ModelStore } from '../../core/models/model.store';
import { ChatStarter } from '../../core/navigation/chat-starter.service';
import { NavigationStore } from '../../core/navigation/navigation.store';
import { Composer } from '../../shared/composer';
import { ModelPicker } from '../../shared/model-picker';

/**
 * 專案頁（ADR-0007）：專案是使用者執行環境內的一個檔案群組，同一專案的對話共用檔案。
 * 在這裡輸入即在此專案開新對話；「專案設定」可改名稱與專案專用的 system prompt。
 */
@Component({
  selector: 'app-project-page',
  imports: [Composer, ModelPicker, RouterLink, DatePipe, FormsModule],
  templateUrl: './project-page.html',
  styleUrl: './project-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly store = inject(NavigationStore);
  private readonly starter = inject(ChatStarter);
  protected readonly modelStore = inject(ModelStore);
  protected readonly maxLength = SYSTEM_PROMPT_MAX_LENGTH;

  readonly projectId = input.required<string>();
  protected readonly project = signal<Project | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly nameDraft = signal('');
  protected readonly promptDraft = signal('');
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly settingsError = signal<string | null>(null);
  protected readonly promptTooLong = computed(() => isSystemPromptTooLong(this.promptDraft()));
  protected readonly dirty = computed(() => {
    const p = this.project();
    return (
      !!p &&
      (this.nameDraft().trim() !== p.name || this.promptDraft().trim() !== (p.systemPrompt ?? ''))
    );
  });

  protected readonly selectedModel = computed(() =>
    resolveModel(this.modelStore.models(), null, this.modelStore.preferred()),
  );

  protected readonly conversations = computed(
    () =>
      this.store.groups().projects.find((g) => g.project.id === this.projectId())?.conversations ??
      [],
  );

  constructor() {
    // 從側邊欄切換專案時 router 會重用此元件，因此以 projectId 的變化重新載入。
    effect(() => {
      const id = this.projectId();
      untracked(() => this.load(id));
    });
  }

  ngOnInit(): void {
    this.modelStore.load();
  }

  protected start(project: Project, submission: ComposerSubmission): void {
    this.busy.set(true);
    this.error.set(null);
    this.starter
      .start(
        project.id,
        submission.content,
        this.selectedModel(),
        submission.makeTopicId,
        submission.files,
      )
      .subscribe({
        error: (e: unknown) => {
          this.error.set(describeApiError(e));
          this.busy.set(false);
        },
      });
  }

  protected saveSettings(project: Project): void {
    this.saving.set(true);
    this.saved.set(false);
    this.settingsError.set(null);
    this.api
      .updateProject(project.id, {
        name: this.nameDraft().trim() !== project.name ? this.nameDraft() : undefined,
        systemPrompt: this.promptDraft(),
      })
      .subscribe({
        next: (updated) => {
          this.applyProject(updated);
          this.store.replaceProject(updated);
          this.saving.set(false);
          this.saved.set(true);
        },
        error: (e: unknown) => {
          this.settingsError.set(describeApiError(e));
          this.saving.set(false);
        },
      });
  }

  private load(projectId: string): void {
    this.project.set(null);
    this.error.set(null);
    this.saved.set(false);
    this.settingsError.set(null);
    this.api.getProject(projectId).subscribe({
      next: (project) => this.applyProject(project),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  private applyProject(project: Project): void {
    this.project.set(project);
    this.nameDraft.set(project.name);
    this.promptDraft.set(project.systemPrompt ?? '');
  }
}
