"""Flask app: JSON API plus the built React bundle."""

from __future__ import annotations

import sys
from pathlib import Path

from flask import Flask, jsonify, request, send_from_directory

import repository
from repository import SampleRepository


def _static_dir() -> Path:
    """Where the built frontend lives.

    Under PyInstaller the bundle is unpacked to a temp dir exposed as _MEIPASS;
    in development the files sit in backend/static after `npm run build`.
    """
    if getattr(sys, "frozen", False):
        return Path(sys._MEIPASS) / "static"  # type: ignore[attr-defined]
    return Path(__file__).parent / "static"


def create_app() -> Flask:
    static_dir = _static_dir()
    app = Flask(__name__, static_folder=str(static_dir), static_url_path="")

    # One repository for the process. Swap this line to change data source.
    repo = SampleRepository(days=30)

    @app.get("/api/summary")
    def summary():
        days = request.args.get("days", default=30, type=int)
        return jsonify(repository.build_summary(repo, days=days))

    @app.get("/api/activity")
    def activity():
        days = request.args.get("days", default=30, type=int)
        return jsonify(repository.build_activity(repo, days=days))

    @app.get("/api/events")
    def events():
        return jsonify(
            repository.build_events(
                repo,
                limit=request.args.get("limit", default=100, type=int),
                action=request.args.get("action", type=str),
                search=request.args.get("search", type=str),
            )
        )

    @app.get("/api/runs")
    def runs():
        return jsonify(repository.build_runs(repo, limit=request.args.get("limit", default=20, type=int)))

    @app.get("/api/health")
    def health():
        return jsonify({"ok": True, "is_sample_data": repo.is_sample})

    @app.get("/")
    def index():
        return send_from_directory(static_dir, "index.html")

    @app.errorhandler(404)
    def spa_fallback(_):
        # Unknown non-API paths fall through to the SPA.
        if request.path.startswith("/api/"):
            return jsonify({"error": "not found"}), 404
        return send_from_directory(static_dir, "index.html")

    return app


if __name__ == "__main__":
    create_app().run(port=5178, debug=True)
