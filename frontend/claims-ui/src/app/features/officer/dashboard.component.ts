import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService, problemMessage } from '../../core/api.service';
import { Exposure, STATUS_LABELS, Workload } from '../../core/models';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, CurrencyPipe, DecimalPipe, DatePipe, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h1>Team dashboard</h1>
        <p class="muted">Workload, service levels and outstanding liability across all markets.</p>
      </div>
      <button class="btn" (click)="load()">Refresh</button>
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    @if (workload(); as w) {
      @if (exposure(); as e) {
        <section class="kpis">
          <div class="kpi"><span>Open claims</span><strong>{{ w.openClaims }}</strong></div>
          <div class="kpi"><span>Unassigned</span><strong>{{ w.unassignedClaims }}</strong></div>
          <div class="kpi" [class.alarm]="w.slaBreaches > 0"><span>Over {{ w.slaDays }}-day SLA</span><strong>{{ w.slaBreaches }}</strong></div>
          <div class="kpi"><span>Assigned to me</span><strong>{{ w.myOpenClaims }}</strong></div>
          <div class="kpi wide"><span>Outstanding liability</span><strong>{{ e.totalOutstanding | currency: e.reportingCurrency : 'code' : '1.0-0' }}</strong></div>
        </section>

        <div class="grid-2">
          <section class="card">
            <h2>Claims by status</h2>
            @for (s of w.byStatus; track s.status) {
              <div class="bar-row">
                <span class="bar-label">{{ statusLabels[s.status] }}</span>
                <span class="bar"><span class="bar-fill" [class]="'bar-fill status-' + s.status" [style.width.%]="pct(s.count, maxStatus())"></span></span>
                <span class="bar-value">{{ s.count }}</span>
              </div>
            }
          </section>

          <section class="card">
            <h2>Age of open claims</h2>
            @for (a of w.aging; track a.label; let i = $index) {
              <div class="bar-row">
                <span class="bar-label">{{ a.label }}</span>
                <span class="bar"><span class="bar-fill" [class.aging-old]="i >= 2" [style.width.%]="pct(a.count, maxAging())"></span></span>
                <span class="bar-value">{{ a.count }}</span>
              </div>
            }
          </section>
        </div>

        <section class="card table-wrap">
          <h2>Team workload</h2>
          <table class="table">
            <thead>
              <tr>
                <th>Officer</th><th class="num">Open</th><th class="num">Under review</th><th class="num">Awaiting claimant</th>
                <th class="num">Awaiting payment</th><th class="num">Decided (30d)</th><th class="num">Settled (30d)</th><th class="num">Avg days to decision</th>
              </tr>
            </thead>
            <tbody>
              @for (o of w.officers; track o.officerId) {
                <tr>
                  <td>{{ o.officerName }}</td>
                  <td class="num"><strong>{{ o.openAssigned }}</strong></td>
                  <td class="num">{{ o.underReview }}</td>
                  <td class="num">{{ o.awaitingClaimant }}</td>
                  <td class="num">{{ o.awaitingSettlement }}</td>
                  <td class="num">{{ o.decidedLast30Days }}</td>
                  <td class="num">{{ o.settledLast30Days }}</td>
                  <td class="num">{{ o.averageDaysToDecision === null ? '—' : (o.averageDaysToDecision | number: '1.1-1') }}</td>
                </tr>
              }
            </tbody>
          </table>
        </section>

        <section class="card table-wrap">
          <h2>Liability exposure by market</h2>
          <table class="table">
            <thead>
              <tr>
                <th>Market</th><th class="num">Open claims</th><th class="num">Undecided (estimates)</th>
                <th class="num">Approved, unpaid</th><th class="num">Total (local)</th><th class="num">Total ({{ e.reportingCurrency }})</th>
              </tr>
            </thead>
            <tbody>
              @for (m of e.byMarket; track m.marketCode) {
                <tr>
                  <td>{{ m.marketName }}</td>
                  <td class="num">{{ m.openClaims }}</td>
                  <td class="num">{{ m.estimatedOutstanding | currency: m.currency : 'code' : '1.0-0' }}</td>
                  <td class="num">{{ m.approvedAwaitingSettlement | currency: m.currency : 'code' : '1.0-0' }}</td>
                  <td class="num">{{ m.totalOutstanding | currency: m.currency : 'code' : '1.0-0' }}</td>
                  <td class="num"><strong>{{ m.totalOutstandingReporting | currency: e.reportingCurrency : 'code' : '1.0-0' }}</strong></td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td>All markets</td>
                <td class="num">{{ e.openClaims }}</td>
                <td class="num">{{ e.estimatedOutstanding | currency: e.reportingCurrency : 'code' : '1.0-0' }}</td>
                <td class="num">{{ e.approvedAwaitingSettlement | currency: e.reportingCurrency : 'code' : '1.0-0' }}</td>
                <td></td>
                <td class="num"><strong>{{ e.totalOutstanding | currency: e.reportingCurrency : 'code' : '1.0-0' }}</strong></td>
              </tr>
            </tfoot>
          </table>
          <p class="small muted">{{ e.basis }} Settled in the last 30 days: {{ e.settledLast30Days | currency: e.reportingCurrency : 'code' : '1.0-0' }}.</p>
        </section>

        <div class="grid-2">
          <section class="card">
            <h2>Largest open exposures</h2>
            @for (c of e.largestOpenClaims; track c.id) {
              <a class="list-row" [routerLink]="['/claims', c.id]">
                <span><strong>{{ c.claimNumber }}</strong> <span class="muted small">{{ c.claimType }} · {{ c.marketName }}</span></span>
                <span>{{ c.outstandingAmount | currency: c.currency : 'code' : '1.0-0' }}</span>
              </a>
            } @empty { <p class="muted">No open claims.</p> }
          </section>

          <section class="card">
            <h2>Oldest unassigned</h2>
            @for (c of w.oldestUnassigned; track c.id) {
              <a class="list-row" [routerLink]="['/claims', c.id]">
                <span><strong>{{ c.claimNumber }}</strong> <span class="muted small">{{ c.claimantName }}</span></span>
                <span [class.sla-breach]="c.ageDays > w.slaDays">{{ c.ageDays }} days</span>
              </a>
            } @empty { <p class="muted">Nothing waiting to be picked up.</p> }
          </section>
        </div>

        <p class="small muted">Generated {{ w.generatedAt | date: 'd MMM y, HH:mm:ss' }}</p>
      }
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  protected readonly workload = signal<Workload | null>(null);
  protected readonly exposure = signal<Exposure | null>(null);
  protected readonly error = signal('');
  protected readonly statusLabels = STATUS_LABELS;

  protected readonly maxStatus = computed(() => Math.max(1, ...(this.workload()?.byStatus.map(s => s.count) ?? [])));
  protected readonly maxAging = computed(() => Math.max(1, ...(this.workload()?.aging.map(a => a.count) ?? [])));

  constructor() {
    this.load();
  }

  protected load(): void {
    forkJoin({ workload: this.api.workload(), exposure: this.api.exposure() }).subscribe({
      next: ({ workload, exposure }) => {
        this.workload.set(workload);
        this.exposure.set(exposure);
        this.error.set('');
      },
      error: err => this.error.set(problemMessage(err))
    });
  }

  protected pct(value: number, max: number): number {
    return Math.round((value / max) * 100);
  }
}
