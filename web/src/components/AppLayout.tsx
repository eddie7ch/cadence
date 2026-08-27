import type { ReactNode } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';

const NAV = [
  { to: '/activities', label: 'Activities' },
  { to: '/trends', label: 'Trends' },
  { to: '/coaching', label: 'Coaching' },
] as const;

export function AppLayout(): ReactNode {
  const { athlete, signOut } = useAuth();

  return (
    <div className="shell">
      <header className="topbar">
        <div className="topbar__inner">
          <div className="brand">
            <span className="brand__mark" aria-hidden="true" />
            <span className="brand__name">Cadence</span>
          </div>

          <nav className="nav" aria-label="Main">
            {NAV.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) => (isActive ? 'nav__link nav__link--active' : 'nav__link')}
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="topbar__account">
            <span className="topbar__athlete" title={athlete?.email ?? ''}>
              {athlete?.displayName ?? ''}
            </span>
            <button type="button" className="button button--ghost button--small" onClick={signOut}>
              Sign out
            </button>
          </div>
        </div>
      </header>

      <main className="main">
        <Outlet />
      </main>
    </div>
  );
}
