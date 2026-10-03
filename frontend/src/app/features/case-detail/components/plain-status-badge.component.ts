import { Component, computed, input } from '@angular/core';

import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';
import { plainStatus } from '../../../core/plain-status';

@Component({
  selector: 'app-plain-status-badge',
  template: `
    <span class="badge" [class]="info().badgeClass" [title]="'Internal status: ' + status()">{{ info().label }}</span>
  `,
})
export class PlainStatusBadgeComponent {
  readonly status = input.required<CaseStatus>();
  readonly info = computed(() => plainStatus(this.status()));
}
