import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Health, HealthLevel } from '../api/types';

/** Loopback, and the kiosk is idle most of the day, so this is cheap. */
const INTERVAL_MS = 5000;

export interface HealthState {
  /**
   * Null until the first answer arrives. The dot renders nothing while this is
   * null rather than guessing: a green dot shown over a backend that has not
   * answered yet is the one reading worse than no dot at all.
   */
  level: HealthLevel | null;
  /** Null before the first answer, and while the backend is unreachable. */
  detail: Health | null;
  /** True when the poll itself failed, as opposed to answering "Down". */
  unreachable: boolean;
}

/**
 * Polls /api/health.
 *
 * A failed request counts as Down rather than being swallowed: if the frontend
 * cannot reach its own backend, the kiosk is not serving anyone, and that is
 * exactly the state the dot exists to show. The previous answer is deliberately
 * not kept on failure, and the level starts out null rather than optimistic —
 * a green dot held over a dead backend is worse than no dot at all.
 */
export function useHealth(): HealthState {
  const [state, setState] = useState<HealthState>({
    level: null,
    detail: null,
    unreachable: false,
  });

  useEffect(() => {
    const abort = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let stopped = false;

    async function poll() {
      try {
        const health = await api.health(abort.signal);
        if (!stopped) {
          setState({ level: health.status, detail: health, unreachable: false });
        }
      } catch {
        // AbortError on unmount lands here too, which is why `stopped` guards
        // the write rather than the catch deciding what the error meant.
        if (!stopped) {
          setState({ level: 'Down', detail: null, unreachable: true });
        }
      }

      if (!stopped) {
        timer = setTimeout(poll, INTERVAL_MS);
      }
    }

    void poll();

    return () => {
      stopped = true;
      abort.abort();
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    };
  }, []);

  return state;
}
