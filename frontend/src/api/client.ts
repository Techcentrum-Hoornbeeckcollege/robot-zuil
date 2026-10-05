import type { Health, RoomSummary, SearchResponse } from './types';

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.text();
    throw new Error(`${response.status}: ${body}`);
  }
  return (await response.json()) as T;
}

export const api = {
  /**
   * POST rather than GET: a visitor's name in a query string would end up in
   * access logs and browser history. See docs/privacy.md.
   */
  searchByName(name: string, signal?: AbortSignal): Promise<SearchResponse> {
    return fetch('/api/search/by-name', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name }),
      signal,
    }).then(json<SearchResponse>);
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
};
