// The backend's roles are Analyst/Supervisor (Auth/Roles.cs) - the UX brief talks about
// "advisor" and "reviewer" personas instead. They map 1:1: an advisor submits/fixes packets
// (exactly what CaseStateMachine's Analyst-gated transitions do), a reviewer decides on them
// (exactly what DecisionsController's Supervisor-only endpoint does). This file is the one
// place that mapping is written down.
export type Role = 'Analyst' | 'Supervisor';

export function isAdvisor(role: Role): boolean {
  return role === 'Analyst';
}

export function isReviewer(role: Role): boolean {
  return role === 'Supervisor';
}
