// Mirrors the DTOs in src/Zuil.Host/Endpoints.
//
// Hand-written for now. Once the API settles, generate this from the host's
// OpenAPI document instead, so the two sides cannot drift silently.
//
// Durations cross the admin API as plain numbers in the unit the form field
// uses — minutes, hours, milliseconds — rather than as "hh:mm:ss" strings, so
// a touchscreen input never has to build one by hand.

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

// --- Agenda ---------------------------------------------------------------

/**
 * One row of an agenda.
 *
 * `subject` is null unless the configured visibility allows this viewer to see
 * it, and the server is what makes it null. The client never has to decide
 * whether to render it — if it is here, it may be shown.
 */
export interface AgendaEntry {
  roomId: string;
  roomDisplayName: string;
  floor: string;
  start: string;
  end: string;
  subject: string | null;
}

export interface UpcomingAgendaResponse {
  now: string;
  stale: boolean;
  showSubjects: boolean;
  entries: AgendaEntry[];
}

export interface RoomAgenda {
  id: string;
  displayName: string;
  floor: string;
  entries: AgendaEntry[];
}

export interface RoomAgendasResponse {
  now: string;
  from: string;
  to: string;
  /** The requested day falls partly outside the cached window. */
  truncated: boolean;
  stale: boolean;
  showSubjects: boolean;
  rooms: RoomAgenda[];
}

/** Values of Zuil.Core.Settings.AttractView, as the enum serializes. */
export type AttractView = 'Attract' | 'Upcoming' | 'Board';

/** Values of Zuil.Core.Settings.SubjectVisibility. */
export type SubjectVisibility = 'Hidden' | 'AdminOnly' | 'Everyone';

export interface KioskConfig {
  idleTimeoutMs: number;
  attractView: AttractView;
  upcomingCount: number;
  showSubjects: boolean;
  showAgendaNav: boolean;
  hasRooms: boolean;
}

// --- Admin ----------------------------------------------------------------

export interface AdminSession {
  authenticated: boolean;
  /** True while the shipped admin/password pair is still in place. */
  credentialsAreDefault: boolean;
  username: string;
}

export interface AdminFieldError {
  field: string;
  message: string;
}

export interface AdminValidationResponse {
  errors: AdminFieldError[];
}

export interface AdminTestResult {
  ok: boolean;
  message: string;
}

export interface AdminGraphSettings {
  tenantId: string;
  clientId: string;
  baseUrl: string;
  timeZone: string;
  /** Path to a .pfx on the device. Wins over the client secret when set. */
  certificatePath: string;
  certificatePasswordIsSet: boolean;
  /** Whether a secret is stored. The value itself is never returned. */
  secretIsSet: boolean;
  authMode: 'secret' | 'certificate' | 'none';
}

export interface AdminSecuritySettings {
  sessionIdleTimeoutMinutes: number;
  maxFailedAttempts: number;
  lockoutWindowMinutes: number;
}

export interface AdminNetworkSettings {
  /** Fixed at 127.0.0.1 and reported so the UI can show it as not editable. */
  host: string;
  port: number;
}

export interface AdminBrowserSettings {
  launchBrowser: boolean;
  browserPath: string;
}

export interface AdminSyncSettings {
  intervalMinutes: number;
  windowBehindHours: number;
  windowAheadHours: number;
  staleAfterMinutes: number;
  retainPastMeetingsForHours: number;
}

export interface AdminDeviceSettings {
  busId: number;
  address: number;
  attentionPin: number | null;
  responseDelayMs: number;
  maxRetries: number;
  maxCommandsPerSecond: number;
}

export interface AdminPrivacySettings {
  subjectVisibility: SubjectVisibility;
  idleTimeoutSeconds: number;
}

export interface AdminUiSettings {
  attractView: AttractView;
  upcomingCount: number;
  showAgendaNav: boolean;
}

export interface AdminRoom {
  id: string;
  mailbox: string;
  displayName: string;
  floor: string;
  directions: string;
  signalId: number;
}

export interface AdminSettings {
  graph: AdminGraphSettings;
  sync: AdminSyncSettings;
  device: AdminDeviceSettings;
  privacy: AdminPrivacySettings;
  ui: AdminUiSettings;
  security: AdminSecuritySettings;
  network: AdminNetworkSettings;
  browser: AdminBrowserSettings;
  rooms: AdminRoom[];
  username: string;
  credentialsAreDefault: boolean;
}

/** What to do with the stored client secret on this save. */
export type SecretAction = 'Keep' | 'Set' | 'Clear';

export interface AdminGraphUpdate
  extends Omit<
    AdminGraphSettings,
    'secretIsSet' | 'authMode' | 'certificatePasswordIsSet'
  > {
  secretAction: SecretAction;
  secret?: string;
  certificatePasswordAction: SecretAction;
  certificatePassword?: string;
}

/** Port only — the host is not settable. */
export type AdminNetworkUpdate = Omit<AdminNetworkSettings, 'host'>;

/** Changing the admin account. The current password is required. */
export interface AdminCredentialUpdate {
  username: string;
  currentPassword: string;
  newPassword: string;
}

/** A partial update: any section left out is kept as it is. */
export interface AdminSettingsUpdate {
  graph?: AdminGraphUpdate;
  sync?: AdminSyncSettings;
  device?: AdminDeviceSettings;
  privacy?: AdminPrivacySettings;
  ui?: AdminUiSettings;
  security?: AdminSecuritySettings;
  network?: AdminNetworkUpdate;
  browser?: AdminBrowserSettings;
}

export interface AdminDiagnostics {
  now: string;
  clockSynchronized: boolean;
  deviceConnected: boolean;
  lastSuccessfulSync: string | null;
  lastAttemptedSync: string | null;
  lastError: string | null;
  meetingCount: number;
  stale: boolean;
  roomCount: number;
  activeSessions: number;
  credentialsAreDefault: boolean;
  /** Where the database and its key file live. Not configurable; see DataDirectory. */
  dataDirectory: string;
}
