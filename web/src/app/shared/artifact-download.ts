import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ArtifactGroup } from '../core/api/api-types';
import { artifactDelivery } from '../core/files/artifact-files';
@Component({
  selector: 'app-artifact-download',
  template: `@if (delivery(); as item) {
    <div class="delivery">
      <a [href]="item.url" download>{{ item.label }} ↓</a>
    </div>
  }`,
  styles: `
    .delivery {
      margin-top: 0.75rem;
    }
    a {
      display: inline-block;
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.65rem;
      color: var(--text);
      text-decoration: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArtifactDownload {
  readonly group = input.required<ArtifactGroup>();
  readonly conversationId = input.required<string>();
  protected readonly delivery = computed(() =>
    artifactDelivery(this.group(), this.conversationId()),
  );
}
