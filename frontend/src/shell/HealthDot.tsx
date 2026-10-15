import type { HealthLevel } from '../api/types';

const LABELS: Record<HealthLevel, string> = {
  Ok: 'Zuil in orde',
  Degraded: 'Zuil werkt, maar heeft aandacht nodig',
  Down: 'Zuil heeft een storing',
};

interface Props {
  /** Null until the first poll answers; nothing is drawn until then. */
  level: HealthLevel | null;
}

/**
 * A colour, and nothing else.
 *
 * This is the one health indicator a visitor in the lobby can see, so it says
 * nothing about *what* is wrong: "Outlook niet geconfigureerd" on a public
 * screen tells a stranger how the building's systems are doing. The detail is
 * behind the admin login, under Diagnose.
 *
 * The real reader is a member of staff walking past, for whom amber or red is
 * the whole message: go and look at Diagnose. The text is in the title and the
 * accessible name, not on screen.
 */
export function HealthDot({ level }: Props) {
  if (level === null) {
    return null;
  }

  return (
    <span
      className={`health-dot health-dot-${level.toLowerCase()}`}
      role="status"
      title={LABELS[level]}
    >
      <span className="sr-only">{LABELS[level]}</span>
    </span>
  );
}
