import { useEffect, useRef, useState } from "react";
import { ACTION_INFO } from "../actions";

/**
 * "What does this action mean?" reference, opened from a help icon. Anchored
 * bottom-right so it stays out of the way of the table it explains.
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
        <div className="help__panel" role="dialog" aria-label="Action reference">
          <div className="help__head">
            <h3>What the actions mean</h3>
            <button
              className="help__close"
              onClick={() => setOpen(false)}
              aria-label="Close"
            >
              ×
            </button>
          </div>
          <dl className="help__list">
            {ACTION_INFO.map((a) => (
              <div className="help__item" key={a.action}>
                <dt>
                  <span className="tag">{a.action}</span>
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
        className="help__btn"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-label="Action reference"
        title="What the actions mean"
      >
        ?
      </button>
    </div>
  );
}
