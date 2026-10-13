import type { AttractView, SearchResponse } from '../api/types';

/** The tabs inside the admin panel. Not routes — the kiosk has no URL bar. */
export type AdminTab =
  | 'outlook'
  | 'sync'
  | 'rooms'
  | 'privacy'
  | 'device'
  | 'display'
  | 'diagnostics';

/**
 * Which screen the kiosk is on.
 *
 * A discriminated union rather than a bare string plus some loose state, so the
 * result screen cannot exist without its response and `switch` in App.tsx is
 * exhaustive — `noFallthroughCasesInSwitch` plus a `never` check in the default
 * branch means adding a view here fails the build until it is rendered.
 */
export type View =
  | { kind: 'attract' }
  | { kind: 'search' }
  | { kind: 'result'; response: SearchResponse }
  | { kind: 'upcoming' }
  | { kind: 'board' }
  | { kind: 'login' }
  | { kind: 'admin'; tab: AdminTab };

/** The views the kiosk is allowed to sit on when nobody is using it. */
export type RestingKind = 'attract' | 'upcoming' | 'board';

/** Maps the server's AttractView enum onto a view kind. */
export function restingKind(attractView: AttractView): RestingKind {
  switch (attractView) {
    case 'Upcoming':
      return 'upcoming';
    case 'Board':
      return 'board';
    case 'Attract':
      return 'attract';
    default:
      return 'attract';
  }
}

/**
 * Whether this view holds something a visitor typed or was shown about
 * themselves, and so must be wiped after the idle timeout.
 *
 * The agenda views are not on this list on purpose: with subjects hidden they
 * show a time and a room, which is the same thing a printed sign in the lobby
 * would show, so they are a legitimate screen to leave up.
 */
export function holdsVisitorData(view: View): boolean {
  return view.kind === 'search' || view.kind === 'result';
}

export const ADMIN_TABS: readonly { id: AdminTab; label: string }[] = [
  { id: 'outlook', label: 'Outlook' },
  { id: 'sync', label: 'Synchronisatie' },
  { id: 'rooms', label: 'Zalen' },
  { id: 'privacy', label: 'Privacy' },
  { id: 'device', label: 'Apparaat' },
  { id: 'display', label: 'Weergave' },
  { id: 'diagnostics', label: 'Diagnose' },
];
