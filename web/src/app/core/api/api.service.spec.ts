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

  it('sends the selected model and thinking depth with the same message', () => {
    api.sendMessage('c1', 'think', 'reasoning-model', null, [], 'request-1', 'high').subscribe();
    const request = http.expectOne('/api/conversations/c1/messages');
    expect(request.request.body.modelId).toBe('reasoning-model');
    expect(request.request.body.thinkingLevel).toBe('high');
    expect(request.request.body.clientRequestId).toBe('request-1');
    request.flush({});
  });

  it('uploads an attachment as the raw request body with its file name', () => {
    const file = new File(['png-bytes'], '截圖 1.png', { type: 'image/png' });
    api.uploadAttachment('c1', file).subscribe();
    api.sendMessage('c1', 'look', null, null, ['a1']).subscribe();

    const upload = http.expectOne((r) => r.url === '/api/conversations/c1/attachments');
    expect(upload.request.method).toBe('POST');
    expect(upload.request.body).toBe(file);
    expect(upload.request.params.get('fileName')).toBe('截圖 1.png');
    expect(upload.request.headers.get('Content-Type')).toBe('application/octet-stream');
    const send = http.expectOne('/api/conversations/c1/messages');
    expect(send.request.body.attachmentIds).toEqual(['a1']);
    upload.flush({});
    send.flush({});
  });

  it('filters conversations by project, or lists all without one', () => {
    api.listConversations('p1').subscribe();
    api.listConversations().subscribe();
    const [filtered, all] = http.match((r) => r.url === '/api/conversations');
    expect(filtered.request.params.get('projectId')).toBe('p1');
    expect(all.request.params.has('projectId')).toBe(false);
    filtered.flush([]);
    all.flush([]);
  });

  it('creates an ungrouped conversation with a null projectId', () => {
    api.createConversation(null, 'hi').subscribe();
    const request = http.expectOne('/api/conversations');
    expect(request.request.body).toEqual({ projectId: null, title: 'hi' });
    request.flush({});
  });

  it('describes problem details errors using detail', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: { detail: '這個對話還有執行中的工作', code: 'EXECUTION_CONFLICT' },
    });
    expect(describeApiError(error)).toBe('這個對話還有執行中的工作');
  });
});
