import type {
  AdminCredentialUpdate,
  AdminDeviceCapabilities,
  AdminDeviceReply,
  AdminDiagnostics,
  AdminRoom,
  AdminSession,
  AdminSettings,
  AdminSettingsUpdate,
  AdminTestResult,
  Health,
  KioskConfig,
  RoomAgendasResponse,
  RoomSummary,
  SearchResponse,
  UpcomingAgendaResponse,
} from './types';

/**
 * A failed request, carrying the status so a caller can tell a 401 (the admin
 * session has expired — go back to the login panel) from something that
 * genuinely went wrong.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly body: string;

  constructor(status: number, body: string) {
    super(`${status}: ${body}`);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.text();
    throw new ApiError(response.status, body);
  }
  return (await response.json()) as T;
}

async function empty(response: Response): Promise<void> {
  if (!response.ok) {
    throw new ApiError(response.status, await response.text());
  }
}

function send(path: string, method: string, body?: unknown, signal?: AbortSignal) {
  return fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
}

export const api = {
  /**
   * POST rather than GET: a visitor's name in a query string would end up in
   * access logs and browser history. See docs/privacy.md.
   */
  searchByName(name: string, signal?: AbortSignal): Promise<SearchResponse> {
    return send('/api/search/by-name', 'POST', { name }, signal).then(json<SearchResponse>);
  },

  rooms(signal?: AbortSignal): Promise<RoomSummary[]> {
    return fetch('/api/rooms', { signal }).then(json<RoomSummary[]>);
  },

  health(signal?: AbortSignal): Promise<Health> {
    return fetch('/api/health', { signal }).then(json<Health>);
  },

  clearDevice(): Promise<void> {
    return fetch('/api/device/clear', { method: 'POST' }).then(() => undefined);
  },

  // --- Agenda -------------------------------------------------------------

  kioskConfig(signal?: AbortSignal): Promise<KioskConfig> {
    return fetch('/api/kiosk-config', { signal }).then(json<KioskConfig>);
  },

  agendaUpcoming(limit?: number, signal?: AbortSignal): Promise<UpcomingAgendaResponse> {
    const query = limit === undefined ? '' : `?limit=${limit}`;
    return fetch(`/api/agenda/upcoming${query}`, { signal }).then(json<UpcomingAgendaResponse>);
  },

  /** @param date ISO yyyy-MM-dd; defaults to today in the building's timezone. */
  agendaRooms(date?: string, signal?: AbortSignal): Promise<RoomAgendasResponse> {
    const query = date === undefined ? '' : `?date=${date}`;
    return fetch(`/api/agenda/rooms${query}`, { signal }).then(json<RoomAgendasResponse>);
  },

  // --- Admin --------------------------------------------------------------

  adminSession(signal?: AbortSignal): Promise<AdminSession> {
    return fetch('/api/admin/session', { signal }).then(json<AdminSession>);
  },

  adminLogin(username: string, password: string, signal?: AbortSignal): Promise<AdminSession> {
    return send('/api/admin/session', 'POST', { username, password }, signal)
      .then(json<AdminSession>);
  },

  adminSaveCredentials(
    update: AdminCredentialUpdate,
    signal?: AbortSignal,
  ): Promise<{ ok: boolean; message: string }> {
    return send('/api/admin/credentials', 'PUT', update, signal)
      .then(json<{ ok: boolean; message: string }>);
  },

  adminLogout(): Promise<void> {
    return send('/api/admin/session', 'DELETE').then(empty);
  },

  adminSettings(signal?: AbortSignal): Promise<AdminSettings> {
    return fetch('/api/admin/settings', { signal }).then(json<AdminSettings>);
  },

  adminSaveSettings(update: AdminSettingsUpdate, signal?: AbortSignal): Promise<AdminSettings> {
    return send('/api/admin/settings', 'PUT', update, signal).then(json<AdminSettings>);
  },

  adminSaveRooms(rooms: AdminRoom[], signal?: AbortSignal): Promise<{ rooms: AdminRoom[] }> {
    return send('/api/admin/rooms', 'PUT', { rooms }, signal).then(json<{ rooms: AdminRoom[] }>);
  },

  adminTestGraph(signal?: AbortSignal): Promise<AdminTestResult> {
    return send('/api/admin/graph/test', 'POST', {}, signal).then(json<AdminTestResult>);
  },

  adminTestDevice(signalId: number, signal?: AbortSignal): Promise<AdminTestResult> {
    return send('/api/admin/device/test', 'POST', { signalId }, signal).then(json<AdminTestResult>);
  },

  /** The commands the host knows how to send; the GUI draws itself from these. */
  adminDeviceCapabilities(signal?: AbortSignal): Promise<AdminDeviceCapabilities> {
    return fetch('/api/admin/device/capabilities', { signal }).then(json<AdminDeviceCapabilities>);
  },

  adminSendDeviceCommand(
    command: string,
    values: Record<string, number>,
    signal?: AbortSignal,
  ): Promise<AdminDeviceReply> {
    return send('/api/admin/device/command', 'POST', { command, values }, signal)
      .then(json<AdminDeviceReply>);
  },

  adminSyncNow(): Promise<void> {
    return send('/api/admin/sync/now', 'POST', {}).then(empty);
  },

  adminRequestUpdate(): Promise<void> {
    return send('/api/admin/update', 'POST', {}).then(empty);
  },

  adminDiagnostics(signal?: AbortSignal): Promise<AdminDiagnostics> {
    return fetch('/api/admin/diagnostics', { signal }).then(json<AdminDiagnostics>);
  },
};
