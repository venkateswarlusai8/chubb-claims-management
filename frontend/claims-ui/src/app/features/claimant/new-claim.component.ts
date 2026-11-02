import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, problemMessage } from '../../core/api.service';
import { ClaimType } from '../../core/models';

const today = () => new Date().toISOString().slice(0, 10);

@Component({
  selector: 'app-new-claim',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page-head">
      <div>
        <h1>Report an incident</h1>
        <p class="muted">Tell us what happened. Your name and contact details come from your account.</p>
      </div>
    </div>

    <form class="card form" [formGroup]="form" (ngSubmit)="submit()">
      <div class="grid-2">
        <label>Policy number
          <input formControlName="policyNumber" placeholder="e.g. MTR-SG-100234" />
          @if (invalid('policyNumber')) { <span class="field-error">Enter your policy number (3–40 characters).</span> }
        </label>
        <label>Market
          <select formControlName="marketCode">
            @for (m of markets(); track m.code) { <option [value]="m.code">{{ m.name }} ({{ m.currency }})</option> }
          </select>
        </label>
        <label>Type of claim
          <select formControlName="claimType">
            <option value="Motor">Motor</option>
            <option value="Property">Property</option>
          </select>
        </label>
        <label>Date of incident
          <input type="date" formControlName="incidentDate" [max]="maxDate" />
          @if (invalid('incidentDate')) { <span class="field-error">Choose the date of the incident.</span> }
        </label>
      </div>

      <label>What happened?
        <textarea rows="5" formControlName="description" placeholder="Describe the incident and the damage or loss"></textarea>
        @if (invalid('description')) { <span class="field-error">Please describe the incident (at least 10 characters).</span> }
      </label>

      <label class="narrow">Estimated amount ({{ currency() }})
        <input type="number" min="0.01" step="0.01" formControlName="estimatedAmount" />
        @if (invalid('estimatedAmount')) { <span class="field-error">Enter an amount greater than zero.</span> }
      </label>

      @if (error()) { <p class="alert error">{{ error() }}</p> }

      <div class="row gap">
        <button class="btn primary" type="submit" [disabled]="submitting()">{{ submitting() ? 'Submitting…' : 'Submit claim' }}</button>
        <a class="btn" routerLink="/claims">Cancel</a>
      </div>
    </form>
  `
})
export class NewClaimComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly maxDate = today();
  protected readonly markets = computed(() => this.reference()?.markets ?? []);
  protected readonly submitting = signal(false);
  protected readonly error = signal('');
  private readonly reference = toSignal(this.api.referenceData$);

  protected readonly form = this.fb.nonNullable.group({
    policyNumber: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(40)]],
    marketCode: ['SG', Validators.required],
    claimType: ['Motor' as ClaimType, Validators.required],
    incidentDate: [today(), Validators.required],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]],
    estimatedAmount: [0, [Validators.required, Validators.min(0.01)]]
  });

  private readonly marketCode = toSignal(this.form.controls.marketCode.valueChanges, { initialValue: 'SG' });
  protected readonly currency = computed(() => this.markets().find(m => m.code === this.marketCode())?.currency ?? '');

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set('');
    this.api.submitClaim(this.form.getRawValue()).subscribe({
      next: claim => this.router.navigate(['/claims', claim.id], { queryParams: { submitted: 1 } }),
      error: err => {
        this.error.set(problemMessage(err));
        this.submitting.set(false);
      }
    });
  }
}
