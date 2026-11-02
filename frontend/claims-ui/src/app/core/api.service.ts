import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import {
  ClaimDetail, ClaimListQuery, ClaimSummary, Exposure, NotificationList, PagedResult,
  ReferenceData, SubmitClaimRequest, User, Workload
} from './models';

/** Thin typed client over the REST API. All paths are relative and proxied to the backend in development. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  readonly referenceData$ = this.http.get<ReferenceData>(`${this.base}/reference-data`).pipe(shareReplay(1));

  demoUsers(): Observable<User[]> {
    return this.http.get<User[]>(`${this.base}/demo-users`);
  }

  listClaims(query: ClaimListQuery = {}): Observable<PagedResult<ClaimSummary>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<ClaimSummary>>(`${this.base}/claims`, { params });
  }

  getClaim(id: string): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`${this.base}/claims/${id}`);
  }

  submitClaim(request: SubmitClaimRequest): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims`, request);
  }

  assign(id: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/assign`, {});
  }

  requestInformation(id: string, question: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/information-requests`, { question });
  }

  provideInformation(id: string, requestId: string, response: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/information-requests/${requestId}/response`, { response });
  }

  approve(id: string, approvedAmount: number, note: string | null): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/approve`, { approvedAmount, note });
  }

  reject(id: string, reason: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/reject`, { reason });
  }

  settle(id: string, settledAmount: number | null): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`${this.base}/claims/${id}/settle`, { settledAmount });
  }

  workload(): Observable<Workload> {
    return this.http.get<Workload>(`${this.base}/dashboard/workload`);
  }

  exposure(): Observable<Exposure> {
    return this.http.get<Exposure>(`${this.base}/dashboard/exposure`);
  }

  notifications(): Observable<NotificationList> {
    return this.http.get<NotificationList>(`${this.base}/notifications`);
  }

  markNotificationRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/notifications/${id}/read`, {});
  }

  markAllNotificationsRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/notifications/read-all`, {});
  }
}

/** Turns an API error (RFC 7807 problem details or validation problem) into a readable message. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the API. Is the backend running on http://localhost:5080?';
    const body = error.error;
    if (body?.errors) {
      const first = Object.values(body.errors as Record<string, string[]>)[0];
      if (first?.length) return first[0];
    }
    if (body?.detail) return body.detail;
    if (body?.title) return body.title;
    return `Request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}
