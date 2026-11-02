import { Component } from '@angular/core';

@Component({
  selector: 'app-welcome',
  standalone: true,
  template: `
    <section class="card welcome">
      <h1>Claims Management</h1>
      <p>Choose a demo user in the top-right corner to continue.</p>
      <div class="grid-2">
        <div>
          <h3>Claimants</h3>
          <p class="muted">Report an incident, track progress, answer questions from the claims team and receive decisions.</p>
        </div>
        <div>
          <h3>Claims officers</h3>
          <p class="muted">Pick up incoming claims, request information, approve, reject and settle, and monitor workload and liability exposure.</p>
        </div>
      </div>
    </section>
  `
})
export class WelcomeComponent {}
