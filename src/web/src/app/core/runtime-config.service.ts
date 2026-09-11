import { Injectable, signal } from '@angular/core';

export interface RuntimeConfig {
  gatewayUrl: string;
}

// Fetched from AegisScribe.Web's /config endpoint at startup rather than baked
// into environment.ts, so the same build artifact promotes across environments.
// See .claude/rules/gateway.md -> "The web host".
@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly _gatewayUrl = signal('');
  readonly gatewayUrl = this._gatewayUrl.asReadonly();

  async load(): Promise<void> {
    const response = await fetch('/config');
    if (!response.ok) {
      throw new Error(`Failed to load runtime config: ${response.status}`);
    }

    const config: RuntimeConfig = await response.json();
    this._gatewayUrl.set(config.gatewayUrl);
  }
}
