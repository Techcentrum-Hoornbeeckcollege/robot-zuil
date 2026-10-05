import { useEffect, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';

/**
 * Subscribes to cache-refresh pushes from the host.
 *
 * The kiosk page may stay open for weeks, so it cannot rely on a reload to pick
 * up new data, and polling every few seconds forever is wasteful.
 */
export function useStatusHub(onCacheUpdated: () => void): HubConnectionState {
  const [state, setState] = useState(HubConnectionState.Disconnected);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hub/status')
      .withAutomaticReconnect()
      .build();

    connection.on('cacheUpdated', onCacheUpdated);
    connection.onreconnecting(() => setState(HubConnectionState.Reconnecting));
    connection.onreconnected(() => setState(HubConnectionState.Connected));
    connection.onclose(() => setState(HubConnectionState.Disconnected));

    connection
      .start()
      .then(() => setState(HubConnectionState.Connected))
      .catch(() => setState(HubConnectionState.Disconnected));

    return () => {
      void connection.stop();
    };
    // onCacheUpdated is intentionally not a dependency: re-subscribing on every
    // render would tear the websocket down continuously.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return state;
}
