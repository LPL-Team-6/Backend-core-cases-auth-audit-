import { Component, inject } from '@angular/core';

import { DEV_USERS, DevUserService } from './dev-user.service';
import { isAdvisor } from './role-view';

@Component({
  selector: 'app-dev-user-switcher',
  template: `
    <label class="dev-user-switcher">
      <span>Signed in as</span>
      <select
        [value]="devUser.current().username"
        (change)="devUser.setUser($any($event.target).value)"
      >
        @for (user of users; track user.username) {
          <option [value]="user.username">
            {{ user.displayName }} · {{ user.firmId }} · {{ isAdvisor(user.role) ? 'Advisor' : 'Reviewer' }}
          </option>
        }
      </select>
    </label>
  `,
  styles: `
    .dev-user-switcher {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.9rem;
      color: var(--color-text-muted);
    }

    select {
      padding: 0.35rem 0.5rem;
      border-radius: var(--radius);
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      color: var(--color-text);
    }
  `,
})
export class DevUserSwitcherComponent {
  readonly devUser = inject(DevUserService);
  readonly users = DEV_USERS;
  readonly isAdvisor = isAdvisor;
}
