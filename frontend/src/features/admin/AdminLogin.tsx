import { useState } from 'react';
import type { FormEvent } from 'react';

interface Props {
  error: string | null;
  busy: boolean;
  onSubmit: (password: string) => void;
  onCancel: () => void;
}

/**
 * The admin password prompt.
 *
 * `type="password"` even though nobody is likely to be reading over a
 * maintainer's shoulder, because this screen is in a public lobby and that is
 * exactly the situation the masking is for.
 */
export function AdminLogin({ error, busy, onSubmit, onCancel }: Props) {
  const [password, setPassword] = useState('');

  function submit(event: FormEvent) {
    event.preventDefault();
    onSubmit(password);
  }

  return (
    <form className="panel admin-login" onSubmit={submit}>
      <h2>Beheer</h2>

      <label className="label" htmlFor="admin-password">
        Wachtwoord
      </label>

      <input
        id="admin-password"
        className="input"
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        autoComplete="current-password"
        autoFocus
      />

      {error && <p className="error">{error}</p>}

      <div className="admin-actions">
        <button className="button" type="submit" disabled={busy || password.length === 0}>
          {busy ? 'Inloggen…' : 'Inloggen'}
        </button>

        <button className="button button-secondary" type="button" onClick={onCancel}>
          Terug
        </button>
      </div>
    </form>
  );
}
