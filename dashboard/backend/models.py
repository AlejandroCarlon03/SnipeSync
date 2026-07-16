"""Shape of the data the dashboard renders.

These mirror what the SnipeSync Azure Functions would emit if they persisted a
record of their work. Today the functions only call ILogger, so nothing durable
exists yet -- see sample_data.py and the README. Keeping the shape explicit here
means swapping the sample source for a real one is a change of provider, not a
change of contract.
"""

from __future__ import annotations

from dataclasses import dataclass, asdict
from datetime import datetime
from enum import Enum
from typing import Any


class SyncFunction(str, Enum):
    """The two timer-triggered functions in the C# app."""

    ONBOARD = "OnboardEmployeeSync"
    FORMER = "FormerEmployeeSync"


class Action(str, Enum):
    """What the sync did to a single user."""

    USER_CREATED = "user_created"
    TITLE_CHANGED = "title_changed"
    SKIPPED = "skipped"
    FAILED = "failed"


class Status(str, Enum):
    SUCCESS = "success"
    FAILURE = "failure"
    SKIPPED = "skipped"


@dataclass(frozen=True)
class SyncEvent:
    """One user-level outcome within a run."""

    id: str
    run_id: str
    timestamp: datetime
    function: SyncFunction
    action: Action
    status: Status
    display_name: str
    email: str
    detail: str

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        d["timestamp"] = self.timestamp.isoformat()
        d["function"] = self.function.value
        d["action"] = self.action.value
        d["status"] = self.status.value
        return d


@dataclass(frozen=True)
class SyncRun:
    """One execution of one function."""

    id: str
    function: SyncFunction
    started_at: datetime
    finished_at: datetime
    dry_run: bool
    users_scanned: int
    created: int
    title_changed: int
    skipped: int
    failed: int

    @property
    def status(self) -> Status:
        return Status.FAILURE if self.failed else Status.SUCCESS

    @property
    def duration_seconds(self) -> float:
        return (self.finished_at - self.started_at).total_seconds()

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        d["started_at"] = self.started_at.isoformat()
        d["finished_at"] = self.finished_at.isoformat()
        d["function"] = self.function.value
        d["status"] = self.status.value
        d["duration_seconds"] = self.duration_seconds
        return d
