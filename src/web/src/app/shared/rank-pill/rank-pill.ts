import { Component, input } from '@angular/core';

// A tenant rank, as design/aegisscribe-armory.html draws it (§07's <scribe-rank-pill>).
//
// The colour is TENANT CONFIG, not a design token: it arrives on the ServiceModel from whatever the
// community chose, and is surfaced as the --rank-color custom property the stylesheet reads. That is
// the one sanctioned way a non-token colour enters this app, and it is why the value is validated
// server-side as exactly #rrggbb — it lands in an inline style.
//
// Deliberately NOT used for the in-game rank. The game's rank and the community's are different
// facts and neither derives from the other (tenancy.md), so the roster renders the in-game one as
// plain mono text instead. Giving both the same pill would imply a relationship that does not exist.
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
