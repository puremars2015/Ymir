import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/chat/dev-chat').then((m) => m.DevChat),
    title: 'Vibe Maker',
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/login/login-placeholder').then((m) => m.LoginPlaceholder),
    title: '登入 · Vibe Maker',
  },
  { path: '**', redirectTo: '' },
];
