// Mirrors the DTOs in src/Zuil.Host/Endpoints.
//
// Hand-written for now. Once the API settles, generate this from the host's
// OpenAPI document instead, so the two sides cannot drift silently.

export interface DirectionHint {
  roomDisplayName: string;
  floor: string;
  directions: string;
  start: string;
  end: string;
  signalId: number;
}

export interface SearchResponse {
  results: DirectionHint[];
  stale: boolean;
}

export interface RoomSummary {
  id: string;
  displayName: string;
  floor: string;
  directions: string;
}

export interface Health {
  now: string;
  clockSynchronized: boolean;
  deviceConnected: boolean;
  lastSuccessfulSync: string | null;
  lastError: string | null;
  meetingCount: number;
  stale: boolean;
}
