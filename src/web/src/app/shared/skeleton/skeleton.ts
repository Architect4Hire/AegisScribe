import { Component, computed, input } from '@angular/core';

@Component({
  imports: [],
  selector: 'scribe-skeleton',
  styleUrl: './skeleton.css',
  templateUrl: './skeleton.html',
})
export class Skeleton {
  readonly height = input.required<number>();
  readonly count = input(1);

  // Each bar down the stack fades a little further, floored at .5 — a hint that there may be more
  // below without pretending to know exactly how much. Matches the reference's own example bars.
  readonly opacities = computed(() =>
    Array.from({ length: this.count() }, (_, index) => Math.max(1 - index * 0.25, 0.5)),
  );
}
