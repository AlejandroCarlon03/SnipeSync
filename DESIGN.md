---
name: SnipeSync Audit Dashboard
description: A warm-paper reading surface for an audit trail, where one blue voice carries every signal.
colors:
  page: "#f9f9f7"
  surface: "#fcfcfb"
  surface-raised: "#ffffff"
  ink-primary: "#0b0b0b"
  ink-secondary: "#52514e"
  ink-muted: "#898781"
  border: "rgba(11, 11, 11, 0.1)"
  border-strong: "rgba(11, 11, 11, 0.16)"
  gridline: "#e1e0d9"
  baseline: "#c3c2b7"
  accent: "#2a78d6"
  accent-wash: "rgba(42, 120, 214, 0.08)"
  series-created: "#2a78d6"
  series-titles: "#1baf7a"
  series-failed: "#d03b3b"
  status-good: "#0ca30c"
  status-good-text: "#006300"
  status-warning: "#fab219"
  status-critical: "#d03b3b"
typography:
  display:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "32px"
    fontWeight: 640
    lineHeight: 1.15
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "15px"
    fontWeight: 650
    lineHeight: 1.5
    letterSpacing: "normal"
  title:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 620
    lineHeight: 1.5
    letterSpacing: "-0.01em"
  body:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: 1.5
    letterSpacing: "0.05em"
rounded:
  pill: "999px"
  sm: "6px"
  md: "8px"
  lg: "12px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "14px"
  lg: "18px"
  xl: "24px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "#ffffff"
    rounded: "{rounded.md}"
    padding: "8px 16px"
    typography: "{typography.body}"
  icon-button:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.md}"
    height: "32px"
    width: "32px"
  icon-button-hover:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.ink-primary}"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.lg}"
    padding: "16px 18px"
  tile:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.lg}"
    padding: "16px 18px"
  search-field:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.md}"
    height: "30px"
    padding: "0 10px"
  segmented-button:
    backgroundColor: "transparent"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.sm}"
    padding: "5px 11px"
  segmented-button-active:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.ink-primary}"
    rounded: "{rounded.sm}"
    padding: "5px 11px"
  table-header:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.label}"
    padding: "9px 18px"
  chip-dry-run:
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.pill}"
    padding: "1px 7px"
  avatar:
    backgroundColor: "{colors.accent-wash}"
    textColor: "{colors.accent}"
    rounded: "{rounded.pill}"
    height: "28px"
    width: "28px"
---

# Design System: SnipeSync Audit Dashboard

## Overview

**Creative North Star: "The Quiet Ledger"**

This is a record you read, not a dashboard that performs. The surface is warm paper —
`#f9f9f7`, not a neutral grey — ruled with warm hairlines at `#e1e0d9`, carrying near-black
ink at `#0b0b0b`. Nothing glows, nothing gradients, nothing floats without cause. The system
behaves like a well-kept logbook: the entries are the point, and the interface's job is to
stay out of their way while making them scannable.

Restraint here is structural, not timid. The palette holds exactly one interactive voice —
a single blue accent — and spends it only on things that are actually interactive or
actually selected. Every other color in the system is either ink, paper, a rule, or a
status. That discipline is what lets a red `#d03b3b` failure read as urgent on a page with
979 rows on it: nothing else is competing for that register.

The system is built for someone who already knows the domain and is reading a lot of rows
quickly. Density is deliberate — 13px table body, 9-10px cell padding, 30px controls — and
the type scale earns its hierarchy through weight and tracking rather than size jumps. The
one place the system raises its voice is the stat tile value: 32px at weight 640 with
-0.025em tracking, because a count is the one thing worth seeing from across the desk.

**Key Characteristics:**
- Warm paper ground, never neutral grey — the whole neutral ramp is tinted
- Exactly one accent voice, spent only on interaction and selection
- Hairline borders define space; shadows are nearly absent at rest
- Dense by intent, tuned for reading many rows fast
- Numeric font weights (550/620/640/650) rather than the 400/700 default pair
- Dark theme is a selected palette, never an inversion

## Colors

A warm, near-neutral ground carrying one blue interactive voice and a strict three-state
status triad.

