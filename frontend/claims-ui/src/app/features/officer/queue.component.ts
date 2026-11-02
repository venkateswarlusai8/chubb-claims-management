import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, combineLatest, debounceTime, of, switchMap, tap } from 'rxjs';
import { ApiService, problemMessage } from '../../core/api.service';
import { ClaimListQuery, ClaimSummary, PagedResult } from '../../core/models';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

type View = 'unassigned' | 'mine' | 'open' | 'all';

const VIEWS: { key: View; label: string; query: ClaimListQuery }[] = [
  { key: 'unassigned', label: 'Unassigned', query: { assigned: 'unassigned', open: true, sort: 'oldest' } },
  { key: 'mine', label: 'My claims', query: { assigned: 'me', open: true, sort: 'oldest' } },
  { key: 'open', label: 'All open', query: { open: true, sort: 'oldest' } },
  { key: 'all', label: 'All claims', query: { sort: 'newest' } }
];

const PAGE_SIZE = 15;

@Component({
  selector: 'app-queue',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h1>Claims queue</h1>
        <p class="muted">Oldest first for open work. Claims older than {{ slaDays }} days are flagged.</p>
      </div>
    </div>

    <div class="toolbar">
      <div class="tabs">
        @for (v of views; track v.key) {
          <button class="tab" [class.active]="view() === v.key" (click)="setView(v.key)">{{ v.label }}</button>
        }
      </div>
      <div class="row gap">
        <select [ngModel]="market()" (ngModelChange)="market.set($event); page.set(1)">
          <option value="">All markets</option>
          @for (m of markets(); track m.code) { <option [value]="m.code">{{ m.name }}</option> }
        </select>
        <input type="search" placeholder="Search claim, claimant, policy" title="Search by claim number, claimant name or policy number" [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
      </div>
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>Claim</th><th>Claimant</th><th>Type / market</th><th>Status</th>
            <th class="num">Outstanding</th><th>Officer</th><th class="num">Age</th>
          </tr>
        </thead>
        <tbody>
          @for (c of result().items; track c.id) {
            <tr class="clickable" (click)="open(c)">
              <td><strong>{{ c.claimNumber }}</strong></td>
              <td>{{ c.claimantName }}</td>
              <td>{{ c.claimType }} · {{ c.marketCode }}</td>
              <td>
                <app-status-badge [status]="c.status" />
                @if (c.awaitingClaimant) { <span class="small muted"> awaiting claimant</span> }
              </td>
              <td class="num">{{ c.outstandingAmount | currency: c.currency : 'code' }}</td>
              <td>{{ c.assignedOfficerName ?? '—' }}</td>
              <td class="num" [class.sla-breach]="isBreach(c)">{{ c.ageDays }}d</td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">{{ loading() ? 'Loading…' : 'No claims in this view.' }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (pages() > 1) {
      <div class="pager">
        <button class="btn" [disabled]="page() === 1" (click)="page.set(page() - 1)">Previous</button>
        <span class="muted">Page {{ page() }} of {{ pages() }} · {{ result().totalCount }} claims</span>
        <button class="btn" [disabled]="page() >= pages()" (click)="page.set(page() + 1)">Next</button>
      </div>
    }
  `
})
export class QueueComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly views = VIEWS;
  protected readonly slaDays = 14;
  protected readonly view = signal<View>('unassigned');
  protected readonly market = signal('');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly result = signal<PagedResult<ClaimSummary>>({ items: [], page: 1, pageSize: PAGE_SIZE, totalCount: 0 });
  protected readonly pages = computed(() => Math.ceil(this.result().totalCount / PAGE_SIZE));
  protected readonly markets = computed(() => this.reference()?.markets ?? []);
  private readonly reference = toSignal(this.api.referenceData$);

  constructor() {
    combineLatest([toObservable(this.view), toObservable(this.market), toObservable(this.search), toObservable(this.page)]).pipe(
      debounceTime(150),
      tap(() => this.loading.set(true)),
      switchMap(([view, market, search, page]) => {
        const query = { ...VIEWS.find(v => v.key === view)!.query, market, search, page, pageSize: PAGE_SIZE };
        return this.api.listClaims(query).pipe(catchError(err => {
          this.error.set(problemMessage(err));
          return of(null);
        }));
      }),
      takeUntilDestroyed()
    ).subscribe(result => {
      if (result) {
        this.result.set(result);
        this.error.set('');
      }
      this.loading.set(false);
    });
  }

  protected setView(view: View): void {
    this.view.set(view);
    this.page.set(1);
  }

  protected isBreach(c: ClaimSummary): boolean {
    return !['Settled', 'Rejected'].includes(c.status) && c.ageDays > this.slaDays;
  }

  protected open(c: ClaimSummary): void {
    this.router.navigate(['/claims', c.id]);
  }
}
