import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
    title: '登入 · Vibe Maker',
  },
  {
    // 登入後直接進入主畫面（ChatGPT 式版面：側邊欄 + 對話）
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/home/new-chat-page').then((m) => m.NewChatPage),
        title: 'Vibe Maker',
      },
      {
        path: 'projects/:projectId',
        loadComponent: () => import('./features/projects/project-page').then((m) => m.ProjectPage),
        title: '專案 · Vibe Maker',
      },
      {
        path: 'c/:conversationId',
        loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPage),
        title: '對話 · Vibe Maker',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
