"""Builds SnipeSyncDashboard.exe.

Run:  python build_exe.py

Does the whole job end to end: builds the React bundle with npm, then packages
it plus Flask and the Python runtime into one self-contained .exe. The result
needs nothing installed on the target machine -- copy it anywhere and run it.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent
FRONTEND = ROOT / "frontend"
BACKEND = ROOT / "backend"
STATIC = BACKEND / "static"
DIST = ROOT / "dist"

APP_NAME = "SnipeSyncDashboard"


def run(cmd: list[str], cwd: Path) -> None:
    print(f"\n$ {' '.join(cmd)}  (in {cwd.name})")
    # shell=True on Windows so npm/npm.cmd resolves.
    subprocess.run(cmd, cwd=cwd, check=True, shell=sys.platform == "win32")


def build_frontend() -> None:
    if not (FRONTEND / "node_modules").exists():
        run(["npm", "install"], FRONTEND)
    run(["npm", "run", "build"], FRONTEND)

    if not (STATIC / "index.html").exists():
        raise SystemExit(f"Frontend build produced no index.html in {STATIC}")


def build_exe() -> Path:
    for stale in (DIST, ROOT / "build"):
        shutil.rmtree(stale, ignore_errors=True)

    run(
        [
            sys.executable,
            "-m",
            "PyInstaller",
            "--noconfirm",
            "--clean",
            "--onefile",
            # No console window behind the app.
            "--windowed",
            "--name",
            APP_NAME,
            # Ship the built UI inside the exe; app.py reads it from _MEIPASS.
            "--add-data",
            f"{STATIC}{';' if sys.platform == 'win32' else ':'}static",
            "--distpath",
            str(DIST),
            "--workpath",
            str(ROOT / "build"),
            "--specpath",
            str(ROOT / "build"),
            str(BACKEND / "main.py"),
        ],
        ROOT,
    )

    exe = DIST / f"{APP_NAME}.exe"
    if not exe.exists():
        raise SystemExit(f"Expected {exe}, but PyInstaller produced nothing there.")
    return exe


def main() -> int:
    build_frontend()
    exe = build_exe()
    size_mb = exe.stat().st_size / (1024 * 1024)
    print(f"\nBuilt {exe}  ({size_mb:.1f} MB)")
    print("Copy it anywhere and double-click to run.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
