import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
    title: '登入 · Vibe Maker',
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/workspaces/workspaces-page').then((m) => m.WorkspacesPage),
        title: 'Workspaces · Vibe Maker',
      },
      {
        path: 'workspaces/:workspaceId',
        loadComponent: () =>
          import('./features/workspaces/workspace-page').then((m) => m.WorkspacePage),
        title: 'Workspace · Vibe Maker',
      },
      {
        path: 'conversations/:conversationId',
        loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPage),
        title: '對話 · Vibe Maker',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
