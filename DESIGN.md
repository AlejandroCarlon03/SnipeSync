---
name: SnipeSync Audit Dashboard
description: The sync drawn as a transit network on midnight enamel; four line inks, one cyan interactive voice, amber for whatever the sync declined to act on.
colors:
  page: "#e8edf5"
  surface: "#ffffff"
  surface-2: "#f2f5fa"
  surface-3: "#e6ecf5"
  ink: "#0a1733"
  ink-2: "#33425f"
  ink-muted: "#56647f"
  rule: "rgba(10, 30, 70, 0.11)"
  rule-strong: "rgba(10, 30, 70, 0.22)"
  dots: "rgba(10, 30, 70, 0.07)"
  accent: "#0a7491"
  accent-text: "#08677f"
  accent-ink: "#ffffff"
  accent-wash: "rgba(10, 116, 145, 0.09)"
  accent-glow: "rgba(10, 116, 145, 0.18)"
  line-offboard: "#c93d29"
  line-reconcile: "#6a4ed6"
  line-onboard: "#11805a"
  line-licenses: "#2360d2"
  anomaly: "#945c00"
  anomaly-fill: "#f4a81c"
  anomaly-wash: "rgba(244, 168, 28, 0.14)"
  good: "#11805a"
  bad: "#c93d29"
  rail: "#061127"
  rail-2: "#0d1d3c"
  rail-ink: "#e8f0ff"
  rail-ink-2: "#a9bbdc"
  rail-rule: "rgba(140, 180, 255, 0.16)"
  rail-accent: "#35d0ee"
  rail-accent-ink: "#03202a"
typography:
  display:
    fontFamily: "Bahnschrift, DIN Alternate, DIN 2014, Segoe UI, system-ui, sans-serif"
    fontSize: "40px"
    fontWeight: 600
    lineHeight: 1.05
    letterSpacing: "-0.01em"
  headline:
    fontFamily: "Bahnschrift, DIN Alternate, DIN 2014, Segoe UI, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 600
    lineHeight: 1.15
  title:
    fontFamily: "Bahnschrift, DIN Alternate, DIN 2014, Segoe UI, system-ui, sans-serif"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.5
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    fontFeature: "tnum"
  label:
    fontFamily: "Bahnschrift, DIN Alternate, DIN 2014, Segoe UI, system-ui, sans-serif"
    fontSize: "11px"
    fontWeight: 500
    lineHeight: 1.5
    letterSpacing: "0.06em"
    fontVariation: "wdth 87.5"
  data:
    fontFamily: "Cascadia Mono, Consolas, ui-monospace, monospace"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.55
rounded:
  tick: "3px"
  sm: "4px"
  plaque: "6px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.sm}"
    height: "32px"
    padding: "0 13px"
  button-primary-rail:
    backgroundColor: "{colors.rail-accent}"
    textColor: "{colors.rail-accent-ink}"
    rounded: "{rounded.sm}"
    height: "32px"
    padding: "0 13px"
  button-ghost:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.sm}"
    height: "32px"
    padding: "0 13px"
  icon-button:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.sm}"
    height: "32px"
    width: "32px"
  field-input:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    height: "32px"
    padding: "0 10px"
  search-field:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    height: "36px"
    padding: "0 12px"
  segmented-track:
    backgroundColor: "{colors.surface-3}"
    rounded: "{rounded.sm}"
    padding: "2px"
  segmented-button:
    backgroundColor: "transparent"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.tick}"
    height: "26px"
    padding: "0 10px"
  segmented-button-on:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.accent-text}"
    rounded: "{rounded.tick}"
    height: "26px"
    padding: "0 10px"
  panel:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.plaque}"
    padding: "14px 16px"
  board-tile:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    typography: "{typography.display}"
    padding: "16px 20px 15px"
  table-header:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.label}"
    height: "34px"
    padding: "0 10px"
  action-chip:
    textColor: "{colors.ink}"
    rounded: "{rounded.pill}"
    height: "22px"
    padding: "0 8px 0 7px"
  chip:
    textColor: "{colors.ink-2}"
    rounded: "{rounded.tick}"
    height: "20px"
    padding: "0 7px"
  chip-dry:
    textColor: "{colors.anomaly}"
    rounded: "{rounded.tick}"
    height: "20px"
    padding: "0 7px"
  rail:
    backgroundColor: "{colors.rail}"
    textColor: "{colors.rail-ink}"
    height: "56px"
    padding: "0 24px"
