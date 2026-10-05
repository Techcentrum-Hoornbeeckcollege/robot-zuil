import { useEffect, useRef } from 'react';

/**
 * Calls `onIdle` after `timeoutMs` without touch, pointer or key input.
 *
 * Not a nicety: without it, the last visitor's name and their meeting stay on a
 * screen in a public lobby until someone else walks up.
 */
export function useIdleReset(timeoutMs: number, onIdle: () => void): void {
  const callback = useRef(onIdle);
  callback.current = onIdle;

  useEffect(() => {
    let timer: number;

    const reset = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => callback.current(), timeoutMs);
    };

    const events: (keyof WindowEventMap)[] = [
      'pointerdown',
      'pointermove',
      'keydown',
      'touchstart',
      'wheel',
    ];

    events.forEach((e) => window.addEventListener(e, reset, { passive: true }));
    reset();

    return () => {
      window.clearTimeout(timer);
      events.forEach((e) => window.removeEventListener(e, reset));
    };
  }, [timeoutMs]);
}
