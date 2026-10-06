import { Routes } from '@angular/router';
import { adminGuard, authGuard, signedInGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
    title: '登入 · Vibe Maker',
  },
  {
    // 本機帳號必須先改密碼（ADR-0009）；放在主畫面之外，避免主畫面呼叫其他 API。
    path: 'change-password',
    canActivate: [signedInGuard],
    loadComponent: () =>
      import('./features/login/change-password-page').then((m) => m.ChangePasswordPage),
    title: '變更密碼 · Vibe Maker',
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
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings-page').then((m) => m.SettingsPage),
        title: '個人設定 · Vibe Maker',
      },
      {
        path: 'admin/users',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/users-page').then((m) => m.UsersPage),
        title: '使用者管理 · Vibe Maker',
      },
      {
        path: 'admin/make-topics',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/make-topics-page').then((m) => m.MakeTopicsPage),
        title: 'Make 主題 · Vibe Maker',
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
