import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { MyExtensions } from '../../core/api/api-types';

/**
 * 個人設定：我的 Agent 擴充能力（ADR-0012 A.6）。顯示管理員是否開放、以及我建立過的 skill 與 MCP server 名稱。
 * 建立方式是直接請 Agent 幫忙（平台會提供說明 skill），這裡只供檢視。
 */
@Component({
  selector: 'app-my-extensions-card',
  template: `
    <section class="stack extensions">
      <h2>Agent 擴充能力</h2>
      @if (data(); as d) {
        <ul class="plain">
          <li>
            自建 skill：<strong>{{ d.skillsAllowed ? '已開放' : '未開放' }}</strong>
            @if (d.inventoryAvailable && d.skills.length > 0) {
              <span class="muted">（{{ d.skills.join('、') }}）</span>
            }
          </li>
          <li>
            OneDrive 連結：<strong>{{ d.oneDriveAllowed ? '已開放' : '未開放' }}</strong>
          </li>
          <li>
            對外連線：<strong>{{ d.internetAllowed ? '允許' : '只能連到平台的模型與服務' }}</strong>
          </li>
          <li>
            自建 MCP server：<strong>{{ d.mcpAllowed ? '已開放' : '未開放' }}</strong>
            @if (d.inventoryAvailable && d.mcpServers.length > 0) {
              <span class="muted">（{{ d.mcpServers.join('、') }}）</span>
            }
          </li>
        </ul>
        <p class="muted hint">
          @if (d.skillsAllowed || d.mcpAllowed) {
            在對話中請 Agent 幫你建立或修改，例如「幫我建立一個整理會議紀錄的
            skill」；從下一則訊息開始生效。
          } @else {
            需要時請洽管理員開放。
          }
          @if (
            (d.skills.length > 0 || d.mcpServers.length > 0) && !(d.skillsAllowed && d.mcpAllowed)
          ) {
            未開放的項目會保留，但不會被載入。
          }
        </p>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      } @else {
        <p class="muted">載入中…</p>
      }
    </section>
  `,
  styles: `
    .extensions {
      margin-top: 2rem;
    }
    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
    .plain {
      margin: 0;
      padding-left: 1.25rem;
    }
    .hint {
      margin: 0;
      font-size: 0.8125rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyExtensionsCard implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly data = signal<MyExtensions | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getMyExtensions().subscribe({
      next: (d) => this.data.set(d),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }
}
