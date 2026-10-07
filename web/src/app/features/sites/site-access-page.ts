import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { siteAccessTarget } from '../../core/sites/site-rules';

/**
 * 私人網站的登入轉接頁（ADR-0016 §4）：SiteHost 把沒有網站 cookie 的瀏覽者導到這裡，
 * 這一頁以 Ymir 的登入身分取得一次性票據，再把瀏覽器導回網站兌換。平台 cookie 不會送到網站網域。
 */
@Component({
  selector: 'app-site-access-page',
  imports: [RouterLink],
  template: `
    <section class="page stack">
      <h1>開啟網站</h1>
      @if (error(); as e) {
        <p class="error" role="alert">{{ e }}</p>
        <a routerLink="/">回到 Vibe Maker</a>
      } @else {
        <p class="muted" role="status">正在確認你的存取權限…</p>
      }
    </section>
  `,
  styles: `
    .page {
      max-width: 32rem;
      margin: 4rem auto;
      padding: 1.5rem 1rem;
    }
    h1 {
      margin: 0;
      font-size: 1.25rem;
    }
    p {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteAccessPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const query = this.route.snapshot.queryParamMap;
    const target = siteAccessTarget(query.get('site'), query.get('path'));
    if (!target) {
      this.error.set('網站連結不正確。');
      return;
    }
    this.api.issueSiteTicket(target.siteId, target.path).subscribe({
      // 只導向後端回傳的 SiteHost 兌換網址（網址由後端依網站設定組成）。
      next: (ticket) => window.location.assign(ticket.redirectUrl),
      error: (e: unknown) =>
        this.error.set(
          e instanceof HttpErrorResponse && e.status === 404
            ? '找不到這個網站，可能已取消發布。'
            : describeApiError(e),
        ),
    });
  }
}
