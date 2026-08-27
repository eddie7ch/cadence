import { useCallback, useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { api, type RegisterPayload, type SignInPayload } from '../api/client';
import { clearSession, getSession, setSession, subscribe, type Session } from '../api/session';
import { AuthContext, type AuthContextValue } from './AuthContext';

/**
 * The session lives in the api layer because the client has to clear it on a
 * 401 without reaching into React. useSyncExternalStore keeps the tree in step
 * with that store, so an expired token signs the user out from anywhere.
 */
export function AuthProvider({ children }: { children: ReactNode }): ReactNode {
  const session = useSyncExternalStore<Session | null>(subscribe, getSession, getSession);

  const signIn = useCallback(async (payload: SignInPayload) => {
    const response = await api.auth.signIn(payload);
    setSession({ accessToken: response.accessToken, athlete: response.athlete });
  }, []);

  const register = useCallback(async (payload: RegisterPayload) => {
    const response = await api.auth.register(payload);
    setSession({ accessToken: response.accessToken, athlete: response.athlete });
  }, []);

  const signOut = useCallback(() => {
    clearSession();
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      athlete: session?.athlete ?? null,
      isAuthenticated: session !== null,
      signIn,
      register,
      signOut,
    }),
    [session, signIn, register, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