### Primary
- **Signal Blue** (`#2a78d6` light / `#3987e5` dark): The only interactive voice. Primary
  buttons, focus rings, the active segmented option, sortable-header active state, the
  avatar monogram, row hover wash, and the `created` data series. If something is blue, it
  is either interactive, selected, or the created series — never decoration.
- **Signal Wash** (`rgba(42, 120, 214, 0.08)`): Table row hover and `::selection`. Present
  at 8% so it registers as a hover cue without becoming a highlight.

### Secondary
- **Reclaim Green** (`#1baf7a` light / `#199e70` dark): The `titles` data series in the
  activity chart. Distinct from status green — this is a category, not a verdict.
- **Failure Red** (`#d03b3b` light / `#e66767` dark): The `failed` data series, and shared
  with `status-critical`. The one deliberate overlap in the palette: a failed series and a
  critical status mean the same thing to the reader.

### Neutral
- **Paper** (`#f9f9f7`): The page ground. Warm-tinted, deliberately not `#fafafa`.
- **Card Paper** (`#fcfcfb`): Cards, tiles, panels, sticky table headers. A half-step
  lighter than the page — the separation is tonal before it is ever a shadow.
- **Raised Paper** (`#ffffff`): Only for things that sit above a card: search fields, icon
  buttons, the active segmented pill.
- **Ink** (`#0b0b0b`): Body copy, headings, numbers. Near-black, never pure black in light.
- **Annotation Ink** (`#52514e`): Secondary labels, badge text, segmented rest state.
- **Marginalia Ink** (`#898781`): Column headers, hints, placeholders, timestamps, metadata.
  The only ink shared unchanged between light and dark.
- **Rule** (`rgba(11, 11, 11, 0.1)`): Every border, divider, and cell separator.
- **Strong Rule** (`rgba(11, 11, 11, 0.16)`): Hover state on bordered controls only.
- **Gridline** (`#e1e0d9`) and **Baseline** (`#c3c2b7`): Chart scaffolding. Warm, so the
  chart sits on the same paper as everything else.

### Tertiary
- **Caution Amber** (`#fab219`): Dry-run chips only. Marks a rehearsal, not a problem.
- **Verified Green** (`#0ca30c`, text `#006300` in light): Positive deltas and good run
  status. The text variant exists because `#0ca30c` does not carry enough contrast on warm
  paper at 12px — the darker `#006300` is used wherever green becomes type.

### Named Rules

**The One Voice Rule.** The accent is the only hue that means "you can touch this." Never
introduce a second interactive color, and never use the accent for decoration, emphasis, or
branding fill. Its scarcity is what makes a focus ring legible.

**The Warm Neutral Rule.** Every neutral in this system is tinted warm. A pure `#fff`,
`#fafafa`, or `rgba(0,0,0,…)` grey dropped into this palette reads as a foreign object
against `#f9f9f7`. Match the tint or don't add the color.

**The Selected-Dark Rule.** The dark theme is a chosen set of steps, not an inversion.
Series colors lighten (`#2a78d6` → `#3987e5`), `status-good-text` moves the *opposite*
direction from its light counterpart (`#006300` → `#0ca30c`), and ink-muted stays put. If
you change a series color, re-validate both themes for CVD separation and contrast rather
than computing the counterpart.

## Typography

**Display / Body / Label Font:** `system-ui, -apple-system, "Segoe UI", sans-serif` — one
stack for everything. No webfont is loaded, and none should be: the dashboard is a local
tool that must paint instantly from a loopback origin.

**Character:** The pairing is the operating system's own voice, which is exactly right for a
utility that lives beside the OS. Personality comes from weight and tracking, not from
typeface choice.

### Hierarchy
- **Display** (640, 32px, 1.15, -0.025em): Stat tile values only. The single largest thing
  on the page.
- **Headline** (650, 15px): The product wordmark in the top bar.
- **Title** (620, 14px, -0.01em): Card and panel headings.
- **Body** (400, 13-14px, 1.5): Table cells, detail text, running copy. Base is 14px; tables
  step down to 13px.
- **Label** (600, 11px, 0.05em, uppercase): Table column headers and detail-grid keys.
- **Micro** (550-600, 10.5-12.5px): Chips, badges, hints, metadata, control text.

