import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { RuntimeConfigService } from './core/runtime-config.service';

@Component({
  imports: [RouterOutlet],
  selector: 'scribe-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('web');
  protected readonly gatewayUrl = inject(RuntimeConfigService).gatewayUrl;
}
