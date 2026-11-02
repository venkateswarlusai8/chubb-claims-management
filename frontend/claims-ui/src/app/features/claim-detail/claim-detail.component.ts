import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { EMPTY, Observable, catchError, switchMap, timer } from 'rxjs';
import { ApiService, problemMessage } from '../../core/api.service';
import { ClaimAction, ClaimDetail, STATUS_LABELS } from '../../core/models';
import { SessionService } from '../../core/session';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

/** Claims progress asynchronously (the other party acts), so the open detail view refreshes periodically. */
const REFRESH_MS = 10_000;

type Panel = 'requestInformation' | 'approve' | 'reject' | 'settle' | null;

@Component({
  selector: 'app-claim-detail',
  standalone: true,
  imports: [RouterLink, FormsModule, CurrencyPipe, DatePipe, StatusBadgeComponent],
  template: `
    <a class="back" [routerLink]="session.isOfficer() ? '/officer/queue' : '/claims'">← Back</a>

    @if (error() && !claim()) { <p class="alert error">{{ error() }}</p> }

    @if (claim(); as c) {
      @if (submitted()) {
        <p class="alert success">Claim {{ c.claimNumber }} submitted. We'll notify you as it progresses.</p>
      }

      <div class="page-head">
        <div>
          <h1>{{ c.claimNumber }} <app-status-badge [status]="c.status" /></h1>
          <p class="muted">{{ c.claimType }} claim · {{ c.marketName }} · submitted {{ c.submittedAt | date: 'd MMM y, HH:mm' }} · {{ c.ageDays }} days {{ isOpen() ? 'open' : 'to close' }}</p>
        </div>
      </div>

      <div class="detail-layout">
        <div class="stack">
          <!-- Actions: rendered from the server's allowedActions so the UI never re-implements lifecycle rules -->
          @if (c.allowedActions.length) {
            <section class="card actions">
              <h2>{{ session.isOfficer() ? 'Actions' : 'Your response is needed' }}</h2>

              @if (can('provideInformation') && openRequest(); as req) {
                <p class="question"><strong>{{ req.requestedByName }} asked:</strong> {{ req.question }}</p>
                <textarea rows="4" [(ngModel)]="responseText" placeholder="Your answer"></textarea>
                <button class="btn primary" [disabled]="busy() || !responseText.trim()" (click)="run(api.provideInformation(c.id, req.id, responseText))">Send response</button>
              }

              @if (session.isOfficer()) {
                <div class="row gap wrap">
                  @if (can('assign')) { <button class="btn primary" [disabled]="busy()" (click)="run(api.assign(c.id))">Pick up claim</button> }
                  @if (can('requestInformation')) { <button class="btn" [class.selected]="panel() === 'requestInformation'" (click)="toggle('requestInformation')">Request information</button> }
                  @if (can('approve')) { <button class="btn" [class.selected]="panel() === 'approve'" (click)="toggle('approve')">Approve</button> }
                  @if (can('reject')) { <button class="btn danger-outline" [class.selected]="panel() === 'reject'" (click)="toggle('reject')">Reject</button> }
                  @if (can('settle')) { <button class="btn primary" [class.selected]="panel() === 'settle'" (click)="toggle('settle')">Record settlement</button> }
                </div>

                @switch (panel()) {
                  @case ('requestInformation') {
                    <div class="panel">
                      <label>Question for the claimant
                        <textarea rows="3" [(ngModel)]="question" placeholder="e.g. Please upload photos of the damage"></textarea>
                      </label>
                      <button class="btn primary" [disabled]="busy() || question.trim().length < 5" (click)="run(api.requestInformation(c.id, question))">Send request</button>
                    </div>
                  }
                  @case ('approve') {
                    <div class="panel">
                      <label>Approved amount ({{ c.currency }})
                        <input type="number" min="0.01" step="0.01" [(ngModel)]="approvedAmount" />
                      </label>
                      <label>Note (optional)
                        <input [(ngModel)]="approveNote" placeholder="e.g. Approved less policy excess" />
                      </label>
                      <button class="btn primary" [disabled]="busy() || !(approvedAmount > 0)" (click)="run(api.approve(c.id, approvedAmount, approveNote || null))">Confirm approval</button>
                    </div>
                  }
                  @case ('reject') {
                    <div class="panel">
                      <label>Reason (shared with the claimant)
                        <textarea rows="3" [(ngModel)]="rejectReason"></textarea>
                      </label>
                      <button class="btn danger" [disabled]="busy() || rejectReason.trim().length < 5" (click)="run(api.reject(c.id, rejectReason))">Confirm rejection</button>
                    </div>
                  }
                  @case ('settle') {
                    <div class="panel">
                      <label>Amount paid ({{ c.currency }}, up to {{ c.approvedAmount | currency: c.currency : 'code' }})
                        <input type="number" min="0.01" step="0.01" [(ngModel)]="settledAmount" />
                      </label>
                      <button class="btn primary" [disabled]="busy() || !(settledAmount > 0)" (click)="run(api.settle(c.id, settledAmount))">Confirm payment</button>
                    </div>
                  }
                }
              }

              @if (error()) { <p class="alert error">{{ error() }}</p> }
            </section>
          } @else if (session.isOfficer() && c.assignedOfficerName && isOpen() && c.assignedOfficerId !== session.user()?.id) {
            <p class="alert info">Assigned to {{ c.assignedOfficerName }}. Only the assigned officer can action this claim.</p>
          } @else if (!session.isOfficer() && isOpen()) {
            <p class="alert info">{{ claimantStatusText() }}</p>
          }

          <section class="card">
            <h2>Incident</h2>
            <dl class="facts">
              <dt>Policy</dt><dd>{{ c.policyNumber }}</dd>
              <dt>Date of incident</dt><dd>{{ c.incidentDate | date: 'd MMM y' }}</dd>
              <dt>Claimant</dt><dd>{{ c.claimantName }} · {{ c.claimantEmail }}</dd>
            </dl>
            <p class="description">{{ c.description }}</p>
          </section>

          @if (c.informationRequests.length) {
            <section class="card">
              <h2>Information requests</h2>
              @for (r of c.informationRequests; track r.id) {
                <div class="info-request" [class.open]="r.isOpen">
                  <div class="small muted">{{ r.requestedByName }} · {{ r.requestedAt | date: 'd MMM, HH:mm' }}</div>
                  <p><strong>Q:</strong> {{ r.question }}</p>
                  @if (r.response) {
                    <p><strong>A:</strong> {{ r.response }} <span class="small muted">({{ r.respondedAt | date: 'd MMM, HH:mm' }})</span></p>
                  } @else if (r.isOpen) {
                    <p class="small warn-text">Awaiting claimant response</p>
                  } @else {
                    <p class="small muted">Closed without response</p>
                  }
                </div>
              }
            </section>
          }
        </div>

        <aside class="stack">
          <section class="card">
            <h2>Amounts</h2>
            <dl class="facts">
              <dt>Estimated</dt><dd>{{ c.estimatedAmount | currency: c.currency : 'code' }}</dd>
              @if (c.approvedAmount !== null) { <dt>Approved</dt><dd>{{ c.approvedAmount | currency: c.currency : 'code' }}</dd> }
              @if (c.settledAmount !== null) { <dt>Paid</dt><dd>{{ c.settledAmount | currency: c.currency : 'code' }}</dd> }
              @if (session.isOfficer()) { <dt>Outstanding liability</dt><dd><strong>{{ c.outstandingAmount | currency: c.currency : 'code' }}</strong></dd> }
            </dl>
            @if (c.decisionReason) { <p class="small"><strong>Decision note:</strong> {{ c.decisionReason }}</p> }
            <p class="small muted">Handled by: {{ c.assignedOfficerName ?? 'Not yet assigned' }}</p>
          </section>

          <section class="card">
            <h2>Timeline</h2>
            <ol class="timeline">
              @for (h of history(); track $index) {
                <li>
                  <div><strong>{{ statusLabels[h.toStatus] }}</strong> <span class="small muted">· {{ h.occurredAt | date: 'd MMM, HH:mm' }}</span></div>
                  <div class="small muted">{{ h.actorName }} ({{ h.actorRole }})</div>
                  @if (h.note) { <div class="small">{{ h.note }}</div> }
                </li>
              }
            </ol>
          </section>
        </aside>
      </div>
    }
  `
})
export class ClaimDetailComponent implements OnInit {
  readonly id = input.required<string>();
  readonly submitted = input<string>();

