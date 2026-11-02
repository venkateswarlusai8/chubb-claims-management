import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApiService, problemMessage } from '../../core/api.service';
import { ClaimSummary } from '../../core/models';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-my-claims',
  standalone: true,
  imports: [RouterLink, CurrencyPipe, DatePipe, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h1>My claims</h1>
        <p class="muted">Track progress and respond when the claims team needs more information.</p>
      </div>
      <a class="btn primary" routerLink="/claims/new">Report an incident</a>
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    @if (actionNeeded().length) {
      <section class="alert warn">
        <strong>Action needed:</strong> the claims team is waiting for information on
        @for (c of actionNeeded(); track c.id; let last = $last) {
          <a [routerLink]="['/claims', c.id]">{{ c.claimNumber }}</a>{{ last ? '.' : ', ' }}
        }
      </section>
    }

    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (!claims().length) {
      <section class="card empty">
        <p>You have no claims yet.</p>
        <a class="btn primary" routerLink="/claims/new">Report an incident</a>
      </section>
    } @else {
      <div class="claim-cards">
        @for (c of claims(); track c.id) {
          <a class="card claim-card" [routerLink]="['/claims', c.id]">
            <div class="row between">
              <strong>{{ c.claimNumber }}</strong>
              <app-status-badge [status]="c.status" />
            </div>
            <div class="muted">{{ c.claimType }} · {{ c.marketName }}</div>
            <div class="row between">
              <span>{{ c.estimatedAmount | currency: c.currency : 'code' }}</span>
              <span class="muted small">Submitted {{ c.submittedAt | date: 'd MMM y' }}</span>
            </div>
            @if (c.assignedOfficerName) { <div class="small muted">Handled by {{ c.assignedOfficerName }}</div> }
          </a>
        }
      </div>
    }
  `
})
export class MyClaimsComponent {
  private readonly api = inject(ApiService);
  protected readonly claims = signal<ClaimSummary[]>([]);
  protected readonly actionNeeded = signal<ClaimSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  constructor() {
    this.api.listClaims({ pageSize: 100 }).pipe(takeUntilDestroyed()).subscribe({
      next: page => {
        this.claims.set(page.items);
        this.actionNeeded.set(page.items.filter(c => c.status === 'InformationRequired'));
        this.loading.set(false);
      },
      error: err => {
        this.error.set(problemMessage(err));
        this.loading.set(false);
      }
    });
  }
}
