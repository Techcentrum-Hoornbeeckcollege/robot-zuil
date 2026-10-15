import { useState } from 'react';
import { api, ApiError } from '../../api/client';
import type { AdminFieldError } from '../../api/types';
import { errorFor, SaveActions, TextField } from './fields';

interface Props {
  username: string;
  credentialsAreDefault: boolean;
  /** Called after a successful change; every session is ended, so we log out. */
  onChanged: () => void;
}

/**
 * Changing the admin username and password.
 *
 * Separate from the settings tabs because it needs the current password and
 * because a password is not a setting — nothing here goes through the settings
 * PUT.
 */
export function AccountForm({ username, credentialsAreDefault, onChanged }: Props) {
  const [name, setName] = useState(username);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [repeat, setRepeat] = useState('');
  const [errors, setErrors] = useState<AdminFieldError[]>([]);
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const mismatch = repeat.length > 0 && newPassword !== repeat;
  const dirty =
    currentPassword.length > 0 && newPassword.length > 0 && !mismatch;

  async function save() {
    setBusy(true);
    setErrors([]);
    setFailure(null);

    try {
      await api.adminSaveCredentials({
        username: name,
        currentPassword,
        newPassword,
      });

      onChanged();
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 400) {
        setErrors(parseErrors(caught.body));
      } else {
        setFailure('Opslaan is niet gelukt.');
      }
    } finally {
      setBusy(false);
      setCurrentPassword('');
      setNewPassword('');
      setRepeat('');
    }
  }

  return (
    <section className="admin-section">
      {credentialsAreDefault && (
        <p className="warning">
          Dit apparaat gebruikt nog de standaardgegevens. Iedereen die dit project
          kent, kan hier dus binnen. Stel nu een eigen wachtwoord in.
        </p>
      )}

      <TextField
        id="account-username"
        label="Gebruikersnaam"
        value={name}
        onChange={setName}
        error={errorFor(errors, 'username')}
      />

      <TextField
        id="account-current"
        label="Huidig wachtwoord"
        type="password"
        value={currentPassword}
        onChange={setCurrentPassword}
        hint="Ook nodig als u alleen de gebruikersnaam wijzigt: een onbeheerd open scherm mag geen manier zijn om het account over te nemen."
        error={errorFor(errors, 'currentPassword')}
      />

      <TextField
        id="account-new"
        label="Nieuw wachtwoord"
        type="password"
        value={newPassword}
        onChange={setNewPassword}
        hint="Minstens 8 tekens."
        error={errorFor(errors, 'newPassword')}
      />

      <TextField
        id="account-repeat"
        label="Herhaal nieuw wachtwoord"
        type="password"
        value={repeat}
        onChange={setRepeat}
        error={mismatch ? 'De wachtwoorden zijn niet gelijk.' : undefined}
      />

      <SaveActions busy={busy} dirty={dirty} saved={false} onSave={save} />

      <p className="admin-field-hint">
        Na het opslaan worden alle beheersessies beëindigd en moet u opnieuw
        inloggen.
      </p>

      {failure && <p className="error">{failure}</p>}
    </section>
  );
}

function parseErrors(body: string): AdminFieldError[] {
  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === 'object' && parsed !== null && 'errors' in parsed) {
      const errors = (parsed as { errors: unknown }).errors;
      if (Array.isArray(errors)) {
        return errors as AdminFieldError[];
      }
    }
  } catch {
    // Fall through: a generic failure is shown instead.
  }

  return [];
}
