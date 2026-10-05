import { useCallback, useState } from 'react';
import { api } from './api/client';
import type { SearchResponse } from './api/types';
import { AttractScreen } from './features/idle/AttractScreen';
import { Directions } from './features/directions/Directions';
import { NameSearch } from './features/search/NameSearch';
import { useIdleReset } from './hooks/useIdleReset';
import { useStatusHub } from './hooks/useStatusHub';

type Screen = 'attract' | 'search' | 'result';

/** Back to the attract screen after this long without touch. */
const IDLE_TIMEOUT_MS = 45_000;

export function App() {
  const [screen, setScreen] = useState<Screen>('attract');
  const [response, setResponse] = useState<SearchResponse | null>(null);

  const reset = useCallback(() => {
    setResponse(null);
    setScreen('attract');
    // Also drop the physical indicator, so the column is not still pointing at
    // the previous visitor's room.
    void api.clearDevice();
  }, []);

  useIdleReset(IDLE_TIMEOUT_MS, () => {
    if (screen !== 'attract') {
      reset();
    }
  });

  // Nothing to refresh on screen today, but keeping the connection open means a
  // future "room is now occupied" indicator needs no polling.
  useStatusHub(useCallback(() => {}, []));

  return (
    <main className="app">
      {screen === 'attract' && <AttractScreen onStart={() => setScreen('search')} />}

      {screen === 'search' && (
        <NameSearch
          onResult={(result) => {
            setResponse(result);
            setScreen('result');
          }}
        />
      )}

      {screen === 'result' && response && (
        <Directions
          results={response.results}
          stale={response.stale}
          onReset={reset}
        />
      )}
    </main>
  );
}
