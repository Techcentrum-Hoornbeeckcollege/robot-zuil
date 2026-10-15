import { useCallback, useEffect, useState } from 'react';
import { api, ApiError } from '../../api/client';
import type { AdminSession } from '../../api/types';

interface Result {
  session: AdminSession | null;
  checking: boolean;
  /** Null when the last attempt succeeded or none has been made. */
  error: string | null;
  busy: boolean;
  login: (username: string, password: string) => Promise<boolean>;
  logout: () => Promise<void>;
}

/**
 * The admin login, and whether the password is still the shipped default.
 *
 * The session itself lives in an HttpOnly cookie, so this hook never sees a
 * token — it only asks the server whether the cookie it is already sending is
 * good for anything.
 */
export function useAdminSession(): Result {
  const [session, setSession] = useState<AdminSession | null>(null);
  const [checking, setChecking] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();

    api
      .adminSession(controller.signal)
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => {
        if (!controller.signal.aborted) {
          setChecking(false);
        }
      });

    return () => controller.abort();
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    setBusy(true);
    setError(null);

    try {
      setSession(await api.adminLogin(username, password));
      return true;
    } catch (caught) {
      // The server's own wording, since it distinguishes a wrong password from
      // a lockout and both matter to whoever is standing there.
      if (caught instanceof ApiError) {
        setError(messageFor(caught));
      } else {
        setError('Inloggen is niet gelukt.');
      }
      return false;
    } finally {
      setBusy(false);
    }
  }, []);

  const logout = useCallback(async () => {
    try {
      await api.adminLogout();
    } finally {
      setSession(null);
    }
  }, []);

  return { session, checking, error, busy, login, logout };
}

function messageFor(error: ApiError): string {
  const fromServer = parseError(error.body);
  if (fromServer) {
    return fromServer;
  }

  return error.status === 429
    ? 'Te veel mislukte pogingen. Probeer het over een paar minuten opnieuw.'
    : 'Inloggen is niet gelukt.';
}

function parseError(body: string): string | null {
  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === 'object' && parsed !== null && 'error' in parsed) {
      const message = (parsed as { error: unknown }).error;
      return typeof message === 'string' ? message : null;
    }
  } catch {
    // A rate-limiter rejection has an empty body; fall through to the default.
  }

  return null;
}
