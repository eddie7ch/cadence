import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';

export function RequireAuth({ children }: { children: ReactNode }): ReactNode {
  const { isAuthenticated } = useAuth();
  const location = useLocation();

  if (!isAuthenticated) {
    // `from` lets the sign-in screen send the athlete back to the deep link they
    // opened, which matters because the nginx SPA fallback makes those work.
    return <Navigate to="/signin" replace state={{ from: location.pathname + location.search }} />;
  }

  return <>{children}</>;
}
