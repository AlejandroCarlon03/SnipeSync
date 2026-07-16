"""Generates the sample dataset the dashboard currently renders.

THIS IS NOT REAL DATA. The SnipeSync functions do not yet persist what they do,
so there is nothing real to read. This module fabricates a plausible 30 days of
sync history so the UI can be designed against realistic shapes -- varying
volumes, quiet weekends, the occasional failure, a dry run.

To wire up real data, replace this provider (see repository.py) rather than
editing the UI.
"""

from __future__ import annotations

import random
from datetime import datetime, timedelta

from models import Action, Status, SyncEvent, SyncFunction, SyncRun

# Fixed seed: the sample set should look the same every launch, so UI changes are
# the only thing that moves between screenshots.
SEED = 20260715

FIRST_NAMES = [
    "Ada", "Grace", "Alan", "Katherine", "Linus", "Margaret", "Dennis", "Barbara",
    "Ken", "Radia", "James", "Anita", "Tim", "Shafi", "Vint", "Frances",
    "Guido", "Carol", "Bjarne", "Jean", "Rasmus", "Sophie", "Yukihiro", "Marissa",
]
LAST_NAMES = [
    "Lovelace", "Hopper", "Turing", "Johnson", "Torvalds", "Hamilton", "Ritchie",
    "Liskov", "Thompson", "Perlman", "Gosling", "Borg", "Berners-Lee", "Goldwasser",
    "Cerf", "Allen", "Rossum", "Shaw", "Stroustrup", "Bartik", "Lerdorf", "Wilson",
]
TITLES = [
    "Software Engineer", "Account Executive", "IT Support Specialist",
    "Sales Manager", "HR Coordinator", "Data Analyst", "Project Manager",
    "Systems Administrator", "Marketing Associate", "Finance Analyst",
]
FAILURE_REASONS = [
    "Snipe-IT returned 422: username already taken",
    "Snipe-IT returned 500: internal server error",
    "Request timed out after 3 retries",
    "Ambiguous name match -- 2 users share this name, skipped to avoid a wrong write",
]


def _domain(first: str, last: str) -> str:
    return f"{first.lower()}.{last.lower().replace('-', '')}@contoso.com"


def generate(days: int = 30, now: datetime | None = None) -> tuple[list[SyncRun], list[SyncEvent]]:
    """Builds `days` of sync history ending at `now`."""
    rng = random.Random(SEED)
    now = now or datetime.now().replace(microsecond=0)

    runs: list[SyncRun] = []
    events: list[SyncEvent] = []
    used_names: set[tuple[str, str]] = set()

    def fresh_name() -> tuple[str, str]:
        for _ in range(200):
            pair = (rng.choice(FIRST_NAMES), rng.choice(LAST_NAMES))
            if pair not in used_names:
                used_names.add(pair)
                return pair
        return rng.choice(FIRST_NAMES), rng.choice(LAST_NAMES)

    # The functions are both on a "0 0 2 * * *" timer -- 02:00 daily.
    for day_offset in range(days - 1, -1, -1):
        day = (now - timedelta(days=day_offset)).replace(hour=2, minute=0, second=0)
        if day > now:
            continue

        # Hiring and offboarding slow down at weekends.
        weekend = day.weekday() >= 5
        volume = 0.25 if weekend else 1.0

        for function in (SyncFunction.ONBOARD, SyncFunction.FORMER):
            run_id = f"{function.value[:2].lower()}-{day:%Y%m%d}"
            started = day + timedelta(seconds=rng.randint(0, 90))

            scanned = rng.randint(180, 260)
            actionable = max(0, int(rng.triangular(0, 5, 1.5) * volume))

            # One dry run in the recent past, so the badge has something to show.
            dry_run = day_offset == 3 and function is SyncFunction.ONBOARD

            created = title_changed = failed = skipped = 0
            run_events: list[SyncEvent] = []

            for i in range(actionable):
                first, last = fresh_name()
                name = f"{first} {last}"
                email = _domain(first, last)
                at = started + timedelta(seconds=rng.randint(5, 200))

                # ~8% of attempted writes fail.
                if rng.random() < 0.08:
                    failed += 1
                    action, status = Action.FAILED, Status.FAILURE
                    detail = rng.choice(FAILURE_REASONS)
                elif function is SyncFunction.ONBOARD:
                    created += 1
                    action, status = Action.USER_CREATED, Status.SUCCESS
                    detail = f"Created with title '{rng.choice(TITLES)}'"
                else:
                    title_changed += 1
                    action, status = Action.TITLE_CHANGED, Status.SUCCESS
                    detail = f"{rng.choice(TITLES)} -> Former Employee"

                if dry_run and status is Status.SUCCESS:
                    detail = f"[DRY RUN] Would have {detail[0].lower()}{detail[1:]}"

                run_events.append(
                    SyncEvent(
                        id=f"{run_id}-{i}",
                        run_id=run_id,
                        timestamp=at,
                        function=function,
                        action=action,
                        status=status,
                        display_name=name,
                        email=email,
                        detail=detail,
                    )
                )

            # Users the sync looked at but had no reason to touch.
            skipped = rng.randint(0, 3)

            finished = started + timedelta(seconds=rng.randint(8, 45))
            runs.append(
                SyncRun(
                    id=run_id,
                    function=function,
                    started_at=started,
                    finished_at=finished,
                    dry_run=dry_run,
                    users_scanned=scanned,
                    created=created,
                    title_changed=title_changed,
                    skipped=skipped,
                    failed=failed,
                )
            )
            events.extend(run_events)

    runs.sort(key=lambda r: r.started_at, reverse=True)
    events.sort(key=lambda e: e.timestamp, reverse=True)
    return runs, events
