import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, switchMap, throwError } from 'rxjs';
import { CharacterService } from '../../../core/character.service';
import { REGIONS, toRealmSlug } from '../../../core/realm';
import { RosterService } from '../../../core/roster.service';

// Which half of the two-step add failed, so the message names the right thing: a lookup 404 is "no
// such character", an add 409 is "already on the roster".
type Step = 'lookup' | 'add';

class StepError {
  constructor(
    readonly step: Step,
    readonly cause: unknown,
  ) {}
}

// The manual path onto the roster — the one that still works with no guild linked and no Blizzard
// credentials (8.5: no step is a dead end).
//
// Two calls, both through the ordinary stack. The character lookup is the cache-first global read: it
// answers from the stored row and only reaches Blizzard when the row is missing or stale, under the
// global limiter rather than this community's budget. The add then takes the character's id, which is
// why the lookup comes first — the roster endpoint deliberately refuses to fetch anything itself.
//
// Rendered for officers only by the roster; the TenantOfficer policy on POST /roster is what refuses.
@Component({
  imports: [],
  selector: 'scribe-add-character',
  styleUrl: './add-character.scss',
  templateUrl: './add-character.html',
})
export class AddCharacter {
  private readonly characterService = inject(CharacterService);
  private readonly rosterService = inject(RosterService);
  private readonly destroyRef = inject(DestroyRef);

  readonly tenantSlug = input.required<string>();
  // Open from the start on an empty roster, where adding somebody is the only thing to do.
  readonly startOpen = input(false);

  // Emits the character's name once it is on the roster, so the roster can reload.
  readonly added = output<string>();

  readonly regions = REGIONS;

  readonly region = signal<string>('us');
  readonly realm = signal('');
  readonly name = signal('');
  readonly adding = signal(false);
  readonly notice = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  readonly canAdd = computed(
    () => !this.adding() && toRealmSlug(this.realm()) !== '' && this.name().trim() !== '',
  );

  // Opens itself when the roster turns out to be empty, and never closes itself: the reload after a
  // successful add briefly passes through loading, and snapping the panel shut under an officer who is
  // adding a whole raid team one by one would be hostile.
  readonly expanded = signal(false);

  constructor() {
    effect(() => {
      if (this.startOpen()) {
        this.expanded.set(true);
      }
    });
  }

  add(): void {
    if (!this.canAdd()) {
      return;
    }

    const region = this.region();
    const realmSlug = toRealmSlug(this.realm());
    const name = this.name().trim();

    this.adding.set(true);
    this.notice.set(null);
    this.error.set(null);

    this.characterService
      .getCharacter(region, realmSlug, name)
      .pipe(
        catchError((cause: unknown) => throwError(() => new StepError('lookup', cause))),
        switchMap((character) =>
          this.rosterService
            .addEntry(this.tenantSlug(), { characterId: character.id, tenantRankId: null })
            .pipe(catchError((cause: unknown) => throwError(() => new StepError('add', cause)))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.adding.set(false);
          // Realm and region stay: officers usually add several people from the same realm.
          this.name.set('');
          this.notice.set(`Added ${name} to the roster.`);
          this.added.emit(name);
        },
        // The typed values stay in the form, so a typo is a quick fix rather than a retype.
        error: (failure: unknown) => {
          this.adding.set(false);
          this.error.set(failureMessage(failure, name, realmSlug, region));
        },
      });
  }
}

function failureMessage(failure: unknown, name: string, realmSlug: string, region: string): string {
  const step = failure instanceof StepError ? failure.step : 'add';
  const cause = failure instanceof StepError ? failure.cause : failure;
  const status = cause instanceof HttpErrorResponse ? cause.status : 0;

  if (step === 'lookup') {
    switch (status) {
      case 404:
        return `No character called ${name} on ${realmSlug} (${region.toUpperCase()}). Check the spelling and the realm.`;
      case 400:
        return 'Check the realm and character name, then try again.';
      case 429:
        return 'Too many character lookups just now. Wait a moment and try again.';
      default:
        return `Couldn't look up ${name}. Try again.`;
    }
  }

  switch (status) {
    case 409:
      return `${name} is already on the roster.`;
    case 404:
      return `${name} couldn't be found any more. Try again.`;
    default:
      return `Couldn't add ${name} to the roster. Try again.`;
  }
}
