import { useCallback, useState } from 'react';
import { AdminLogin } from './features/admin/AdminLogin';
import { AdminPanel } from './features/admin/AdminPanel';
import { AgendaBoard } from './features/agenda/AgendaBoard';
import { UpcomingAgenda } from './features/agenda/UpcomingAgenda';
import { useAdminSession } from './features/admin/useAdminSession';
import { AttractScreen } from './features/idle/AttractScreen';
import { Directions } from './features/directions/Directions';
import { NameSearch } from './features/search/NameSearch';
import { useIdleReset } from './hooks/useIdleReset';
import { useKioskConfig } from './hooks/useKioskConfig';
import { useStatusHub } from './hooks/useStatusHub';
import { KioskShell } from './shell/KioskShell';
import { useShell } from './shell/useShell';
import { holdsVisitorData, restingKind } from './shell/viewState';
import type { AdminTab } from './shell/viewState';

/**
 * One page for the whole kiosk: name search, two agenda views and the admin
 * panel, with no reload between them.
 *
 * No router. Chromium runs with --app= and --overscroll-history-navigation=0, so
 * there is no address bar, no back button and no gestures — a deep link is a
 * link nobody can follow. An admin URL in history would also be a hint worth not
 * leaving lying around.
 */
export function App() {
  const { config, reload: reloadConfig } = useKioskConfig();
  const resting = restingKind(config.attractView);

  const { view, go, reset } = useShell(resting);
  const { session, error, busy, login, logout } = useAdminSession();

  // Bumped by the hub so the agenda views re-fetch without polling.
  const [cacheNonce, setCacheNonce] = useState(0);

  const onCacheUpdated = useCallback(() => setCacheNonce((n) => n + 1), []);

  const onSettingsUpdated = useCallback(() => {
    // A new idle timeout, resting screen or room list, without a reload. This
    // is what makes "everything in one page" true rather than aspirational.
    reloadConfig();
    setCacheNonce((n) => n + 1);
  }, [reloadConfig]);

  useStatusHub({ onCacheUpdated, onSettingsUpdated });

  useIdleReset(config.idleTimeoutMs, () => {
    // Only screens holding something a visitor typed or was shown need wiping.
    // The agenda views are a legitimate thing to leave up, and the admin panel
    // runs its own shorter timer.
    if (holdsVisitorData(view)) {
      reset();
    }
  });

  const leaveAdmin = useCallback(() => {
    void logout();
    go({ kind: resting });
  }, [go, logout, resting]);

  const setTab = useCallback((tab: AdminTab) => go({ kind: 'admin', tab }), [go]);

  return (
    <KioskShell
      view={view}
      showAgendaNav={config.showAgendaNav}
      onNavigate={(next) =>
        // The Settings tab leads to the login, unless this browser already has
        // a live session — then straight in.
        next.kind === 'login' && session?.authenticated
          ? go({ kind: 'admin', tab: 'outlook' })
          : go(next)
      }
    >
      {renderView()}
    </KioskShell>
  );

  function renderView() {
    switch (view.kind) {
      case 'attract':
        return <AttractScreen onStart={() => go({ kind: 'search' })} />;

      case 'search':
        return (
          <NameSearch
            onResult={(response) => go({ kind: 'result', response })}
          />
        );

      case 'result':
        return (
          <Directions
            results={view.response.results}
            stale={view.response.stale}
            onReset={reset}
          />
        );

      case 'upcoming':
        return (
          <UpcomingAgenda
            limit={config.upcomingCount}
            refreshNonce={cacheNonce}
            onSearch={() => go({ kind: 'search' })}
          />
        );

      case 'board':
        return <AgendaBoard refreshNonce={cacheNonce} />;

      case 'login':
        return (
          <AdminLogin
            error={error}
            busy={busy}
            credentialsAreDefault={session?.credentialsAreDefault ?? false}
            onSubmit={async (username, password) => {
              if (await login(username, password)) {
                go({ kind: 'admin', tab: 'outlook' });
              }
            }}
            onCancel={() => go({ kind: resting })}
          />
        );

      case 'admin':
        return (
          <AdminPanel
            tab={view.tab}
            onTab={setTab}
            onLeave={leaveAdmin}
            onChanged={onSettingsUpdated}
          />
        );

      default: {
        // Adding a view to the union without rendering it fails the build here.
        const exhaustive: never = view;
        return exhaustive;
      }
    }
  }
}
