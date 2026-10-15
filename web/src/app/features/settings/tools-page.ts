import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MyExtensionsCard } from './my-extensions-card';
import { PersonalMcpCard } from './personal-mcp-card';

/** 個人設定「MCP 與工具」分頁：個人 MCP 註冊（ADR-0028）與管理員開放的能力摘要（ADR-0012 A.6）。 */
@Component({
  selector: 'app-tools-page',
  imports: [MyExtensionsCard, PersonalMcpCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stack">
      <app-personal-mcp-card />
      <app-my-extensions-card />
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .stack {
      gap: 1.5rem;
    }
  `,
})
export class ToolsPage {}
