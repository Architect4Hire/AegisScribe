import { Component } from '@angular/core';

// No inputs by design — the label text ("AI generated" / "AI interpreted" / "AI analysis") is
// content the caller projects, so the marker itself never has an "off" state to forget to set.
@Component({
  imports: [],
  selector: 'scribe-ai-badge',
  styleUrl: './ai-badge.css',
  templateUrl: './ai-badge.html',
})
export class AiBadge {}
