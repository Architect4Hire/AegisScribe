import { Component, input } from '@angular/core';

export type StatusPillState = 'ok' | 'attention' | 'critical' | 'neutral';

@Component({
  imports: [],
  selector: 'scribe-status-pill',
  styleUrl: './status-pill.css',
  templateUrl: './status-pill.html',
})
export class StatusPill {
  readonly state = input.required<StatusPillState>();
  readonly label = input.required<string>();
}
