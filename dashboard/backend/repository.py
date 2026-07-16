"""The seam between the dashboard and wherever sync data comes from.

Right now there is exactly one implementation, and it fabricates data. When the
C# functions start persisting their work -- to SQLite, Azure Table Storage, or
App Insights -- add an implementation of SyncRepository alongside this one and
select it in app.py. Nothing in the API layer or the UI should need to change.
"""

from __future__ import annotations

from collections import defaultdict
from datetime import date, datetime, timedelta
from typing import Any, Protocol

import sample_data
from models import Action, Status, SyncEvent, SyncFunction, SyncRun


class SyncRepository(Protocol):
    def runs(self) -> list[SyncRun]: ...
    def events(self) -> list[SyncEvent]: ...
    @property
    def is_sample(self) -> bool: ...


class SampleRepository:
    """Serves the fabricated dataset from sample_data."""

    def __init__(self, days: int = 30) -> None:
        # Twice the display window: the KPI deltas compare the visible window
        # against the one before it, which needs history behind the cutoff.
        self._runs, self._events = sample_data.generate(days=days * 2)
        self._days = days

    def runs(self) -> list[SyncRun]:
        return self._runs

    def events(self) -> list[SyncEvent]:
        return self._events

    @property
    def is_sample(self) -> bool:
        return True


def _window(items: list, key, days: int) -> list:
    cutoff = datetime.now() - timedelta(days=days)
    return [i for i in items if key(i) >= cutoff]


def build_summary(repo: SyncRepository, days: int = 30) -> dict[str, Any]:
    """The KPI row: totals for the window, plus the change vs the window before it."""
    runs = repo.runs()
    events = repo.events()

    current = _window(events, lambda e: e.timestamp, days)
    prior_cutoff_start = datetime.now() - timedelta(days=days * 2)
    prior_cutoff_end = datetime.now() - timedelta(days=days)
    prior = [e for e in events if prior_cutoff_start <= e.timestamp < prior_cutoff_end]

    def count(source: list[SyncEvent], action: Action) -> int:
        return sum(1 for e in source if e.action is action)

    created_now = count(current, Action.USER_CREATED)
    titles_now = count(current, Action.TITLE_CHANGED)
    failed_now = count(current, Action.FAILED)

    runs_in_window = _window(runs, lambda r: r.started_at, days)
    last_run = runs[0] if runs else None

    return {
        "window_days": days,
        "is_sample_data": repo.is_sample,
        "users_created": {
            "value": created_now,
            "previous": count(prior, Action.USER_CREATED),
        },
        "titles_changed": {
            "value": titles_now,
            "previous": count(prior, Action.TITLE_CHANGED),
        },
        "failures": {
            "value": failed_now,
            "previous": count(prior, Action.FAILED),
        },
        "sync_runs": {
            "value": len(runs_in_window),
            "previous": len([r for r in runs if prior_cutoff_start <= r.started_at < prior_cutoff_end]),
        },
        "users_scanned": sum(r.users_scanned for r in runs_in_window),
        "last_run": last_run.to_dict() if last_run else None,
        "health": _health(runs_in_window),
    }


def _health(runs: list[SyncRun]) -> dict[str, Any]:
    """Overall state, driven by the most recent runs."""
    if not runs:
        return {"state": "unknown", "label": "No sync runs recorded"}

    recent = sorted(runs, key=lambda r: r.started_at, reverse=True)[:4]
    failed = sum(r.failed for r in recent)

    if failed == 0:
        return {"state": "good", "label": "All recent syncs completed cleanly"}

    writes = "write" if failed == 1 else "writes"
    state = "warning" if failed <= 2 else "critical"
    return {"state": state, "label": f"{failed} failed {writes} in recent syncs"}


def build_activity(repo: SyncRepository, days: int = 30) -> list[dict[str, Any]]:
    """Daily counts for the trend chart -- one row per day, zero-filled."""
    events = _window(repo.events(), lambda e: e.timestamp, days)

    by_day: dict[date, dict[str, int]] = defaultdict(
        lambda: {"users_created": 0, "titles_changed": 0, "failures": 0}
    )
    for e in events:
        bucket = by_day[e.timestamp.date()]
        if e.action is Action.USER_CREATED:
            bucket["users_created"] += 1
        elif e.action is Action.TITLE_CHANGED:
            bucket["titles_changed"] += 1
        elif e.action is Action.FAILED:
            bucket["failures"] += 1

    # Zero-fill: a day with no syncs is meaningful and must still plot.
    today = date.today()
    out = []
    for offset in range(days - 1, -1, -1):
        day = today - timedelta(days=offset)
        bucket = by_day.get(day, {"users_created": 0, "titles_changed": 0, "failures": 0})
        out.append({"date": day.isoformat(), **bucket})
    return out


def build_events(
    repo: SyncRepository,
    limit: int = 100,
    action: str | None = None,
    search: str | None = None,
) -> list[dict[str, Any]]:
    events = repo.events()

    if action and action != "all":
        events = [e for e in events if e.action.value == action]

    if search:
        needle = search.strip().lower()
        events = [
            e for e in events
            if needle in e.display_name.lower()
            or needle in e.email.lower()
            or needle in e.detail.lower()
        ]

    return [e.to_dict() for e in events[:limit]]


def build_runs(repo: SyncRepository, limit: int = 20) -> list[dict[str, Any]]:
    return [r.to_dict() for r in repo.runs()[:limit]]