### Named Rules

**The Numeric Weight Rule.** This system uses 550, 620, 640, and 650 — not 400/500/600/700.
Those in-between weights are what make dense 12-14px type feel deliberate instead of bolded.
Preserve the exact numbers; rounding them to the nearest hundred flattens the hierarchy.

**The Tighter-As-Larger Rule.** Tracking tightens as size grows (-0.025em at 32px, -0.01em
at 14px, normal at 13px, +0.05em at 11px uppercase). Any new size must join this curve.

## Layout

A single centered column, `max-width: 1440px`, with 24px page padding and a 20px gap between
sections. Cards use 16-18px internal padding; the top bar uses 12px/24px.

The stat tiles are a fixed 4-column grid at desktop, collapsing to 2 columns at 1080px and 1
column at 720px. The main body is a `split` grid that collapses to a single column at 1080px.
At 720px the top bar wraps and the health indicator drops to its own full-width row, page
padding drops to 16px, and card heads tighten to 14px.

The top bar is sticky with a translucent `color-mix` background at 88% surface plus a 12px
backdrop blur — the one genuinely modern material gesture in the system, and it earns its
place by keeping filters reachable while scrolling 500 rows.

Table headers are independently sticky within their scroll container, and long values wrap
via `overflow-wrap: anywhere` rather than widening the table.

**The Nowrap Table Rule.** Table cells are `white-space: nowrap` and the wrapper scrolls
horizontally. Row content never reflows into a second line; the expanded detail row is where
long content goes.

## Elevation & Depth

**Flat at rest, tonal by construction.** Depth in this system comes from a three-step paper
ramp — page `#f9f9f7`, card `#fcfcfb`, raised `#ffffff` — and from hairline borders. The two
shadow tokens are so light in the light theme (`0 1px 2px` at 4% and `0 1px 3px` at 3%) that
they function as edge definition rather than lift.

Real elevation appears only in response to state or genuine floating: the active segmented
pill takes `--shadow-card` to lift out of its track, the help FAB carries its own
`0 3px 12px / 25%`, and the help popover carries `0 8px 28px / 28%`.

In dark, shadows invert their role — `0 1px 2px rgba(0,0,0,0.4)` is doing real work, because
tonal separation between `#0d0d0d` and `#1a1a19` is subtler than paper-on-paper.

### Shadow Vocabulary
- **Card** (`0 1px 2px rgba(11,11,11,0.04), 0 1px 3px rgba(11,11,11,0.03)`): Cards, tiles,
  the active segmented pill. Edge definition, not lift.
- **Pop** (`0 4px 16px rgba(11,11,11,0.1), 0 1px 3px rgba(11,11,11,0.06)`): Reserved for
  genuinely floating surfaces.

### Named Rules

**The Border-First Rule.** Separation is a 1px `--border` hairline before it is ever a
shadow. If a new surface needs to feel distinct, give it the border and the next paper step;
reach for a shadow only when it actually floats above the page.

## Shapes

Rounded, never soft. Four radii carry the whole system: **12px** (`--radius`) for the large
containers — cards and tiles; **8px** (`--radius-sm`) for controls and inner panels — icon
buttons, search fields, segmented tracks, primary buttons; **6-7px** for controls nested
inside other controls — segmented options, filter inputs; and **999px** for chips, tags, and
badge dots.

Circles are reserved for identity and affordance: the 28px avatar and the 38px help FAB.

Borders are uniformly 1px. There are no dashed borders, no double borders, and no decorative
strokes anywhere in the system.

**The Nesting Radius Rule.** A nested element's radius is always smaller than its parent's —
6px options inside an 8px track, 8px controls inside a 12px card. Never match or exceed the
parent radius.

## Components

### Buttons
- **Shape:** 8px radius (`--radius-sm`), 1px border.
- **Primary:** Accent fill, white text, 8px/16px padding, weight 550.
- **Icon:** 32×32 grid-centered, raised paper fill, `--border` stroke, secondary ink.
  Hover moves ink to primary and border to `--border-strong` over 0.12s — color only, never
  a transform.
- **Ghost:** Transparent fill with a `--border` stroke; hover shifts both border and text to
  the accent.
