import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { EMPTY, catchError, switchMap, timer } from 'rxjs';
import { ApiService } from './core/api.service';
import { User } from './core/models';
import { SessionService } from './core/session';

/** Notifications are produced asynchronously by the backend, so the shell polls for the unread count. */
const NOTIFICATION_POLL_MS = 10_000;

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">
        <span class="brand-mark">C</span>
        <span><small>APAC</small>Claims</span>
      </a>

      @if (session.user()) {
        <nav class="nav">
          @if (session.isOfficer()) {
            <a routerLink="/officer/queue" routerLinkActive="active">Claims queue</a>
            <a routerLink="/officer/dashboard" routerLinkActive="active">Dashboard</a>
          } @else {
            <a routerLink="/claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">My claims</a>
            <a routerLink="/claims/new" routerLinkActive="active">Report an incident</a>
          }
          <a routerLink="/notifications" routerLinkActive="active" class="bell">
            Notifications
            @if (unread() > 0) { <span class="count">{{ unread() }}</span> }
          </a>
        </nav>
      }

      <label class="switcher">
        <span>Signed in as</span>
        <select (change)="switchUser($any($event.target).value)">
          @if (!session.user()) { <option value="" selected disabled>Choose a demo user</option> }
          <optgroup label="Claimants">
            @for (u of claimants(); track u.id) { <option [value]="u.id" [selected]="u.id === session.user()?.id">{{ u.name }}</option> }
          </optgroup>
          <optgroup label="Claims officers">
            @for (u of officers(); track u.id) { <option [value]="u.id" [selected]="u.id === session.user()?.id">{{ u.name }}</option> }
          </optgroup>
        </select>
      </label>
    </header>

    <main class="page">
      <router-outlet />
    </main>

    <footer class="footer">Prototype. The user switcher is a demo affordance, not authentication; the API enforces all permissions.</footer>
  `
})
export class AppComponent {
  protected readonly session = inject(SessionService);
  private readonly api = inject(ApiService);

  private readonly users = signal<User[]>([]);
  protected readonly claimants = signal<User[]>([]);
  protected readonly officers = signal<User[]>([]);
  protected readonly unread = signal(0);

  constructor() {
    this.api.demoUsers().pipe(takeUntilDestroyed()).subscribe({
      next: users => {
        this.users.set(users);
        this.claimants.set(users.filter(u => u.role === 'Claimant'));
        this.officers.set(users.filter(u => u.role === 'Officer'));
      },
      error: () => this.users.set([])
    });

    toObservable(this.session.user).pipe(
      switchMap(user => user
        ? timer(0, NOTIFICATION_POLL_MS).pipe(switchMap(() => this.api.notifications().pipe(catchError(() => EMPTY))))
        : EMPTY),
      takeUntilDestroyed()
    ).subscribe(list => this.unread.set(list.unreadCount));
  }

  protected switchUser(id: string): void {
    const user = this.users().find(u => u.id === id);
    if (user) {
      this.unread.set(0);
      this.session.switchTo(user);
    }
  }
}
