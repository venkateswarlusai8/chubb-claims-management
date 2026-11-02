import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService, problemMessage } from '../core/api.service';
import { AppNotification } from '../core/models';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [DatePipe],
  template: `
    <div class="page-head">
      <div>
        <h1>Notifications</h1>
        <p class="muted">Updates are generated asynchronously from claim events.</p>
      </div>
      @if (unread() > 0) { <button class="btn" (click)="markAll()">Mark all as read</button> }
    </div>

    @if (error()) { <p class="alert error">{{ error() }}</p> }

    <section class="card">
      @for (n of items(); track n.id) {
        <button class="notification" [class.unread]="!n.isRead" (click)="open(n)">
          <div class="row between">
            <strong>{{ n.title }}</strong>
            <span class="small muted">{{ n.createdAt | date: 'd MMM, HH:mm' }}</span>
          </div>
          <div>{{ n.message }}</div>
          <div class="small muted">{{ n.claimNumber }}</div>
        </button>
      } @empty {
        <p class="muted">No notifications yet.</p>
      }
    </section>
  `
})
export class NotificationsComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly items = signal<AppNotification[]>([]);
  protected readonly unread = signal(0);
  protected readonly error = signal('');

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.notifications().subscribe({
      next: list => {
        this.items.set(list.items);
        this.unread.set(list.unreadCount);
      },
      error: err => this.error.set(problemMessage(err))
    });
  }

  protected open(n: AppNotification): void {
    const go = () => this.router.navigate(['/claims', n.claimId]);
    if (n.isRead) {
      go();
      return;
    }
    this.api.markNotificationRead(n.id).subscribe({ next: go, error: go });
  }

  protected markAll(): void {
    this.api.markAllNotificationsRead().subscribe(() => this.load());
  }
}
