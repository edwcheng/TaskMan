import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import AppShell from '../components/AppShell.vue'
import { useAuthStore } from '../stores/auth'
import { safeReturnTo } from '../utils/returnUrl'
import LoginView from '../views/LoginView.vue'
import NotFoundView from '../views/NotFoundView.vue'
import PlaceholderView from '../views/PlaceholderView.vue'

// Phase 5 ships the shell, guards, and login; the placeholder views keep
// navigation functional until Phases 6–7 replace them with real screens.
const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: LoginView,
    meta: { public: true, title: 'Sign in' },
  },
  {
    path: '/',
    component: AppShell,
    children: [
      { path: '', redirect: { path: '/tasks' } },
      {
        path: 'tasks',
        name: 'tasks',
        component: PlaceholderView,
        props: { title: 'Tasks' },
        meta: { title: 'Tasks' },
      },
      {
        path: 'forums/:slug',
        name: 'forum-tasks',
        component: PlaceholderView,
        props: { title: 'Forum tasks' },
        meta: { title: 'Forum' },
      },
      {
        path: 'my-tasks',
        name: 'my-tasks',
        component: PlaceholderView,
        props: { title: 'My tasks' },
        meta: { title: 'My tasks' },
      },
      {
        path: 'tasks/new',
        name: 'new-task',
        component: PlaceholderView,
        props: { title: 'New task' },
        meta: { title: 'New task' },
      },
      {
        path: 'tasks/:id',
        name: 'task-detail',
        component: PlaceholderView,
        props: { title: 'Task' },
        meta: { title: 'Task' },
      },
      {
        path: 'tasks/:id/edit',
        name: 'task-edit',
        component: PlaceholderView,
        props: { title: 'Edit task' },
        meta: { title: 'Edit task' },
      },
      {
        path: 'admin/users',
        name: 'admin-users',
        component: PlaceholderView,
        props: { title: 'User management' },
        meta: { title: 'User management', admin: true },
      },
      {
        path: 'admin/forums',
        name: 'admin-forums',
        component: PlaceholderView,
        props: { title: 'Forum management' },
        meta: { title: 'Forum management', admin: true },
      },
      {
        path: 'profile',
        name: 'profile',
        component: PlaceholderView,
        props: { title: 'Profile' },
        meta: { title: 'Profile' },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: NotFoundView,
    meta: { title: 'Not found' },
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // Restore the session on first navigation (the SPA calls /auth/me on boot).
  if (auth.status === 'idle') {
    await auth.bootstrap()
  }

  // Already signed in? Send anonymous visitors home.
  if (to.meta.public === true) {
    if (auth.isAuthenticated) {
      return safeReturnTo(to.query.return) ?? '/'
    }

    return true
  }

  if (!auth.isAuthenticated) {
    return { path: '/login', query: to.fullPath === '/' ? {} : { return: to.fullPath } }
  }

  if (to.meta.admin === true && !auth.isAdmin) {
    return { path: '/tasks' }
  }

  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${String(to.meta.title)} · TaskMan` : 'TaskMan'
})

export default router
