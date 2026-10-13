import { useState } from 'react';
import type { FormEvent } from 'react';

interface Props {
  error: string | null;
  busy: boolean;
  /** True while the shipped admin/password pair is still in place. */
  credentialsAreDefault: boolean;
  onSubmit: (username: string, password: string) => void;
  onCancel: () => void;
}

/**
 * The admin login.
 *
 * `type="password"` even though nobody is likely to be reading over a
 * maintainer's shoulder, because this screen is in a public lobby and that is
 * exactly the situation the masking is for.
 */
export function AdminLogin({
  error,
  busy,
  credentialsAreDefault,
  onSubmit,
  onCancel,
}: Props) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  function submit(event: FormEvent) {
    event.preventDefault();
    onSubmit(username, password);
  }

  return (
    <form className="panel admin-login" onSubmit={submit}>
      <h2>Beheer</h2>

      {credentialsAreDefault && (
        <p className="warning">
          Dit apparaat gebruikt nog het standaardwachtwoord. Log in en wijzig het
          onder Account.
        </p>
      )}

      <label className="label" htmlFor="admin-username">
        Gebruikersnaam
      </label>

      <input
        id="admin-username"
        className="input"
        value={username}
        onChange={(e) => setUsername(e.target.value)}
        autoComplete="username"
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck={false}
        autoFocus
      />

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
      />

      {error && <p className="error">{error}</p>}

      <div className="admin-actions">
        <button
          className="button"
          type="submit"
          disabled={busy || username.length === 0 || password.length === 0}
        >
          {busy ? 'Inloggen…' : 'Inloggen'}
        </button>

        <button className="button button-secondary" type="button" onClick={onCancel}>
          Terug
        </button>
      </div>
    </form>
  );
}
