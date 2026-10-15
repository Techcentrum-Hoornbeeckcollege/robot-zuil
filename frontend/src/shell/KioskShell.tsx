import type { ReactNode } from 'react';
import type { HealthLevel } from '../api/types';
import { HealthDot } from './HealthDot';
import type { View } from './viewState';

interface Props {
  view: View;
  showAgendaNav: boolean;
  health: HealthLevel | null;
  onNavigate: (view: View) => void;
  children: ReactNode;
}

interface NavItem {
  label: string;
  kind: View['kind'];
  target: View;
}

const ITEMS: readonly NavItem[] = [
  { label: 'Zoeken', kind: 'search', target: { kind: 'search' } },
  { label: 'Straks', kind: 'upcoming', target: { kind: 'upcoming' } },
  { label: "Alle agenda's", kind: 'board', target: { kind: 'board' } },
];

/**
 * Settings sits apart from the agenda tabs: it is for staff, not visitors, and
 * it leads to a login rather than to a screen.
 */
const SETTINGS: NavItem = {
  label: 'Instellingen',
  kind: 'login',
  target: { kind: 'login' },
};

/**
 * The chrome around every screen: one page, with a nav bar instead of a reload.
 *
 * The bar is hidden on the attract screen, which is a single full-bleed button
 * and must not have anything else to tap, and in the admin panel, which has its
 * own tabs and its own way out.
 */
export function KioskShell({ view, showAgendaNav, health, onNavigate, children }: Props) {
  const chromeless = view.kind === 'attract' || view.kind === 'login' || view.kind === 'admin';

  const items = showAgendaNav
    ? ITEMS
    : ITEMS.filter((item) => item.kind === 'search');

  const showNav = !chromeless;

  return (
    <main className={chromeless ? 'app' : 'app app-with-nav'}>
      <div className="app-body">{children}</div>

      {showNav && (
        <nav className="nav">
          {items.map((item) => (
            <button
              key={item.kind}
              type="button"
              className={item.kind === view.kind ? 'nav-button nav-button-active' : 'nav-button'}
              onClick={() => onNavigate(item.target)}
            >
              {item.label}
            </button>
          ))}

          <button
            type="button"
            className="nav-button nav-button-settings"
            onClick={() => onNavigate(SETTINGS.target)}
          >
            {SETTINGS.label}
          </button>

          <HealthDot level={health} />
        </nav>
      )}
    </main>
  );
}
