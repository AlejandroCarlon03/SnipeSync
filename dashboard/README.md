# SnipeSync Dashboard

A desktop dashboard for the SnipeSync Entra ID → Snipe-IT sync. Ships as a single
Windows `.exe` with nothing to install — copy it to a folder or the desktop and
double-click.

<!-- Screenshots are in the pull request; they're not committed to keep the repo light. -->

## ⚠️ It currently shows sample data

**The numbers in the dashboard are fabricated.** The sync functions
(`FormerEmployeeSync`, `OnboardEmployeeSync`) only write to `ILogger` today —
console output locally, Application Insights in Azure. Nothing durable records
*what* they changed, so there is no real history for a dashboard to read.

The UI is built against a realistic sample dataset so the design can be settled
first. A banner says so in-app, and `/api/summary` returns `is_sample_data: true`.

### Wiring up real data

`backend/repository.py` is the seam. Implement the `SyncRepository` protocol and
select it in `app.py` — the API and UI need no changes. The options:

| Source | What it needs | Notes |
|---|---|---|
| **SQLite / Azure Table Storage** | The C# functions must write a record per action | Cleanest data. The functions run in Azure, so a local SQLite file isn't reachable from a desktop — a cloud store (Azure Table) is the realistic choice unless the sync also runs locally. |
| **Application Insights** | Azure credentials; App Insights provisioned | No C# changes, reads the telemetry already emitted. Log-derived data is messier and query cost applies. |

## Running it

**End users:** double-click `SnipeSyncDashboard.exe`. No Python, no Node, no
install. It opens its own window; it does not need a browser or internet access.
Nothing is written to disk and the port is bound to loopback only.

**Requirements:** Windows 10/11 with the Edge WebView2 runtime, which is
preinstalled on Windows 11 and current Windows 10. Without it the app falls back
to opening in the default browser rather than failing.

## Building the .exe

```bash
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
.venv\Scripts\python build_exe.py
```

Output: `dist/SnipeSyncDashboard.exe` (~17 MB). The build script runs `npm
install`/`npm run build` for you, then packages the bundle, Flask, and the Python
runtime into the one file. Needs Node and Python on the *build* machine only.

## Developing

Two servers, because the UI hot-reloads:

```bash
# API on :5178
cd backend && ../.venv/Scripts/python app.py

# UI on :5179, proxying /api to :5178
cd frontend && npm run dev
```

In the packaged app there's no proxy — Flask serves the built bundle and the API
from one origin.

## Layout

```
dashboard/
  backend/
    main.py         # desktop entry point: Flask on a loopback port + native window
    app.py          # Flask: JSON API + serves the built React bundle
    repository.py   # ← the seam where a real data source plugs in
    sample_data.py  # fabricates the sample history (NOT real data)
    models.py       # the data shapes, mirroring what the C# app would emit
  frontend/
    src/
      App.tsx
      api.ts        # typed API client; keep in step with models.py
      app.css       # layout + components
      index.css     # design tokens (light/dark)
      components/
  build_exe.py      # npm build → PyInstaller → dist/SnipeSyncDashboard.exe
```

## Notes on the UI

- **Colours are validated, not chosen by eye.** The three chart series were
  checked for colourblind separation and contrast against both the light and dark
  surfaces. Dark mode uses its own set of steps rather than a flip of the light
  values. If you change a series colour, re-validate it — the dark set's
  separation has little headroom, which is why the chart also carries a legend,
  tooltips, and a table view rather than relying on colour alone.
- **The chart measures its own container** rather than using Recharts'
  `ResponsiveContainer`, which renders nothing at all where `ResizeObserver` is
  unavailable.
- The theme attribute is set synchronously on toggle, since the chart reads its
  colours from CSS custom properties at render time.
