import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { TenantService } from '../../../core/tenant.service';
import { SlugCheckServiceModel } from '../../../models/auth.models';

// Mirrors TenantSlugRules in the Domain project, held here only to stop the form submitting something
// the server will certainly reject. The authoritative rules, the derivation and the reserved list all
// live server-side and reach this screen through GET /tenants/slug-check.
const MAX_NAME_LENGTH = 200;
const MIN_SLUG_LENGTH = 3;
const MAX_SLUG_LENGTH = 64;

// Long enough that typing a name does not fire a request per keystroke, short enough that the slug
// appears while the user is still looking at the field.
const SLUG_CHECK_DEBOUNCE_MS = 350;

// 'derived' — the slug is whatever the server makes of the name, and its input is read-only.
// 'edited'  — the user has taken it over, and the name no longer drives it.
type SlugMode = 'derived' | 'edited';

// The design system's four states are for data-bound components. This form binds no external data that
// can go stale, so there is deliberately NO degraded state: the slug check is a live hint, not data we
// hold, and its failure path is 'unavailable'.
type SubmitState = 'idle' | 'submitting';
type CheckState = 'idle' | 'checking' | 'answered' | 'unavailable';

@Component({
  imports: [ReactiveFormsModule],
  selector: 'scribe-create-community',
  styleUrl: './create-community.scss',
  templateUrl: './create-community.html',
})
export class CreateCommunity {
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly tenants = inject(TenantService);

  readonly maxNameLength = MAX_NAME_LENGTH;
  readonly minSlugLength = MIN_SLUG_LENGTH;
  readonly maxSlugLength = MAX_SLUG_LENGTH;

  readonly slugMode = signal<SlugMode>('derived');
  readonly checkState = signal<CheckState>('idle');
  readonly check = signal<SlugCheckServiceModel | null>(null);
  readonly state = signal<SubmitState>('idle');
  readonly formErrors = signal<string[]>([]);

  // Held here rather than in the control's own errors: flagging the slug also unlocks the field, which
  // swaps one input for another, and the incoming formControlName directive re-runs
  // updateValueAndValidity on registration — wiping anything set with setErrors(). In a signal, the
  // message survives that swap instead of flashing and vanishing.
  readonly slugServerErrors = signal<string[]>([]);

