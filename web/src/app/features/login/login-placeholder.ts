import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** 登入頁佔位。Sprint 2 實作 BFF + OIDC：按鈕導向 `/api/auth/login`，由後端完成企業 SSO（ADR-0002）。 */
@Component({
  selector: 'app-login-placeholder',
  imports: [RouterLink],
  template: `
    <section class="login">
      <h2>企業帳號登入</h2>
      <p>Sprint 2 將接上企業 SSO（OIDC / Entra ID）。登入由後端處理，前端不保存任何 token。</p>
      <a routerLink="/">先前往開發用聊天頁</a>
    </section>
  `,
  styles: `
    .login {
      max-width: 28rem;
      margin: 4rem auto;
      text-align: center;
      color: var(--text-muted);
    }
    h2 {
      color: var(--text);
    }
    a {
      color: var(--accent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPlaceholder {}