---

# Design System: SnipeSync Audit Dashboard

## Overview

**Creative North Star: "Midnight Line"**

The sync is a transit network. Four functions are four lines (offboard, reconcile, onboard,
licenses), and every audited action is a station a person reached on one of them. The
dashboard is the network map and the departures board at once: a navy rail across the top,
a ruled filter strip, one connected status band where the four counts sit as stations on a
shared track, and a ledger of records whose inspector draws the route the person travelled
and where it stopped.

The material is enamel signage. The dark theme is midnight enamel (navy ground with a faint
dot grid, porcelain ink); the light theme is porcelain enamel with the same dot grid in
navy. Each theme's steps are chosen for its own ground, not inverted. Titles and numerals are
set in Bahnschrift, the DIN 1451 lineage of road and rail lettering. Containers are 6px
plaques edged with 1px rules; stations are ringed circles.

Colour is spent by meaning, never by decoration. Line inks identify which line something
belongs to; amber marks anything the sync declined to act on; cyan is the only voice that
means "you can touch this". The system is dense, built for an operator scanning hundreds of
rows and opening one to read why.

**Key Characteristics:**
- Navy rail in both themes; porcelain or midnight enamel page beneath, with an 18px dot grid
- Four line inks for identity, amber for refusals, cyan alone for interaction
- Bahnschrift for numerals, titles, and uppercase labels; Segoe UI Variable for running text
- 6px plaques, 1px rules, ringed-circle stations
- Shape always carries meaning alongside colour: ring vs dot, dashed vs solid
- Tabular numerals everywhere

## Colors

A navy-and-enamel ground carrying four line inks, one amber anomaly signal, and one cyan
interactive voice. Light values are in the frontmatter; dark values are listed here as the
theme's own selections.

### Primary
- **Signal Cyan** (accent; dark `#35d0ee`): The only interactive voice. Primary buttons,
  focus rings, the on-state of toggles, hovered borders on inputs and icon buttons, row hover
  wash, selection fill and frame, completed sync steps. Text in this hue uses
  `accent-text` (dark `#62dcf4`), never `accent`, so it holds 4.5:1 on surfaces.
- **Rail Cyan** (rail-accent): The same voice as it appears on the navy rail, constant in both
  themes. Rail controls (Sync button, rail toggle, rail focus ring, progress bar) use it, with
  `rail-accent-ink` as the text on its fill.

### Secondary (the line inks)
- **Offboard Coral** (line-offboard; dark `#ff7f6b`): The offboard line.
- **Reconcile Violet** (line-reconcile; dark `#b49cff`): The reconciliation retry line.
- **Onboard Green** (line-onboard; dark `#46d89e`): The onboard line.
- **Licenses Blue** (line-licenses; dark `#67a3ff`): The license line.

Line inks appear as station rings, route segments, the 14px stroke before a line name, action
chip dots and tints, chart bars, and the status band's track. They identify; they never mean
"good" or "bad".

### Tertiary
- **Anomaly Amber** (anomaly for text, `#945c00` light / `#ffc143` dark; anomaly-fill for
  shapes, `#f4a81c` light): Everything the sync declined to act on. Unmatched action chips,
  the lit station on a non-zero anomaly tile, stopped route nodes, the unmatched panel and its
  avatars, dry-run chips, completed-with-errors outcomes. The light theme splits text
  (`anomaly`) from fill (`anomaly-fill`) because the saturated amber cannot carry text.
- **Good / Bad** (good, bad): Delta direction on tiles and sync outcomes. They share hues with
  onboard and offboard, but they appear only on deltas, outcomes, failed steps, and the error
  alert.

