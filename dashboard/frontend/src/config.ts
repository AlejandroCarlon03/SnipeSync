/**
 * Frontend API configuration.
 *
 * The dashboard runs inside the .NET/Photino host, which serves this SPA and
 * exposes same-origin `/api/audit` + `/api/audit/stats` that it proxies to the
 * Azure Functions app — attaching the function key server-side. So by default the
 * browser calls its own origin ("") with no key: nothing secret is in the bundle.
 *
 * `window.__DASHBOARD_CONFIG__` remains an optional escape hatch (e.g. pointing a
 * standalone build straight at a Functions app), but the normal path leaves both
 * fields empty and relies on the host proxy.
 */
export interface DashboardConfig {
  /** Origin to call. "" = same origin (the host proxy). */
  baseUrl: string;
  /** Optional key for a direct-to-Functions setup; unused with the host proxy. */
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
