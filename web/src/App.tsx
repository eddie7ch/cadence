import type { ReactNode } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { AppLayout } from './components/AppLayout';
import { RequireAuth } from './components/RequireAuth';
import { EmptyState } from './components/Ui';
import { ActivitiesPage } from './pages/ActivitiesPage';
import { ActivityDetailPage } from './pages/ActivityDetailPage';
import { AuthPage } from './pages/AuthPage';
import { CoachingPage } from './pages/CoachingPage';
import { TrendsPage } from './pages/TrendsPage';

export function App(): ReactNode {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/signin" element={<AuthPage />} />
          <Route
            element={
              <RequireAuth>
                <AppLayout />
              </RequireAuth>
            }
          >
            <Route path="/" element={<Navigate to="/activities" replace />} />
            <Route path="/activities" element={<ActivitiesPage />} />
            <Route path="/activities/:id" element={<ActivityDetailPage />} />
            <Route path="/trends" element={<TrendsPage />} />
            <Route path="/coaching" element={<CoachingPage />} />
          </Route>
          <Route
            path="*"
            element={<EmptyState title="Page not found" description="That route does not exist." />}
          />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
