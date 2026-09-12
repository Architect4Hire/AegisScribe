import { Component, input } from '@angular/core';

@Component({
  imports: [],
  selector: 'scribe-stat-tile',
  styleUrl: './stat-tile.css',
  templateUrl: './stat-tile.html',
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly sub = input<string>();
}
