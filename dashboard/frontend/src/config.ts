/**
 * Option A configuration: the desktop host supplies the API base URL and the
 * Azure Functions key at runtime (read from a local config file, e.g. under
 * %APPDATA%) and injects them onto `window` before the bundle loads. Nothing
 * secret is baked into the build output.
 *
 * Under `npm run dev` neither is set: `baseUrl` falls back to "" so requests hit
 * the Vite dev-server proxy (see vite.config.ts), which forwards to a locally
 * running Functions host — keeping the browser same-origin, with no CORS setup
 * and no key needed for local development.
 */
export interface DashboardConfig {
  /** Absolute origin of the Functions app (e.g. https://dkb-snipeit-sync.azurewebsites.net). "" = use dev proxy. */
  baseUrl: string;
  /** Azure Functions key sent as ?code=… since the endpoints are AuthorizationLevel.Function. */
  functionKey: string;
}

declare global {
  interface Window {
    __DASHBOARD_CONFIG__?: Partial<DashboardConfig>;
  }
}

const injected = window.__DASHBOARD_CONFIG__ ?? {};

export const config: DashboardConfig = {
  baseUrl: injected.baseUrl ?? "",
  functionKey: injected.functionKey ?? "",
};
