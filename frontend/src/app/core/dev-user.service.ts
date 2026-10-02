import { Injectable, signal } from '@angular/core';

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
  readonly current = signal<DevUser>(this.restore());

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
