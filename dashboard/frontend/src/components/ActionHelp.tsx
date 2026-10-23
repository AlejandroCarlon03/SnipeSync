import { useEffect, useRef, useState } from "react";
import { ACTION_INFO } from "../actions";
import { actionTag } from "../lines";

/**
 * "What does this action mean?" reference, opened from a help icon in the top rail.
 */
export function ActionHelp() {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  // Close on Escape or a click outside — the usual popover contract.
  useEffect(() => {
    if (!open) return;

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    const onClick = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };

    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onClick);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onClick);
    };
  }, [open]);

  return (
    <div className="help" ref={rootRef}>
      {open && (
        <div className="pop help__panel" role="dialog" aria-label="Action reference">
          <div className="pop__head">
            <h3>What the actions mean</h3>
            <button
              className="icon-btn icon-btn--quiet"
              onClick={() => setOpen(false)}
              aria-label="Close"
            >
              <svg viewBox="0 0 14 14" width="12" height="12" aria-hidden="true">
                <path d="M3 3l8 8M11 3l-8 8" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
              </svg>
            </button>
          </div>
          <dl className="help__list">
            {ACTION_INFO.map((a) => (
              <div className="help__item" key={a.action}>
                <dt>
                  <span className={`tag tag--${actionTag(a.action)}`}>
                    {a.action}
                  </span>
                </dt>
                <dd>
                  {a.description}
                  <span className="help__source">{a.source}</span>
                </dd>
              </div>
            ))}
          </dl>
        </div>
      )}

      <button
        className="icon-btn"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label="Action reference"
        title="What the actions mean"
      >
        <svg viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
          <circle cx="8" cy="8" r="6.2" fill="none" stroke="currentColor" strokeWidth="1.4" />
          <path d="M6.3 6.2a1.8 1.8 0 1 1 2.5 1.7c-.5.2-.8.6-.8 1.1v.4" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
          <circle cx="8" cy="11.4" r=".85" fill="currentColor" />
        </svg>
      </button>
    </div>
  );
}
