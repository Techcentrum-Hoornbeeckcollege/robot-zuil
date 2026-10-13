import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { RoomAgendasResponse, UpcomingAgendaResponse } from '../../api/types';

interface Result<T> {
  data: T | null;
  /** True only on the first load, so a refresh does not blank the board. */
  loading: boolean;
  failed: boolean;
  reload: () => void;
}

function useFetch<T>(fetcher: (signal: AbortSignal) => Promise<T>, nonce: number): Result<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [localNonce, setLocalNonce] = useState(0);

  const reload = useCallback(() => setLocalNonce((n) => n + 1), []);

  useEffect(() => {
    const controller = new AbortController();

    fetcher(controller.signal)
      .then((next) => {
        setData(next);
        setFailed(false);
      })
      .catch((error: unknown) => {
        // An aborted request is a navigation, not a failure.
        if (!controller.signal.aborted) {
          setFailed(true);
        }
        void error;
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => controller.abort();
    // fetcher is recreated by the caller whenever its inputs change, which is
    // what should trigger a re-fetch.
  }, [fetcher, nonce, localNonce]);

  return { data, loading, failed, reload };
}

/**
 * The "Straks" list. `nonce` comes from the hub, so a cache update refreshes the
 * screen without a poll.
 */
export function useUpcomingAgenda(limit: number, nonce: number): Result<UpcomingAgendaResponse> {
  const fetcher = useCallback(
    (signal: AbortSignal) => api.agendaUpcoming(limit, signal),
    [limit],
  );

  return useFetch(fetcher, nonce);
}

/** The per-room board for one day. `date` is yyyy-MM-dd, or undefined for today. */
export function useRoomAgendas(date: string | undefined, nonce: number): Result<RoomAgendasResponse> {
  const fetcher = useCallback(
    (signal: AbortSignal) => api.agendaRooms(date, signal),
    [date],
  );

  return useFetch(fetcher, nonce);
}