### Neutral
- **Enamel Page** (page; dark `#061127`): The ground, under the dot grid.
- **Plaque** (surface; dark `#0b1a36`): Panels, the status band, the inspector, popovers.
- **Inset** (surface-2; dark `#0f2244`): Inputs, table header, the old-to-new change block.
- **Recess** (surface-3; dark `#142a52`): Segmented track, skeleton base.
- **Ink / Ink-2 / Ink-muted** (dark `#e8f0ff` / `#b2c2e0` / `#8a9dc2`): Primary text,
  secondary text, metadata and labels. All three measure at least 4.5:1 on surface and
  surface-2 in both themes.
- **Rule / Rule-strong**: Every 1px border and divider; rule-strong for control borders,
  not-reached route dashes, and the trail's spine.
- **Dots**: The map-sheet dot grid on the page ground.
- **Rail family** (rail, rail-2, rail-ink, rail-ink-2, rail-rule): Navy enamel, constant
  across themes. The sync log reuses it as a terminal well.

### Named Rules

**The One Voice Rule.** Cyan is the only interactive colour. A line ink never marks a
control, and cyan never identifies a line.

**The Amber Refusal Rule.** Amber is reserved for anything the sync declined to act on. It is
never used for emphasis, warning copy, or decoration. Unmatched action chips draw a ring
instead of a dot, so shape carries the meaning as well as colour.

**The Navy Rail Rule.** The top rail is navy in both themes, and its own controls use
`rail-accent`. Popovers opened from the rail return to the page palette and the page accent.

**The Measured Contrast Rule.** Every text token holds at least 4.5:1 on surface and
surface-2 in both themes. A new text colour is measured in both before it ships.

## Typography

**Display Font:** Bahnschrift (with DIN Alternate, DIN 2014, Segoe UI)
**Body Font:** Segoe UI Variable Text (with Segoe UI, system-ui)
**Label/Mono Font:** Bahnschrift condensed for labels; Cascadia Mono (with Consolas) for data

**Character:** Signage over text. Bahnschrift's DIN lettering sets every number and title the
way a station sign would; Segoe UI Variable carries the reading. Bahnschrift is a Windows
system face, chosen deliberately because the app ships only as a Windows Edge app window. No
webfont is loaded.

### Hierarchy
- **Display** (600, 40px, 1.05, -0.01em): Status band counts only. Drops to 32px under 600px.
- **Headline** (600, 24px, 1.15): The inspector's record title.
- **Title** (600, 16px): Panel headings; the ledger heading steps up to 18px, popover
  headings to 15px, trail heading to 14px.
- **Body** (400, 13px, 1.5): Table cells, inspector facts, verdict text. Base is 14px with
  tabular numerals; dense surfaces step down to 12.5-13px. Meta and hints at 11.5-12px.
- **Label** (500, 11px, 0.06em, uppercase, 87.5% width): Table headers, field labels,
  fact keys. Tile labels use the same treatment at 13px/0.04em; the route line name at 12px.
- **Data** (400, 11.5-12px): Identifiers in the inspector and the sync log.

### Named Rules

**The Signage Rule.** Numerals and titles are Bahnschrift; sentences are Segoe UI. A count
set in the body face, or a paragraph set in Bahnschrift, is off-system.

**The Condensed Label Rule.** Uppercase labels are always Bahnschrift at 87.5% width with
positive tracking. Uppercase appears nowhere else.

## Layout

A sticky 56px rail, then a workspace capped at 1680px with a 24px gutter (16px at 1080px and
below) and 16px gaps between bands. Top to bottom: the filter strip (labels above values, one
ruled plaque), the status band (one plaque, four equal columns divided by 1px rules), then the
desk: the ledger takes the remaining width and a sticky side column holds the overview or the
inspector. The side column is 420px (460px while inspecting), 360px / 420px under 1240px,
sticky beneath the rail and scrolling internally.

Under 1080px the desk becomes one column, the overview panels sit two-up below the ledger, and
the inspector becomes a right-hand drawer (up to 480px) over a navy scrim. Under 860px the
status band becomes 2×2 and the side panels stack.

Table cells do not wrap; user and detail cells truncate with an ellipsis and the inspector
carries the full value. Below 1080px user names wrap instead of truncating.

## Elevation & Depth

