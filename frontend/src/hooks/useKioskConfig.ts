import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { KioskConfig } from '../api/types';

/**
 * Defaults used until the first fetch answers, and if it never does.
 *
 * A kiosk whose backend is briefly unreachable has to render something, and
 * these are the conservative choices: the old hard-coded 45s idle timeout, the
 * attract screen, and no subjects.
 */
const FALLBACK: KioskConfig = {
  idleTimeoutMs: 45_000,
  attractView: 'Attract',
  upcomingCount: 25,
  showSubjects: false,
  showAgendaNav: true,
  hasRooms: true,
};

interface Result {
  config: KioskConfig;
  /** False until the first response, so the shell can avoid a visible flip. */
  loaded: boolean;
  reload: () => void;
}

/**
 * The handful of settings the SPA needs before it can render.
 *
 * Re-fetched on the hub's `settingsUpdated` push, which is what lets an
 * administrator change the idle timeout or the resting screen and see it take
 * effect on the kiosk without restarting anything.
 */
export function useKioskConfig(): Result {
  const [config, setConfig] = useState<KioskConfig>(FALLBACK);
  const [loaded, setLoaded] = useState(false);
  const [nonce, setNonce] = useState(0);

  const reload = useCallback(() => setNonce((n) => n + 1), []);

  useEffect(() => {
    const controller = new AbortController();

    api
      .kioskConfig(controller.signal)
      .then((next) => {
        setConfig(next);
        setLoaded(true);
      })
      .catch(() => {
        // Keep whatever we had. The staleness banner and the health endpoint
        // are where a backend problem gets reported; the kiosk still works.
        setLoaded(true);
      });

    return () => controller.abort();
  }, [nonce]);

  return { config, loaded, reload };
}
