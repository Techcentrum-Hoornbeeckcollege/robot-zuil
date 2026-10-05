import { useState } from 'react';
import type { FormEvent } from 'react';
import { api } from '../../api/client';
import type { SearchResponse } from '../../api/types';

interface Props {
  onResult: (response: SearchResponse, query: string) => void;
}

/**
 * Name entry.
 *
 * No autocomplete and no suggestions as you type, on purpose: a public screen
 * that completes names would be a browsable directory of everyone with a
 * meeting in the building. The lookup is exact-match and only runs on submit.
 */
export function NameSearch({ onResult }: Props) {
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const response = await api.searchByName(name);
      onResult(response, name);
    } catch {
      setError('Zoeken is niet gelukt. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="panel" onSubmit={submit}>
      <label className="label" htmlFor="name">
        Vul uw volledige naam in
      </label>

      <input
        id="name"
        className="input"
        value={name}
        onChange={(e) => setName(e.target.value)}
        autoComplete="off"
        autoCorrect="off"
        spellCheck={false}
        placeholder="Voornaam Achternaam"
        // The kiosk has no keyboard; an on-screen one is opened by the OS.
        inputMode="text"
      />

      <button className="button" type="submit" disabled={busy || name.trim().length < 3}>
        {busy ? 'Zoeken…' : 'Zoek mijn vergadering'}
      </button>

      {error && <p className="error">{error}</p>}

      {/* TODO: add an on-screen keyboard component; a touchscreen kiosk cannot
          rely on the OS one appearing under Chromium kiosk mode. */}
    </form>
  );
}
