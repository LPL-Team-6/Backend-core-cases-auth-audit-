import { Injectable, effect, inject, signal } from '@angular/core';

import { CaseApiService } from './case-api.service';

export interface DevUser {
  username: string;
  displayName: string;
  firmId: string;
  role: 'Analyst' | 'Supervisor';
}

// Mirrors the backend's seeded dev identities exactly (Auth/DevUserStore.cs). The backend is
// the only source of truth for who can do what - this list just lets a demo user switch the
// X-Dev-User header from the UI instead of curling the API by hand.
export const DEV_USERS: readonly DevUser[] = [
  { username: 'analyst1', displayName: 'Analyst One', firmId: 'FIRM-A', role: 'Analyst' },
  { username: 'analyst2', displayName: 'Analyst Two', firmId: 'FIRM-B', role: 'Analyst' },
  { username: 'supervisor', displayName: 'Supervisor', firmId: 'FIRM-A', role: 'Supervisor' },
];

const STORAGE_KEY = 'caseauth.devUser';

@Injectable({ providedIn: 'root' })
export class DevUserService {
  private readonly caseApi = inject(CaseApiService);

  readonly current = signal<DevUser>(this.restore());

  // The backend's own user id (e.g. "u-analyst1"), used to filter "my cases" by
  // Case.createdByUserId. Fetched from /api/me rather than hardcoded here, so this file never
  // has to know the backend's internal id scheme. Null until that first call resolves -
  // consumers (e.g. the queue's "my cases" filter) treat null as "don't filter yet".
  readonly userId = signal<string | null>(null);

  constructor() {
    effect(() => {
      // Re-fetch whenever the selected identity changes - current() is read here so the
      // effect re-runs on every switch, not just once at startup.
      this.current();
      this.caseApi.me().subscribe({
        next: (me) => this.userId.set(me.userId ?? null),
        error: () => this.userId.set(null),
      });
    });
  }

  setUser(username: string): void {
    const user = DEV_USERS.find((u) => u.username === username);
    if (!user) {
      return;
    }
    this.current.set(user);
    try {
      localStorage.setItem(STORAGE_KEY, user.username);
    } catch {
      // Private-browsing/blocked storage: the selection just won't survive a reload.
    }
  }

  private restore(): DevUser {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      const match = saved && DEV_USERS.find((u) => u.username === saved);
      if (match) {
        return match;
      }
    } catch {
      // Fall through to the default below.
    }
    return DEV_USERS[0];
  }
}
