import { Component, inject } from '@angular/core';
import { AuthService } from '../../../core/auth.service';
import { RegisterForm } from '../register-form/register-form';

// The app's first route. It renders before any API call can succeed or fail: no resolved tenant,
// no /me, no data of any kind -- see landing.guard.ts for how '/' chooses between this page and
// the tenant picker.
@Component({
  imports: [RegisterForm],
  selector: 'scribe-landing',
  styleUrl: './landing.scss',
  templateUrl: './landing.html',
})
export class Landing {
  private readonly auth = inject(AuthService);

  // A full-page navigation to the gateway, never a routerLink and never an HttpClient call: the
  // OAuth code+PKCE flow is a browser redirect chain, and an XHR cannot follow it anywhere useful.
  // AuthService.login() owns the hand-off.
  login(): void {
    this.auth.login('/');
  }
}
