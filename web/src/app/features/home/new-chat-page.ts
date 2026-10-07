import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { ComposerSubmission } from '../../core/make/make-command';
import { describeApiError } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { resolveModel } from '../../core/models/model-selection';
import { ModelStore } from '../../core/models/model.store';
import { ChatStarter } from '../../core/navigation/chat-starter.service';
import { Composer } from '../../shared/composer';
import { ModelPicker } from '../../shared/model-picker';

/** 登入後的首頁：像 ChatGPT 一樣直接輸入就開始一個未分組的新對話。 */
@Component({
  selector: 'app-new-chat-page',
  host: { class: 'chat-surface' },
  imports: [Composer, ModelPicker],
  template: `
    <section class="welcome">
      <div class="welcome-copy">
        <h1>{{ greeting() }}</h1>
        <p class="muted">描述你想做的小工具或網站，Agent 會在你的工作環境中幫你完成。</p>
      </div>
      <div class="welcome-composer">
        <app-model-picker
          class="picker"
          [models]="modelStore.models()"
          [selected]="selectedModel()"
          [depth]="modelStore.depth()"
          (depthChanged)="modelStore.rememberDepth($event)"
          (changed)="modelStore.remember($event)"
        />
        <app-composer
          class="composer"
          placeholder="有什麼可以幫忙的？例如：幫我建立一個 Todo List 網站"
          [disabled]="busy()"
          (submitted)="start($event)"
        />
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }
      </div>
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      display: grid;
      place-items: center;
      padding: 1rem;
    }
    .welcome {
      width: 100%;
      max-width: 46rem;
      text-align: center;
      margin-bottom: 10vh;
    }
    h1 {
      font-size: 1.75rem;
      font-weight: 600;
      margin-bottom: 0.5rem;
    }
    .picker {
      display: block;
      margin-top: 1.5rem;
      text-align: left;
    }
    .composer {
      display: block;
      margin-top: 0.5rem;
      text-align: left;
    }
    @media (max-width: 768px) {
      :host {
        display: flex;
        align-items: stretch;
        min-height: 0;
        padding: 0 0.75rem max(0.5rem, env(safe-area-inset-bottom));
      }
      .welcome {
        display: flex;
        flex-direction: column;
        margin-bottom: 0;
        min-height: 0;
      }
      .welcome-copy {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        align-content: center;
      }
      .welcome-composer {
        flex-shrink: 0;
        text-align: left;
      }
      .picker {
        margin-top: 0.5rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewChatPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly starter = inject(ChatStarter);
  protected readonly modelStore = inject(ModelStore);
  protected readonly selectedModel = computed(() =>
    resolveModel(this.modelStore.models(), null, this.modelStore.preferred()),
  );

  ngOnInit(): void {
    this.modelStore.load();
  }

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected greeting(): string {
    const name = this.auth.user()?.displayName;
    return name ? `${name}，今天想做什麼？` : '今天想做什麼？';
  }

  protected start(submission: ComposerSubmission): void {
    this.busy.set(true);
    this.error.set(null);
    this.starter
      .start(
        null,
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
}
