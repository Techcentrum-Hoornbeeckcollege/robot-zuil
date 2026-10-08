import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { RestingKind, View } from './viewState';

interface Shell {
  view: View;
  /** Go to a specific view. */
  go: (view: View) => void;
  /** Back to the resting screen, clearing anything a visitor left behind. */
  reset: () => void;
}

/**
 * Holds the current view and knows how to get back to a resting state.
 *
 * `resting` arrives from the server and can change while the page is open — an
 * administrator switching the resting screen, pushed over the hub — so when it
 * changes and the kiosk is already idling on a resting view, it moves to the new
 * one rather than waiting for a reload.
 */
export function useShell(resting: RestingKind): Shell {
  const [view, setView] = useState<View>({ kind: resting });

  // Read inside reset without making reset change identity on every view
  // change, which would re-arm the idle timer continuously.
  const current = useRef(view);
  current.current = view;

  const reset = useCallback(() => {
    // Also drop the physical indicator, so the column is not still pointing at
    // the previous visitor's room.
    void api.clearDevice();
    setView({ kind: resting });
  }, [resting]);

  useEffect(() => {
    const kind = current.current.kind;

    // Only re-home an idle kiosk. Someone mid-search must not be thrown out of
    // it because a setting changed.
    if (kind === 'attract' || kind === 'upcoming' || kind === 'board') {
      setView({ kind: resting });
    }
  }, [resting]);

  const go = useCallback((next: View) => setView(next), []);

  return { view, go, reset };
}
