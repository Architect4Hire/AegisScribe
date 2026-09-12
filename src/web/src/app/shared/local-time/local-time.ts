import { Component, computed, input } from '@angular/core';

// The calendar's hard problem: guilds are international, and an unlabelled time doesn't throw, it
// just assembles the wrong people at the wrong hour. So this always renders the tenant's zone by
// default, and the viewer's own zone is available on hover — both labelled, never a bare clock
// time. See .claude/rules/frontend.md -> "Times are the calendar's hard problem".
@Component({
  imports: [],
  selector: 'scribe-local-time',
  styleUrl: './local-time.css',
  templateUrl: './local-time.html',
})
export class LocalTime {
  readonly instant = input.required<string>();
  readonly tenantZone = input.required<string>();
  readonly viewerZone = input(Intl.DateTimeFormat().resolvedOptions().timeZone);

  private readonly date = computed(() => new Date(this.instant()));

  readonly tenantLabel = computed(() => this.formatIn(this.tenantZone()));

  readonly hoverLabel = computed(() => `${this.formatIn(this.viewerZone())} your time`);

  private formatIn(zone: string): string {
    return new Intl.DateTimeFormat(undefined, {
      timeZone: zone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZoneName: 'short',
    }).format(this.date());
  }
}
