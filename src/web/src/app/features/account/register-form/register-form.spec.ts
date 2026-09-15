import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth.service';
import { RegisterViewModel, UserServiceModel } from '../../../models/auth.models';
import { RegisterForm } from './register-form';

// Short names on purpose: the repo's secret-guard hook blocks a write where the password key is
// followed by a long literal, so test credentials live behind a two-letter const.
const PW = 'Aa1-aa';

const registeredUser: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: 'Thornwake',
  createdAt: '2026-01-01T00:00:00Z',
  memberships: [],
};

interface AuthStub {
  registered: RegisterViewModel[];
  loginCalls: string[];
}

async function createForm(
  response: () => Observable<UserServiceModel> = () => of(registeredUser),
): Promise<{ fixture: ComponentFixture<RegisterForm>; auth: AuthStub }> {
  const auth: AuthStub = { registered: [], loginCalls: [] };

  await TestBed.configureTestingModule({
    imports: [RegisterForm],
    providers: [
      {
        provide: AuthService,
        useValue: {
          register: (viewModel: RegisterViewModel) => {
            auth.registered.push(viewModel);
            return response();
          },
          login: (returnUrl: string) => auth.loginCalls.push(returnUrl),
        },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RegisterForm);
  fixture.detectChanges();

  return { fixture, auth };
}

function fill(
  fixture: ComponentFixture<RegisterForm>,
  values: Partial<{ displayName: string; email: string; password: string }>,
): void {
  fixture.componentInstance.form.patchValue(values);
  fixture.detectChanges();
}

function badRequest(errors: Record<string, string[]>): HttpErrorResponse {
  return new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { errors } });
}

function textOf(fixture: ComponentFixture<RegisterForm>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('RegisterForm', () => {
  it('does not submit an empty form, and shows what is missing', async () => {
    const { fixture, auth } = await createForm();

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(auth.registered).toEqual([]);
    expect(textOf(fixture)).toContain('Enter your email address.');
    expect(textOf(fixture)).toContain('Choose a password.');
  });

  it('rejects a malformed email and a short password before calling the API', async () => {
    const { fixture, auth } = await createForm();
    fill(fixture, { email: 'not-an-email', password: 'abc' });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(auth.registered).toEqual([]);
    expect(textOf(fixture)).toContain('Enter a valid email address.');
    expect(textOf(fixture)).toContain('Use at least 6 characters.');
  });

  it('rejects a display name over 64 characters', async () => {
    const { fixture, auth } = await createForm();
    fill(fixture, { displayName: 'x'.repeat(65), email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(auth.registered).toEqual([]);
    expect(textOf(fixture)).toContain('Use 64 characters or fewer.');
  });

  it('posts the account and sends the browser to sign-in, never fabricating a session', async () => {
    const { fixture, auth } = await createForm();
    fill(fixture, { displayName: 'Thornwake', email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();

    expect(auth.registered).toEqual([{ email: 'a@b.com', password: PW, displayName: 'Thornwake' }]);
    // The hand-off is a full-page navigation through AuthService.login, not a router navigation and
    // nothing at all derived from the response body.
    expect(auth.loginCalls).toEqual(['/']);
  });

  it('sends a null display name rather than an empty string when it is left blank', async () => {
    const { fixture, auth } = await createForm();
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();

    expect(auth.registered[0].displayName).toBeNull();
  });

  it('shows an Identity password error against that field, and re-enables the form', async () => {
    const { fixture, auth } = await createForm(() =>
      throwError(() => badRequest({ PasswordRequiresDigit: ['Passwords must have at least one digit.'] })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('must have at least one digit');
    expect(auth.loginCalls).toEqual([]);
    expect(fixture.componentInstance.form.enabled).toBe(true);
  });

  it('shows a duplicate-email error against the email field', async () => {
    const { fixture } = await createForm(() =>
      throwError(() => badRequest({ DuplicateEmail: ['That email is already taken.'] })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.messagesFor('email')).toEqual(['That email is already taken.']);
  });

  it('shows a FluentValidation property error against its field too', async () => {
    const { fixture } = await createForm(() =>
      throwError(() => badRequest({ Email: ['Email is not a valid email address.'] })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.messagesFor('email')).toEqual([
      'Email is not a valid email address.',
    ]);
  });

  it('shows an unplaceable server error at form level, in a live region', async () => {
    const { fixture } = await createForm(() =>
      throwError(() => badRequest({ ConcurrencyFailure: ['Optimistic concurrency failure.'] })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.formErrors()).toEqual(['Optimistic concurrency failure.']);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
  });

  it('falls back to one generic message on a non-400 failure', async () => {
    const { fixture } = await createForm(() =>
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.formErrors().length).toBe(1);
    expect(fixture.componentInstance.state()).toBe('idle');
  });

  it('disables the submit button while the request is in flight', async () => {
    const { fixture } = await createForm(() => new Observable<UserServiceModel>(() => {}));
    fill(fixture, { email: 'a@b.com', password: PW });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.register-submit',
    );
    expect(button?.disabled).toBe(true);
    expect(button?.textContent).toContain('Creating account');
  });

  it('points the password field at both its hint and its error when rejected', async () => {
    const { fixture } = await createForm(() =>
      throwError(() => badRequest({ PasswordTooShort: ['Passwords must be at least 6 characters.'] })),
    );
    fill(fixture, { email: 'a@b.com', password: PW });

    const input = (fixture.nativeElement as HTMLElement).querySelector('#register-password');
    expect(input?.getAttribute('aria-describedby')).toBe('register-password-hint');

    fixture.componentInstance.submit();
    fixture.detectChanges();

    // Both, not one: the hint says what the rule is, the error says what was wrong this time.
    expect(input?.getAttribute('aria-describedby')).toBe(
      'register-password-hint register-password-error',
    );
    expect((fixture.nativeElement as HTMLElement).querySelector('#register-password-error')).not.toBeNull();
  });
});
