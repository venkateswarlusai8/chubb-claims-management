import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { UserRole, User } from './models';

const STORAGE_KEY = 'claims.demoUser';

/**
 * Holds the current demo identity. This stands in for an OIDC login: with a real identity provider
 * the session would hold a token, and the interceptor would send "Authorization: Bearer ..." instead.
 * The UI uses the role only to choose screens; the API enforces every permission itself.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly router = inject(Router);
  private readonly current = signal<User | null>(readStored());

  readonly user = this.current.asReadonly();
  readonly role = computed(() => this.current()?.role ?? null);
  readonly isOfficer = computed(() => this.role() === 'Officer');

  switchTo(user: User): void {
    this.current.set(user);
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(user)); } catch { /* storage unavailable */ }
    // Re-create routed components so every screen reloads its data for the new identity.
    this.router.navigateByUrl('/switching', { skipLocationChange: true }).then(() => this.router.navigateByUrl(this.homeFor(user.role)));
  }

  homeFor(role: UserRole | null): string {
    return role === 'Officer' ? '/officer/queue' : '/claims';
  }
}

function readStored(): User | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as User) : null;
  } catch {
    return null;
  }
}

/** Attaches the demo identity header to API calls. */
export const identityInterceptor: HttpInterceptorFn = (req, next) => {
  const user = inject(SessionService).user();
  return user && req.url.startsWith('/api')
    ? next(req.clone({ setHeaders: { 'X-Demo-User': user.id } }))
    : next(req);
};

/** Route guard: only users with one of the given roles can enter; others go to their own home. */
export function hasRole(...roles: UserRole[]): CanMatchFn {
  return () => {
    const session = inject(SessionService);
    const role = session.role();
    if (role && roles.includes(role)) return true;
    return inject(Router).parseUrl(role ? session.homeFor(role) : '/welcome');
  };
}