  // Intl is the only source for these. A curated zone list in the repo is data we would be inventing,
  // and it would drift from what TimeZoneInfo on the server actually accepts.
  readonly detectedTimeZone = detectTimeZone();
  readonly timeZones = supportedTimeZones(this.detectedTimeZone);

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
    slug: ['', [Validators.minLength(MIN_SLUG_LENGTH), Validators.maxLength(MAX_SLUG_LENGTH)]],
    timeZoneId: [this.detectedTimeZone, [Validators.required]],
  });

  constructor() {
    // switchMap rather than mergeMap is load-bearing: it cancels the superseded request, so a slower
    // answer for an earlier keystroke can never overwrite a newer one and leave the field showing a
    // slug the user has already typed past.
    this.form.controls.name.valueChanges
      .pipe(
        debounceTime(SLUG_CHECK_DEBOUNCE_MS),
        distinctUntilChanged(),
        switchMap((name) => this.checkFor({ name })),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.applyCheck(result));

    this.form.controls.slug.valueChanges
      .pipe(
        debounceTime(SLUG_CHECK_DEBOUNCE_MS),
        distinctUntilChanged(),
        switchMap((slug) => this.checkFor({ slug })),
        takeUntilDestroyed(),
      )
      .subscribe((result) => this.applyCheck(result));
  }

  // Always the server's answer, never a local derivation — so until the first check returns there is
  // nothing to show.
  get previewSlug(): string | null {
    if (this.slugMode() === 'edited') {
      return this.form.controls.slug.value || null;
    }

    return this.check()?.slug ?? null;
  }

  // Seeded with what the server derived, so Edit is a starting point rather than a blank box.
  editSlug(): void {
    if (this.slugMode() === 'edited') {
      return;
    }

    this.slugMode.set('edited');

    // emitEvent: false because we already know this exact slug's availability — it is the answer being
    // displayed. Letting the seed fire the pipeline would spend a request, and a rate-limit permit, to
    // be told the same thing again.
    this.form.controls.slug.setValue(this.check()?.slug ?? '', { emitEvent: false });
  }

  useDerivedSlug(): void {
    this.slugMode.set('derived');
    this.form.controls.slug.reset('', { emitEvent: false });
    this.slugServerErrors.set([]);
    this.check.set(null);
    this.checkState.set('idle');

    // Re-ask for the current name: the answer we last showed was about the slug they were editing.
    const name = this.form.controls.name.value.trim();
    if (name.length > 0) {
      this.checkState.set('checking');
      this.tenants
        .checkSlug({ name })
        .pipe(
          catchError(() => {
            this.checkState.set('unavailable');
            return of(null);
          }),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe((result) => this.applyCheck(result));
    }
  }

  // Every branch names the address or says why there is not one; none names the community holding a
  // taken slug, because the API does not either.
  get slugMessage(): string | null {
    if (this.checkState() === 'checking') {
      return 'Checking…';
    }
    if (this.checkState() === 'unavailable') {
      return 'We could not check that address just now. You can still continue — we will confirm when you create.';
    }

    const result = this.check();
    if (result === null) {
      return null;
    }
    if (result.available) {
      return 'This address is available.';
    }

    switch (result.reason) {
      case 'Taken':
        return 'This address is already in use. Try another.';
      case 'Reserved':
        return 'This address is reserved. Try another.';
      case 'Invalid':
        return 'Use lower-case letters, numbers and single hyphens.';
      case 'NotDerivable':
        return 'We could not build an address from that name — type one yourself.';
      default:
        // A reason added to the API after this build shipped. `available` is still trustworthy, so say
        // the only thing we actually know (api-contract.md).
        return 'This address is not available. Try another.';
    }
  }

  nameErrors(): string[] {
    return this.messagesFor('name', {
      required: 'Give your community a name.',
      maxlength: `Use ${MAX_NAME_LENGTH} characters or fewer.`,
    });
  }

  slugErrors(): string[] {
    // The server's rejection wins: re-stating our own client-side guess beside it would read as two
    // different problems.
    const fromServer = this.slugServerErrors();
    if (fromServer.length > 0) {
      return fromServer;
    }

    return this.messagesFor('slug', {
      minlength: `Use at least ${MIN_SLUG_LENGTH} characters.`,
      maxlength: `Use ${MAX_SLUG_LENGTH} characters or fewer.`,
    });
  }

  timeZoneErrors(): string[] {
    return this.messagesFor('timeZoneId', { required: 'Choose a time zone.' });
  }

  submit(): void {
    this.formErrors.set([]);
    this.slugServerErrors.set([]);

    if (this.form.invalid) {
      // Nothing shows until a control is touched, so a submit with empty fields would otherwise look
      // like a dead button.
      this.form.markAllAsTouched();
      return;
    }

    this.state.set('submitting');
    this.form.disable({ emitEvent: false });

    const { name, slug, timeZoneId } = this.form.getRawValue();

    // A null slug asks the server to derive one. The check result is deliberately NOT consulted: it was
    // a hint, and the authority is the unique index a moment from now.
    this.tenants
      .create({ name: name.trim(), timeZoneId, slug: this.slugMode() === 'edited' ? slug : null })
      .subscribe({
        // Navigation, not a stored "current tenant" — there is no such variable, and one would drift
        // from the URL (frontend.md → "Tenant context is part of the chrome").
        next: (tenant) => void this.router.navigate(['/t', tenant.slug]),
        error: (error: unknown) => {
          this.state.set('idle');
          this.form.enable({ emitEvent: false });
          this.applyServerErrors(error);
        },
      });
  }

  private messagesFor(name: ControlName, byError: Record<string, string>): string[] {
    const control = this.form.controls[name];
    if (!control.touched || control.valid) {
      return [];
    }

    const errors = control.errors ?? {};

    // Server messages win when present, for the same reason as in slugErrors().
    if (Array.isArray(errors['server'])) {
      return errors['server'] as string[];
    }

    return Object.keys(byError)
      .filter((key) => errors[key])
      .map((key) => byError[key]);
  }

  private checkFor(
    query: { name: string } | { slug: string },
  ): Observable<SlugCheckServiceModel | null> {
    const value = ('name' in query ? query.name : query.slug).trim();

    // Nothing to ask about yet, and asking would spend a rate-limit permit to be told so.
    if (value.length === 0) {
      this.check.set(null);
      this.checkState.set('idle');
      return of(null);
    }

    // The name drives the address only while the user has not taken it over, and vice versa.
    const mode = this.slugMode();
    if (('name' in query && mode === 'edited') || ('slug' in query && mode === 'derived')) {
      return of(null);
    }

    // A new question is being asked, so the last rejection is no longer about what is on screen.
    this.slugServerErrors.set([]);

    this.checkState.set('checking');
    return this.tenants.checkSlug('name' in query ? { name: value } : { slug: value }).pipe(
      // A failed check must not become a dead end: availability is a hint, the server decides at submit
      // time, so the form stays usable and says so.
      catchError(() => {
        this.checkState.set('unavailable');
        return of(null);
      }),
    );
  }

  private applyCheck(result: SlugCheckServiceModel | null): void {
    if (result === null) {
      return;
    }

    this.check.set(result);
    this.checkState.set('answered');
  }

  // 400 is the ordinary rejection — a slug the pre-check found taken, an unrecognised time zone. 409 is
  // the race the pre-check cannot close: same field, but it also unlocks the input, because a user
  // cannot act on "taken" while the box is read-only.
  private applyServerErrors(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      this.formErrors.set([GENERIC_ERROR]);
      return;
    }

    if (error.status === 409) {
      this.flagSlug([SLUG_RACE_MESSAGE]);
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

      if (control === 'slug') {
        this.flagSlug(messages);
        continue;
      }

      this.form.controls[control].setErrors({ server: messages });
      this.form.controls[control].markAsTouched();
    }

    this.formErrors.set(unplaceable);
  }

  // A slug rejection the user cannot reach is not one they can fix, so flagging it opens the field.
  private flagSlug(messages: string[]): void {
    this.editSlug();
    this.form.controls.slug.markAsTouched();
    this.slugServerErrors.set(messages);
  }
}

const GENERIC_ERROR = 'We could not create your community. Please try again.';
const SLUG_RACE_MESSAGE =
  'Another community claimed this address a moment ago. Choose a different one.';

type ControlName = 'name' | 'slug' | 'timeZoneId';

function controlFor(key: string): ControlName | null {
  const normalized = key.toLowerCase();

  if (normalized.includes('slug')) {
    return 'slug';
  }
  if (normalized.includes('timezone')) {
    return 'timeZoneId';
  }
  if (normalized.includes('name')) {
    return 'name';
  }

  return null;
}

// UTC is the fallback rather than a guess at the user's region: it is the one zone that is never wrong
// about itself, and the select lets them correct it.
function detectTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

function supportedTimeZones(detected: string): string[] {
  try {
    const supported = Intl.supportedValuesOf('timeZone');
    return supported.includes(detected) ? supported : [detected, ...supported];
  } catch {
    // No supportedValuesOf in this engine: offer the detected zone rather than a hardcoded list of our
    // own invention.
    return [detected];
  }
}