- **Disabled:** `opacity: 0.55-0.6` with `cursor: default`. Never a color change.

### Segmented Control
- **Track:** 5% ink wash, 8px radius, 2px inner padding.
- **Option:** Transparent, secondary ink, weight 550, 12.5px, 6px radius.
- **Active:** Raised paper fill, primary ink, plus `--shadow-card` — the pill lifts out of
  its track. This is the system's clearest example of shadow-as-state.

### Cards
- **Corners:** 12px. **Background:** card paper. **Border:** 1px `--border`.
- **Shadow:** `--shadow-card` (edge definition; see Elevation).
- **Head:** 16px/18px padding, bottom hairline, title plus optional muted 12px subtitle,
  with controls right-aligned and allowed to wrap.
- **Body:** 16px/18px, with a `--flush` variant at zero padding for tables.

### Stat Tiles
Card shell plus an 8px 2px-radius series swatch, a 12.5px/550 label that truncates with
ellipsis, the 32px display value, and a footer carrying a delta and a hint. Delta color is
semantic: `--status-good-text` up, `--status-critical` down, `--ink-muted` flat, with the
arrow glyph rotated 180° rather than swapped.

### Inputs / Search
30px tall, raised paper, 8px radius, muted-ink icon, borderless transparent `<input>` inside
the shell. Focus is handled by the *container* (`:focus-within` shifts the border to accent),
never by an inner outline. Placeholder is muted ink.

### Tables
Sticky card-paper header row, 11px uppercase label type in muted ink with 0.05em tracking.
Body cells 10px/18px, hairline separated, last row's border removed. Row hover is
`--accent-wash`. Sortable headers are real `<button>`s that inherit the `th`'s typography and
reserve 8px of arrow width so headers don't shift when the sort column moves. Expanded detail
rows sit on a 4% accent wash with a definition grid at 220px minimum columns.

### Person Cell
28px circular avatar at 14% accent wash with accent monogram at 10.5px/650, beside a stacked
name (550) and email (11.5px muted). Both lines nowrap.

### Chips and Badges
Pill chips at 10.5px/600 with 1px/7px padding. `--dry` is amber at 18% fill with a 45% border;
`--fail` is critical red at 14% fill with a 40% border and red text. Badges are dot-plus-label:
a 7px circle carrying the status color beside secondary-ink text — **the dot carries the color,
the text never does.**

### Floating Help
38px circular accent FAB fixed 24px from the bottom-right at `z-index: 50`, hovering via
`filter: brightness(1.1)`. Its panel is `min(420px, 100vw - 48px)`, capped at
`min(70vh, 560px)` with internal scroll, on card paper with an 8px radius.

## Do's and Don'ts

### Do:
- **Do** spend the accent only on interaction, selection, and the created series. If a new
  element is not interactive, it does not get blue.
- **Do** keep every neutral warm-tinted to match `#f9f9f7` / `#e1e0d9`.
- **Do** use the exact numeric weights (550, 620, 640, 650) already in the system.
- **Do** define separation with a 1px `--border` hairline and the next paper step before
  considering a shadow.
- **Do** put focus on the container via `:focus-within` for composite controls, and keep the
  global `:focus-visible` ring (2px accent, 2px offset) intact — it is deliberately absent
  for mouse users.
- **Do** give every status a shape or position cue alongside its color — the dot, the arrow,
  the chip border — so meaning survives color-vision deficiency.
- **Do** keep transitions at 0.12s and limited to color and border-color.

### Don't:
- **Don't** invert the light palette to produce dark values. Both themes are hand-selected
  and CVD-validated; derive nothing.
- **Don't** introduce a second accent hue, a gradient, or a saturated fill. The system has
  one voice.
- **Don't** animate transforms or add motion beyond the existing 0.7s refresh spinner.
- **Don't** use `#0ca30c` for green *text* in the light theme — that is what
  `--status-good-text` (`#006300`) exists for.
- **Don't** let table cells wrap. Long content belongs in the expanded detail row.
- **Don't** match or exceed a parent's corner radius on a nested element.
- **Don't** add a webfont. The system stack is a deliberate choice for a local-first tool.
