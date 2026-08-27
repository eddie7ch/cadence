import { createContext } from 'react';
import type { RegisterPayload, SignInPayload } from '../api/client';
import type { AthleteDto } from '../api/types';

export interface AuthContextValue {
  athlete: AthleteDto | null;
  isAuthenticated: boolean;
  signIn: (payload: SignInPayload) => Promise<void>;
  register: (payload: RegisterPayload) => Promise<void>;
  signOut: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