Flat enamel with tonal layering. Depth comes from the page, surface, surface-2, surface-3
steps and 1px rules. Plaques carry a near-invisible `shadow-1` for edge definition; only
genuinely floating things (popovers, the mobile inspector drawer) take `shadow-pop`. Glow is
the one light-emitting gesture, and it belongs to state: the primary button's 4px cyan halo on
hover, the pulsing live dot on the rail, the running sync step, and the "here" station on a
route.

### Shadow Vocabulary
- **Plaque edge** (`shadow-1`; light `0 1px 2px rgba(10,30,70,0.06), 0 2px 6px rgba(10,30,70,0.04)`, dark `0 1px 2px rgba(0,4,16,0.5)`): Filter strip, status band, panels, inspector, active segmented option.
- **Pop** (`shadow-pop`; light `0 18px 48px -12px rgba(10,30,70,0.28), 0 2px 6px rgba(10,30,70,0.08)`): Popovers and the drawer.
- **Accent halo** (`0 0 0 4px var(--accent-glow)`): Primary button hover, and the 3px focus halo on inputs and search.

### Named Rules

**The Rule-First Rule.** Separation is a 1px rule and the next surface step. A shadow is only
for something that actually floats.

## Shapes

Three radii and the circle. **6px plaques** for every container (filters, status band,
panels, inspector, popovers, alerts). **4px** for controls (buttons, icon buttons, inputs,
search, segmented track, verdict and change blocks, sync log). **3px ticks** for elements
nested in a control or tiny marks (segmented options, status chips, skeletons). **999px**
only for action chips and toggle tracks.

Circles are stations: tile stations (13px, 3px ring), route nodes (17px, 3.5px ring), the
route roundel (18px, 4px ring), trail dots, sync step dots, avatars. Lines are bars: route
segments 5px with rounded ends, the band track 2px, the line-badge stroke 14×3px.

Borders are 1px. Dashing is a meaning, not a style: a dashed border or segment says "not
built" (a dry run, a stop not reached, a skipped sync step).

**The Dashed Is Unbuilt Rule.** Dry runs are dashed, like a line under construction; live is
the default and carries no marker. Not-reached stops and skipped steps are dashed for the same
reason. Never dash anything decoratively.

## Components

### Buttons
- **Shape:** 4px radius, 32px tall, 0 13px padding, 13px/600, icons 13-15px inline SVG.
- **Primary:** Accent fill, accent-ink text. Hover adds a 4px accent-glow halo, no colour or
  transform change. On the rail, primary uses rail-accent and rail-accent-ink.
- **Ghost:** Surface fill, rule-strong border, ink-2 text; hover moves border to accent and
  text to accent-text.
- **Icon:** 32px square (28px quiet variant, borderless), rule-strong border; hover inks the
  glyph and accents the border. On the rail: transparent with rail-rule borders.
- **Disabled:** opacity 0.55, default cursor.

### Chips
- **Action chip:** Pill, 22px, 12px/600 ink text on a 13% tint of the action's line ink with
  a 38% border and a 7px dot in the ink. Unmatched actions use the amber ring.
- **Status chip:** 3px tick corners, 20px, 11.5px/600, rule-strong border. Dry-run is the
  dashed amber variant.
- **Line badge:** A 14×3px stroke in the line ink, then the line name in ink-2.

### Cards / Containers
- **Corner Style:** 6px plaque.
- **Background:** surface, with surface-2 insets.
- **Shadow Strategy:** shadow-1 edge only (see Elevation).
- **Border:** 1px rule; anomaly panels mix 40% anomaly-fill into the rule and wash the head
  with anomaly-wash.
- **Internal Padding:** heads 14px 16px 12px; inspector sections inset 18px.

### Inputs / Fields
- **Style:** 32px (search 36px), surface-2 fill, rule-strong border, 4px radius, Bahnschrift
  condensed label above.
- **Focus:** border to accent plus a 3px accent-glow halo; search focuses on the container
  via focus-within and tints its icon accent-text.
- **Toggle:** a real checkbox drawn as a 28×16 switch; on is accent (rail-accent on the rail).
- **Segmented:** surface-3 track, transparent options; on is surface fill, accent-text, shadow-1.

### Navigation
The rail: 56px navy, logo plus Bahnschrift wordmark at 19px with a ruled subtitle in
rail-ink-2, then status (a 7px live dot that pulses rail-accent), the Sync button, and icon
buttons on the right. Focus rings on the rail are rail-accent. A 3px rail-accent progress bar
sits directly under the rail while a sync runs.

