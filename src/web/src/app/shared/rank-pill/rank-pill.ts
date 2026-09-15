import { Component, input } from '@angular/core';

// A tenant rank, as the design reference draws it.
//
// The colour is TENANT CONFIG, not a design token — the one sanctioned way a non-token colour enters
// this app, surfaced as the --rank-color custom property. That is why the server validates it as
// exactly #rrggbb: it lands in an inline style.
//
// Deliberately NOT used for the in-game rank, which the roster renders as plain mono text — the two
// ranks are different facts (tenancy.md), and one pill for both would imply a relationship that does
// not exist.
@Component({
  imports: [],
  selector: 'scribe-rank-pill',
  styleUrl: './rank-pill.scss',
  templateUrl: './rank-pill.html',
})
export class RankPill {
  readonly name = input.required<string>();

  // Null falls back to --ink-3 in the stylesheet, matching the reference's own default. A rank
  // without a colour is a rank, not a broken pill.
  readonly colour = input<string | null>(null);
}
