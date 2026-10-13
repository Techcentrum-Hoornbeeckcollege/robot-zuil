import type { ReactNode } from 'react';
import type { View } from './viewState';

interface Props {
  view: View;
  showAgendaNav: boolean;
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
 * The chrome around every screen: one page, with a nav bar instead of a reload.
 *
 * The bar is hidden on the attract screen, which is a single full-bleed button
 * and must not have anything else to tap, and in the admin panel, which has its
 * own tabs and its own way out.
 */
export function KioskShell({ view, showAgendaNav, onNavigate, children }: Props) {
  const chromeless = view.kind === 'attract' || view.kind === 'login' || view.kind === 'admin';

  const items = showAgendaNav
    ? ITEMS
    : ITEMS.filter((item) => item.kind === 'search');

  // One button is not a choice, so there is nothing worth showing.
  const showNav = !chromeless && items.length > 1;

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
        </nav>
      )}
    </main>
  );
}
