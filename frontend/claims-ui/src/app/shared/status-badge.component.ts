import { Component, computed, input } from '@angular/core';
import { ClaimStatus, STATUS_LABELS } from '../core/models';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  template: `<span class="badge" [class]="'badge status-' + status()">{{ label() }}</span>`
})
export class StatusBadgeComponent {
  readonly status = input.required<ClaimStatus>();
  protected readonly label = computed(() => STATUS_LABELS[this.status()]);
}
