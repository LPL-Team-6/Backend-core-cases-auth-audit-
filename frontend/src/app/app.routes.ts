import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'cases',
  },
  {
    path: 'cases',
    loadComponent: () => import('./features/case-queue/case-queue.component').then((m) => m.CaseQueueComponent),
    title: 'Case queue',
  },
  {
    path: 'cases/:caseId',
    loadComponent: () => import('./features/case-detail/case-detail.component').then((m) => m.CaseDetailComponent),
    title: 'Case detail',
  },
];
