import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth.service';

type SubmitState = 'idle' | 'submitting';

// Identity's default RequiredLength. The composition rules it also enforces (a digit, an upper and
// lower case letter, a symbol) are deliberately NOT re-implemented here: they are server-owned
// configuration, and a copy in TypeScript drifts the moment someone tunes IdentityOptions. The
// hint below states them for the user; the server's own errors are what actually reject.
const MIN_PASSWORD_LENGTH = 6;

// Matches RegisterViewModelValidator.DisplayName.MaximumLength(64).
const MAX_DISPLAY_NAME_LENGTH = 64;

@Component({
  imports: [ReactiveFormsModule],
  selector: 'scribe-register-form',
  styleUrl: './register-form.scss',
  templateUrl: './register-form.html',
})
export class RegisterForm {
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);

  readonly minPasswordLength = MIN_PASSWORD_LENGTH;
  readonly maxDisplayNameLength = MAX_DISPLAY_NAME_LENGTH;

  readonly state = signal<SubmitState>('idle');

  // Errors the server sent that don't belong to a single field — an Identity code we can't place,
  // or a non-400 failure. Rendered together above the submit button in a live region.
  readonly formErrors = signal<string[]>([]);

  readonly form = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.maxLength(MAX_DISPLAY_NAME_LENGTH)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]],
  });

  // Field-level messages, resolved once here rather than branched over in the template. Server
  // messages win when present: they are the authoritative rejection, and re-stating our own
  // client-side guess next to them would read as two different problems.
  messagesFor(name: ControlName): string[] {
    const control = this.form.controls[name];
    if (!control.touched || control.valid) {
      return [];
    }

    const errors = control.errors ?? {};

    if (Array.isArray(errors['server'])) {
      return errors['server'] as string[];
    }

    const messages: string[] = [];
    if (errors['required']) {
      messages.push(REQUIRED_MESSAGES[name]);
    }
    if (errors['email']) {
      messages.push('Enter a valid email address.');
    }
    if (errors['minlength']) {
      messages.push(`Use at least ${MIN_PASSWORD_LENGTH} characters.`);
    }
    if (errors['maxlength']) {
      messages.push(`Use ${MAX_DISPLAY_NAME_LENGTH} characters or fewer.`);
    }

    return messages;
  }

  submit(): void {
    this.formErrors.set([]);

    if (this.form.invalid) {
      // Nothing is shown until a control is touched, so a submit with empty fields would otherwise
      // look like a dead button.
      this.form.markAllAsTouched();
      return;
    }

    this.state.set('submitting');
    this.form.disable({ emitEvent: false });

    const { displayName, email, password } = this.form.getRawValue();

    this.auth
      .register({ email, password, displayName: displayName.trim() || null })
      .subscribe({
        // The response body is a UserServiceModel and it is deliberately discarded. Registering
        // does NOT sign you in: there is no session to record, no token to hold (the SPA never
        // holds one), and the interactive sign-in step still has to happen. Handing off to
        // login() is a full-page navigation to the gateway, which is the only way an OAuth
        // code+PKCE redirect chain can run.
        next: () => this.auth.login('/'),
        error: (error: unknown) => {
          this.state.set('idle');
          this.form.enable({ emitEvent: false });
          this.applyServerErrors(error);
        },
      });
  }

  // The API answers a rejected registration with ValidationProblemDetails: a dictionary keyed
  // either by a FluentValidation property name ("Email") or an Identity error code
  // ("DuplicateEmail", "PasswordRequiresDigit"). Both shapes get routed to the field they are
  // about; anything unplaceable is shown at form level rather than swallowed.
  private applyServerErrors(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      this.formErrors.set([GENERIC_ERROR]);
      return;
    }

    const errors: unknown = error.error?.errors;
    if (error.status !== 400 || errors === null || typeof errors !== 'object') {
      this.formErrors.set([GENERIC_ERROR]);
      return;
    }

    const unplaceable: string[] = [];

    for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
      const messages = (Array.isArray(value) ? value : [value]).map(String);
      const control = controlFor(key);

      if (control === null) {
        unplaceable.push(...messages);
        continue;
      }

      this.form.controls[control].setErrors({ server: messages });
      this.form.controls[control].markAsTouched();
    }

    this.formErrors.set(unplaceable);
  }
}

const GENERIC_ERROR = "We couldn't create your account. Please try again.";

type ControlName = 'displayName' | 'email' | 'password';

// Substring matching rather than an exhaustive code table on purpose: Identity ships a dozen
// password codes and can gain more, and every one of them contains "Password".
function controlFor(key: string): ControlName | null {
  const normalized = key.toLowerCase();

  if (normalized.includes('password')) {
    return 'password';
  }
  if (normalized.includes('email') || normalized.includes('username')) {
    return 'email';
  }
  if (normalized.includes('displayname')) {
    return 'displayName';
  }

  return null;
}

const REQUIRED_MESSAGES: Record<ControlName, string> = {
  displayName: 'Enter a display name.',
  email: 'Enter your email address.',
  password: 'Choose a password.',
};
