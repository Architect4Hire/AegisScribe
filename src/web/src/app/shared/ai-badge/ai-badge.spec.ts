import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AiBadge } from './ai-badge';

@Component({
  imports: [AiBadge],
  template: `<scribe-ai-badge>AI generated</scribe-ai-badge>`,
})
class HostComponent {}

describe('AiBadge', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renders the projected label inside the badge', () => {
    const badge = (fixture.nativeElement as HTMLElement).querySelector('.ai-badge');
    expect(badge?.textContent?.trim()).toBe('AI generated');
  });
});
