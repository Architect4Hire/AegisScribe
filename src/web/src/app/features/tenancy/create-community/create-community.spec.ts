import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { CurrentUserService } from '../../../core/current-user.service';
import { TenantService } from '../../../core/tenant.service';
import { SlugCheckServiceModel, TenantServiceModel } from '../../../models/auth.models';
import { CreateCommunity } from './create-community';

// Longer than SLUG_CHECK_DEBOUNCE_MS in the component.
const PAST_DEBOUNCE = 400;

const available: SlugCheckServiceModel = {
  slug: 'ashes-of-dawn',
  available: true,
  reason: 'Available',
};
const taken: SlugCheckServiceModel = { slug: 'ashes-of-dawn', available: false, reason: 'Taken' };

const createdTenant: TenantServiceModel = {
  id: 't1',
  slug: 'ashes-of-dawn',
  name: 'Ashes of Dawn',
  timeZoneId: 'UTC',
  // A brand-new community's door starts shut: an owner opens it deliberately.
  acceptsJoinRequests: false,
};

function httpError(status: number, error?: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

describe('CreateCommunity', () => {
  let fixture: ComponentFixture<CreateCommunity>;
  let component: CreateCommunity;
  let checkSlug: ReturnType<
    typeof vi.fn<(query: { name: string } | { slug: string }) => Observable<SlugCheckServiceModel>>
  >;
  let create: ReturnType<typeof vi.fn<() => Observable<TenantServiceModel>>>;
  let clear: ReturnType<typeof vi.fn<() => void>>;

  // The stubs are created here and reconfigured per test with mockReturnValue, because TestBed setup
  // is async and the timer control below is not.
  beforeEach(async () => {
    checkSlug = vi.fn(() => of(available));
    create = vi.fn(() => of(createdTenant));
    clear = vi.fn();

    await TestBed.configureTestingModule({
      imports: [CreateCommunity],
      providers: [
        provideRouter([]),
        { provide: TenantService, useValue: { checkSlug, create } },
        { provide: CurrentUserService, useValue: { clear } },
      ],
    }).compileComponents();
  });

  // Vitest's fake timers rather than Angular's fakeAsync/tick: the unit-test target loads `zone.js`
  // but not `zone.js/testing`, so there is no ProxyZone for fakeAsync to run in. Faking the timers
  // debounceTime schedules on gets the same control without changing the project's test polyfills.
  // Enabled after the async setup above, so compileComponents' own promises are unaffected.
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function render(): void {
    fixture = TestBed.createComponent(CreateCommunity);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('asks the server for a slug once typing settles, not per keystroke', () => {
    render();

    component.form.controls.name.setValue('Ash');
    component.form.controls.name.setValue('Ashes');
    component.form.controls.name.setValue('Ashes of Dawn');
    expect(checkSlug).not.toHaveBeenCalled();

    vi.advanceTimersByTime(PAST_DEBOUNCE);

    expect(checkSlug).toHaveBeenCalledTimes(1);
    expect(checkSlug).toHaveBeenCalledWith({ name: 'Ashes of Dawn' });
  });

  it('renders the slug the server derived, not one built here', () => {
    checkSlug.mockReturnValue(
      of({ slug: 'server-said-this', available: true, reason: 'Available' }),
    );
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    fixture.detectChanges();

    expect(component.previewSlug).toBe('server-said-this');
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '#community-slug',
    );
    expect(input?.value).toBe('server-said-this');
    expect(input?.readOnly).toBe(true);
  });

  it('says so when an address is taken, without naming who holds it', () => {
    checkSlug.mockReturnValue(of(taken));
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    fixture.detectChanges();

    expect(component.slugMessage).toContain('already in use');
  });

  it('drops the availability tint while a re-check is in flight', () => {
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    fixture.detectChanges();

    const hint = () => (fixture.nativeElement as HTMLElement).querySelector('#community-slug-hint');
    expect(hint()?.classList.contains('is-available')).toBe(true);

    // A new answer is pending, so the old one's colour must go with it: check() still holds the
    // previous result, and a green tint under "Checking…" is colour contradicting the text.
    checkSlug.mockReturnValue(new Observable<SlugCheckServiceModel>(() => {}));
    component.form.controls.name.setValue('Something Else');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    fixture.detectChanges();

    expect(component.checkState()).toBe('checking');
    expect(hint()?.classList.contains('is-available')).toBe(false);
    expect(hint()?.classList.contains('is-unavailable')).toBe(false);
    expect(hint()?.textContent).toContain('Checking');
  });

  it('keeps the form usable when the availability check fails', () => {
    checkSlug.mockReturnValue(throwError(() => new Error('offline')));
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);

    // A hint that cannot be fetched must not become a dead end: the server decides at submit time.
    expect(component.checkState()).toBe('unavailable');
    expect(component.slugMessage).toContain('still continue');
    expect(component.form.controls.name.valid).toBe(true);
  });

  it('stops deriving from the name once the user edits the address', () => {
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    checkSlug.mockClear();

    component.editSlug();
    expect(component.form.controls.slug.value).toBe('ashes-of-dawn');

    component.form.controls.name.setValue('A Different Name');
    vi.advanceTimersByTime(PAST_DEBOUNCE);

    expect(checkSlug).not.toHaveBeenCalled();
  });

  it('checks the edited address against ?slug=, not ?name=', () => {
    render();

    component.editSlug();
    checkSlug.mockClear();
    component.form.controls.slug.setValue('my-own-address');
    vi.advanceTimersByTime(PAST_DEBOUNCE);

    expect(checkSlug).toHaveBeenCalledTimes(1);
    expect(checkSlug).toHaveBeenCalledWith({ slug: 'my-own-address' });
  });

  it('omits the slug when it was derived, so the server derives it again', () => {
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledWith({
      name: 'Ashes of Dawn',
      timeZoneId: component.detectedTimeZone,
      slug: null,
    });
  });

  it('sends the edited slug when the user took it over', () => {
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.editSlug();
    component.form.controls.slug.setValue('my-own-address');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(create).toHaveBeenCalledWith({
      name: 'Ashes of Dawn',
      timeZoneId: component.detectedTimeZone,
      slug: 'my-own-address',
    });
  });

  it('navigates to the new community on success rather than storing a current tenant', () => {
    render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith(['/t', 'ashes-of-dawn']);
  });

  it('drops the cached memberships before navigating, so the tenant guard sees the new community', () => {
    render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(clear).toHaveBeenCalledTimes(1);
    expect(clear.mock.invocationCallOrder[0]).toBeLessThan(navigate.mock.invocationCallOrder[0]);
  });

  it('flags the slug field and unlocks it on a 409, keeping every other field', () => {
    create.mockReturnValue(
      throwError(() =>
        httpError(409, { type: 'https://api.aegisscribe.com/problems/tenant-slug-taken' }),
      ),
    );
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    component.form.controls.timeZoneId.setValue('Europe/Berlin');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();
    fixture.detectChanges();

    // Unlocked, because a user cannot act on "taken" while the box is read-only.
    expect(component.slugMode()).toBe('edited');
    expect(component.slugErrors()[0]).toContain('claimed this address');

    // Everything they typed survives.
    expect(component.form.controls.name.value).toBe('Ashes of Dawn');
    expect(component.form.controls.timeZoneId.value).toBe('Europe/Berlin');
    expect(component.state()).toBe('idle');
    expect(component.form.enabled).toBe(true);
  });

  it('routes a 400 on the slug to the slug field and unlocks it too', () => {
    create.mockReturnValue(
      throwError(() => httpError(400, { errors: { Slug: ['This slug is already taken.'] } })),
    );
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(component.slugMode()).toBe('edited');
    expect(component.slugErrors()).toEqual(['This slug is already taken.']);
  });

  it('routes a 400 on the time zone to its own field', () => {
    create.mockReturnValue(
      throwError(() =>
        httpError(400, {
          errors: { TimeZoneId: ["'Time Zone Id' is not a recognized time zone id."] },
        }),
      ),
    );
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(component.timeZoneErrors()[0]).toContain('not a recognized time zone');
    expect(component.slugMode()).toBe('derived');
  });

  it('shows a form-level error for a failure it cannot place on a field', () => {
    create.mockReturnValue(throwError(() => httpError(500)));
    render();

    component.form.controls.name.setValue('Ashes of Dawn');
    vi.advanceTimersByTime(PAST_DEBOUNCE);
    component.submit();

    expect(component.formErrors()[0]).toContain('could not create your community');
  });

  it('does not submit an empty form, and marks it so the user can see why', () => {
    render();

    component.submit();

    expect(create).not.toHaveBeenCalled();
    expect(component.nameErrors()).toEqual(['Give your community a name.']);
  });

  it('defaults the time zone to the browser and offers it in the list', () => {
    render();

    expect(component.form.controls.timeZoneId.value).toBe(component.detectedTimeZone);
    expect(component.timeZones).toContain(component.detectedTimeZone);
  });
});
