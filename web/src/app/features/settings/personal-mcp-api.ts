import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { components } from '../../core/api/schema';

export type PersonalMcpInfo = components['schemas']['PersonalMcpInfo'];
export type PersonalMcpList = components['schemas']['PersonalMcpList'];
export type PersonalMcpTestResult = components['schemas']['PersonalMcpTestResult'];

@Injectable({ providedIn: 'root' })
export class PersonalMcpApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/connections/mcp';
  list() {
    return this.http.get<PersonalMcpList>(this.base);
  }
  add(name: string, url: string, bearerToken: string | null) {
    return this.http.post<PersonalMcpInfo>(this.base, { name, url, bearerToken });
  }
  setEnabled(id: string, enabled: boolean) {
    return this.http.patch<PersonalMcpInfo>(`${this.base}/${id}`, { enabled });
  }
  remove(id: string) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
  test(id: string) {
    return this.http.post<PersonalMcpTestResult>(`${this.base}/${id}/test`, {});
  }
}
