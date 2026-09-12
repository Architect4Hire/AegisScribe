import { Component, input, output } from '@angular/core';

export type EmptyStateVariant = 'empty' | 'error';

@Component({
  imports: [],
  selector: 'scribe-empty-state',
  styleUrl: './empty-state.scss',
  templateUrl: './empty-state.html',
})
export class EmptyState {
  readonly heading = input.required<string>();
  readonly body = input.required<string>();
  readonly actionLabel = input<string>();
  readonly variant = input<EmptyStateVariant>('empty');
  readonly action = output<void>();
}
