import { Component, inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { SessionService, hasRole } from './core/session';

@Component({ standalone: true, template: '' })
class SwitchingComponent {}

/** Sends each user to their role's home screen. */
const roleHome = () => {
  const session = inject(SessionService);
  return inject(Router).parseUrl(session.role() ? session.homeFor(session.role()) : '/welcome');
};

// One Angular app, two user types: feature areas are lazy-loaded and guarded by role.
// Claim detail is shared: the server's allowedActions decides which actions each user sees.
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [roleHome], children: [] },
  { path: 'welcome', loadComponent: () => import('./features/welcome.component').then(m => m.WelcomeComponent) },
  { path: 'switching', component: SwitchingComponent },
  // Order matters: a role guard that fails redirects rather than falling through, so the shared
  // detail route must be matched before the claimant-only list route.
  {
    path: 'claims/new',
    canMatch: [hasRole('Claimant')],
    loadComponent: () => import('./features/claimant/new-claim.component').then(m => m.NewClaimComponent)
  },
  {
    path: 'claims/:id',
    canMatch: [hasRole('Claimant', 'Officer')],
    loadComponent: () => import('./features/claim-detail/claim-detail.component').then(m => m.ClaimDetailComponent)
  },
  {
    path: 'claims',
    pathMatch: 'full',
    canMatch: [hasRole('Claimant')],
    loadComponent: () => import('./features/claimant/my-claims.component').then(m => m.MyClaimsComponent)
  },
  {
    path: 'officer',
    canMatch: [hasRole('Officer')],
    children: [
      { path: 'queue', loadComponent: () => import('./features/officer/queue.component').then(m => m.QueueComponent) },
      { path: 'dashboard', loadComponent: () => import('./features/officer/dashboard.component').then(m => m.DashboardComponent) },
      { path: '', pathMatch: 'full', redirectTo: 'queue' }
    ]
  },
  {
    path: 'notifications',
    canMatch: [hasRole('Claimant', 'Officer')],
    loadComponent: () => import('./features/notifications.component').then(m => m.NotificationsComponent)
  },
  { path: '**', redirectTo: '' }
];