### Status Band (signature)
One plaque, four stations on a shared track. Each tile has a 13px ringed station in its line
ink, a condensed uppercase label, and a 2px track that runs from its station through the
divider into the next tile, fading toward the right. Below: the 40px Bahnschrift count, a
muted hint, and a delta in good/bad/muted with an SVG arrow rotated for direction. A tile
flagged as an anomaly with a non-zero count lights up: amber station fill with a 4px wash
ring, amber count, and an amber wash fading down the tile.

### Ledger Table
Sticky surface-2 header in label type, 34px tall. Rows 9px 10px, separated by rules, first
column inset 16px. Hover is accent-wash. **Selection is a 16% accent fill plus a full 1px
accent outline, never a side stripe.** Sortable headers are buttons whose arrow fades in on
hover and turns accent-text when active.

### Route Strip (signature)
In the inspector, between two rules: a ringed roundel and the line name, then the record's
stops on an even grid. Each stop owns the segment arriving from the one before; segments draw
left to right on open with a 110ms stagger. Six stop states:
- **passed:** ringed node in the line ink, solid segment.
- **here:** 21px node filled with the line ink, surface ring, outer ink ring and glow; label 650.
- **stopped:** 21px amber-filled node with a 4px amber-wash ring; label 650.
- **later:** reached by another loaded record; segment at 55% opacity, label is a link to that record.
- **not-reached:** 13px node with a 2px rule-strong ring, dashed rule-strong segment, muted label.
- **transfer:** interchange onto another line; segment blends from this line's ink to the
  other's, node ringed twice in the other line's ink.

On a dry run, reached segments are dashed in the line ink.

**The Honest Route Rule.** Never draw a stop as not-reached when the loaded data shows it was
reached. Stops come from the action vocabulary and the person's loaded records, never from
invention.

### Inspector
A plaque that slides in 10px from the right over 0.35s. Head: action chip, 24px Bahnschrift
title, muted relative time, close icon. Then the route strip, a verdict (amber-washed block
when the sync declined), an old-to-new change block with the old value struck through in
bad, a two-column fact list with condensed labels, and the person's trail: a vertical spine of
ringed dots in each record's line ink, the open record filled.

### Popovers
Absolute under their trigger, min(420px, 100vw - 32px), on surface with rule-strong border,
6px radius, shadow-pop, sticky head. They use the page palette even when opened from the
rail. The sync popover draws its steps as a vertical line: done stations filled accent,
running pulsing, failed filled bad, skipped dashed.

### Activity Chart
Recharts, horizontal bars per action in each action's line ink (amber for unmatched), or a
month area in accent. Colours are resolved from tokens during render, keyed on the theme
prop, because Recharts needs literal values.

## Do's and Don'ts

### Do:
- **Do** give every line-owned element its line ink through a `--tone` variable, and every
  refusal amber.
- **Do** pair colour with shape: ring vs dot for unmatched, dashed vs solid for unbuilt,
  filled vs ringed for here vs passed.
- **Do** set numerals and titles in Bahnschrift and keep tabular numerals on.
- **Do** keep the rail navy in both themes and use rail-accent for its controls.
- **Do** set `data-theme` synchronously in ThemeToggle, and resolve chart colours from tokens
  during render so a theme switch repaints the chart.
- **Do** measure any new text colour at 4.5:1 on surface and surface-2 in both themes.
- **Do** draw selection as a fill plus a full 1px outline.

### Don't:
- **Don't** use amber for anything except what the sync declined to act on.
- **Don't** mark live runs; only dry runs carry a marker, and that marker is a dash.
- **Don't** draw a stop as not-reached when a loaded record shows it was reached.
- **Don't** use a side stripe to mark selection or state.
- **Don't** use a line ink on a control, or cyan to identify a line.
- **Don't** load a webfont; Bahnschrift ships with Windows and the app ships only there.
- **Don't** compute dark values by inverting light ones; each theme's steps are chosen for
  its own ground.
- **Don't** use rounded pills for anything but action chips and toggle tracks.
