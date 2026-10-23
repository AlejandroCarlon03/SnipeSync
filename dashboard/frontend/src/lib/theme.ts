export type Theme = "light" | "dark";

/** The key index.html reads before first paint to avoid a light flash on launch. */
const STORAGE_KEY = "snipesync.theme";

/** Whatever index.html's boot script already decided (OS preference or a saved choice). */
export function currentTheme(): Theme {
  return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
}

/**
 * Apply and persist a theme. Sets the attribute synchronously rather than from a
 * React effect, so anything that resolves colours out of the computed styles
 * during render never paints one render behind in the previous palette.
 */
export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute("data-theme", theme);
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Private mode / storage disabled — the toggle still works for this session.
  }
}
