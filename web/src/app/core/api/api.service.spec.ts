import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService, describeApiError } from './api.service';
import { HttpErrorResponse } from '@angular/common/http';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
        ),
        provideHttpClientTesting(),
      ],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends a message with a fresh clientRequestId each time', () => {
    api.sendMessage('c1', 'hello').subscribe();
    api.sendMessage('c1', 'hello').subscribe();

    const [first, second] = http.match('/api/conversations/c1/messages');
    expect(first.request.method).toBe('POST');
    expect(first.request.body.content).toBe('hello');
    expect(first.request.body.clientRequestId).toMatch(/^[0-9a-f-]{36}$/);
    expect(first.request.body.clientRequestId).not.toBe(second.request.body.clientRequestId);
    first.flush({});
    second.flush({});
  });

  it('filters conversations by workspace', () => {
    api.listConversations('w1').subscribe();
    const request = http.expectOne((r) => r.url === '/api/conversations');
    expect(request.request.params.get('workspaceId')).toBe('w1');
    request.flush([]);
  });

  it('describes problem details errors using detail', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: { detail: '這個對話還有執行中的工作', code: 'EXECUTION_CONFLICT' },
    });
    expect(describeApiError(error)).toBe('這個對話還有執行中的工作');
  });
});
