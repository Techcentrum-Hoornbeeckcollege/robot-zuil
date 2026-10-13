import { useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';

interface Handlers {
  /** The meeting cache was replaced: anything showing meetings should re-fetch. */
  onCacheUpdated?: () => void;
  /** Settings or rooms changed in the admin panel. */
  onSettingsUpdated?: () => void;
}

/**
 * Subscribes to pushes from the host.
 *
 * The kiosk page may stay open for weeks, so it cannot rely on a reload to pick
 * up new data, and polling every few seconds forever is wasteful.
 *
 * The handlers are held in a ref rather than listed as effect dependencies. The
 * effect has to run once — re-subscribing on every render would tear the
 * websocket down continuously — but a callback captured once would then be
 * frozen at its first render, which is the bug this used to have: the agenda
 * views would never see a cache update because the handler they registered
 * closed over the first, empty state.
 */
export function useStatusHub(handlers: Handlers): HubConnectionState {
  const [state, setState] = useState(HubConnectionState.Disconnected);

  const latest = useRef(handlers);
  latest.current = handlers;

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hub/status')
      .withAutomaticReconnect()
      .build();

    connection.on('cacheUpdated', () => latest.current.onCacheUpdated?.());
    connection.on('settingsUpdated', () => latest.current.onSettingsUpdated?.());

    connection.onreconnecting(() => setState(HubConnectionState.Reconnecting));
    connection.onreconnected(() => {
      setState(HubConnectionState.Connected);
      // Anything could have changed while the socket was down.
      latest.current.onCacheUpdated?.();
      latest.current.onSettingsUpdated?.();
    });
    connection.onclose(() => setState(HubConnectionState.Disconnected));

    connection
      .start()
      .then(() => setState(HubConnectionState.Connected))
      .catch(() => setState(HubConnectionState.Disconnected));

    return () => {
      void connection.stop();
    };
  }, []);

  return state;
}
