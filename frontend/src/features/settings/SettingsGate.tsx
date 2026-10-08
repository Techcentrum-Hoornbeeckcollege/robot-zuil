import { useCallback, useEffect, useRef, useState } from 'react';

/** How long the corner must be held before the login panel opens. */
const HOLD_MS = 3_000;

interface Props {
  onOpen: () => void;
}

/**
 * The way in to the admin panel: an invisible corner target, held for three
 * seconds.
 *
 * No visible button, because a visitor who finds a "Instellingen" button on a
 * lobby kiosk will press it, and the login panel is then one more thing between
 * them and their meeting room. A long press is easy to explain to staff ("press
 * and hold the bottom right corner"), cannot be triggered by stray taps or by
 * someone cleaning the screen, and needs no affordance to work.
 *
 * Only rendered on the resting view, so it is never sitting on top of content
 * somebody is trying to read.
 */
export function SettingsGate({ onOpen }: Props) {
  const [holding, setHolding] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  const cancel = useCallback(() => {
    window.clearTimeout(timer.current);
    timer.current = undefined;
    setHolding(false);
  }, []);

  const start = useCallback(() => {
    setHolding(true);
    timer.current = window.setTimeout(() => {
      setHolding(false);
      onOpen();
    }, HOLD_MS);
  }, [onOpen]);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  return (
    <button
      type="button"
      // No label and no text: screen-reader users are not the audience for a
      // deliberately hidden maintenance gesture, and aria-hidden on a focusable
      // element is invalid, so it is simply unlabelled and out of the tab order.
      tabIndex={-1}
      className={holding ? 'settings-gate settings-gate-holding' : 'settings-gate'}
      onPointerDown={start}
      onPointerUp={cancel}
      onPointerLeave={cancel}
      onPointerCancel={cancel}
    />
  );
}