  protected readonly api = inject(ApiService);
  protected readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly panel = signal<Panel>(null);
  protected readonly statusLabels = STATUS_LABELS;

  protected readonly isOpen = computed(() => !['Settled', 'Rejected'].includes(this.claim()?.status ?? ''));
  protected readonly openRequest = computed(() => this.claim()?.informationRequests.find(r => r.isOpen) ?? null);
  protected readonly history = computed(() => [...(this.claim()?.history ?? [])].reverse());
  protected readonly claimantStatusText = computed(() => {
    switch (this.claim()?.status) {
      case 'Submitted': return 'Your claim has been received and is waiting for a claims officer to pick it up.';
      case 'UnderReview': return 'A claims officer is reviewing your claim. We will notify you of any updates.';
      case 'Approved': return 'Your claim has been approved. Payment is being arranged.';
      default: return '';
    }
  });

  protected responseText = '';
  protected question = '';
  protected approvedAmount = 0;
  protected approveNote = '';
  protected rejectReason = '';
  protected settledAmount = 0;

  ngOnInit(): void {
    timer(0, REFRESH_MS).pipe(
      // Errors are handled per poll so one failed refresh does not stop future refreshes.
      switchMap(() => this.api.getClaim(this.id()).pipe(catchError(err => {
        this.error.set(problemMessage(err));
        return EMPTY;
      }))),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(claim => this.claim.set(claim));
  }

  protected can(action: ClaimAction): boolean {
    return this.claim()?.allowedActions.includes(action) ?? false;
  }

  protected toggle(panel: Panel): void {
    const c = this.claim();
    this.error.set('');
    if (panel === 'approve' && c) this.approvedAmount = c.estimatedAmount;
    if (panel === 'settle' && c) this.settledAmount = c.approvedAmount ?? 0;
    this.panel.set(this.panel() === panel ? null : panel);
  }

  /** Runs a command; the API returns the updated claim (including the next allowed actions). */
  protected run(command: Observable<ClaimDetail>): void {
    this.busy.set(true);
    this.error.set('');
    command.subscribe({
      next: claim => {
        this.claim.set(claim);
        this.panel.set(null);
        this.responseText = this.question = this.approveNote = this.rejectReason = '';
        this.busy.set(false);
      },
      error: err => {
        this.error.set(problemMessage(err));
        this.busy.set(false);
      }
    });
  }
}
