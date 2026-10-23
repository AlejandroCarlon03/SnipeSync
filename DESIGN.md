---
name: SnipeSync Audit Dashboard
description: Every answer on one screen; a packed mosaic of white, hairline-ruled modules in ink and greys, with one muted brick red for the action, the active filter, and what the sync declined.
colors:
  page: "#ffffff"
  surface: "#ffffff"
  surface-2: "#f7f7f7"
  surface-3: "#ececec"
  ink: "#111111"
  ink-2: "#3d3d3d"
  ink-muted: "#6b6b6b"
  rule: "#e0e0e0"
  rule-strong: "#c9c9c9"
  accent: "#b5443c"
  accent-text: "#a23b34"
  accent-ink: "#ffffff"
  accent-wash: "rgba(181, 68, 60, 0.07)"
  accent-glow: "rgba(181, 68, 60, 0.2)"
  anomaly: "#a23b34"
  anomaly-fill: "#b5443c"
  anomaly-wash: "rgba(181, 68, 60, 0.08)"
  anomaly-line: "rgba(181, 68, 60, 0.38)"
  line-offboard: "#111111"
  line-reconcile: "#3d3d3d"
  line-onboard: "#3d3d3d"
  line-licenses: "#6b6b6b"
  good: "#111111"
  bad: "#a23b34"
typography:
  display:
    fontFamily: "Bahnschrift, DIN 2014, DIN Alternate, Segoe UI, system-ui, sans-serif"
    fontSize: "28px"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.01em"
  headline:
    fontFamily: "Bahnschrift, DIN 2014, DIN Alternate, Segoe UI, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 700
    lineHeight: 1.15
    fontVariation: "'wdth' 87.5"
  title:
    fontFamily: "Bahnschrift, DIN 2014, DIN Alternate, Segoe UI, system-ui, sans-serif"
    fontSize: "17px"
    fontWeight: 700
    lineHeight: 1.5
    fontVariation: "'wdth' 87.5"
  title-sm:
    fontFamily: "Bahnschrift, DIN 2014, DIN Alternate, Segoe UI, system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 700
    lineHeight: 1.5
    fontVariation: "'wdth' 87.5"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    fontFeature: "tnum"
  body-sm:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "12.5px"
    fontWeight: 400
    lineHeight: 1.5
    fontFeature: "tnum"
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "11.5px"
    fontWeight: 600
    lineHeight: 1.5
  data:
    fontFamily: "Cascadia Mono, Consolas, ui-monospace, monospace"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.55
rounded:
  sm: "2px"
  mark: "1px"
spacing:
  gap: "12px"
  module: "14px"
  inspector: "16px"
  rail: "248px"
  side: "392px"
  side-inspecting: "440px"
  top: "64px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.sm}"
    padding: "0 14px"
    height: "34px"
  button-ghost:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 14px"
    height: "34px"
  button-ghost-hover:
    backgroundColor: "{colors.surface-2}"
  search:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 14px"
    height: "40px"
  field:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 8px"
    height: "32px"
  segment:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.sm}"
    padding: "0 11px"
    height: "28px"
  segment-active:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
  tag-declined:
    backgroundColor: "{colors.anomaly-wash}"
    textColor: "{colors.anomaly}"
    rounded: "{rounded.sm}"
    padding: "0 6px"
    height: "20px"
  tag-black:
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 6px"
    height: "20px"
  tag-outline:
    textColor: "{colors.ink-2}"
    rounded: "{rounded.sm}"
    padding: "0 6px"
    height: "20px"
  facet-item:
    textColor: "{colors.ink}"
    padding: "4px 12px"
    height: "30px"
  facet-item-active:
    backgroundColor: "{colors.accent-wash}"
    textColor: "{colors.accent-text}"
  module:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.sm}"
    padding: "11px 14px 10px"
  step-passed:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.sm}"
    padding: "6px 7px"
    height: "52px"
  step-here:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.surface}"
    rounded: "{rounded.sm}"
    padding: "6px 7px"
    height: "52px"
  step-declined:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.sm}"
    padding: "6px 7px"
    height: "52px"
  table-row-selected:
    backgroundColor: "{colors.accent-wash}"
  rank:
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    size: "22px"
---

# Design System: SnipeSync Audit Dashboard

## Overview

**Creative North Star: "Dense Modules"**

Every answer sits on one screen. The dashboard is a packed mosaic of white modules, each ruled by its own 1px hairline, so investigating a person never means navigating away. Search runs across the top. A left rail filters by counted lists. Four counts sit in one ruled strip. The audit table takes the wide column, and ranked "Needs a look" and chart modules stack on the right. Opening a row swaps the chart for an inspector whose step strip shows how far the sync got and why it stopped.

The palette is ink on white, four greys, and one muted brick red. The brick started as a utility red and was softened at the user's request ("The red is way too harsh, change the color to something easier on the eyes"), and it does exactly three jobs. Everything that is merely recorded stays ink or grey, so a page full of declined rows reads calm and the one solid brick box in the inspector reads as the strongest mark on screen. Headings and every number use condensed Bahnschrift, a DIN-lineage face that ships with Windows. Body text is Segoe UI Variable.

This replaces "Midnight Line" (navy rail, four line hues, cyan and amber), which the user rejected. Nothing of that world carries over except the route model in the inspector, which is now drawn as a strip of boxed steps rather than a transit line.

**Key Characteristics:**
- White ground (#fff in light); modules separated by their own 1px rules, not by a grey gutter.
- Ink #111 and four greys; one brick red with exactly three uses.
- Square 2px corners everywhere; 1px corners on 8px key marks.
- Condensed Bahnschrift headings and DIN numerals; Segoe UI Variable body at 13px with tabular figures.
- Flat. The only shadow belongs to popovers and the tablet drawer.
- One authored motion: the step strip staggers in.

## Colors

Ink on white with a grey ramp, and a single muted brick that is spent only on a hand, an active choice, or a refusal.

### Primary
- **Muted Brick** (accent / anomaly-fill): the fill of the primary action (Sync), the active segment, the sync progress bar, and the declined step in the inspector's step strip. Also the 2px keyboard focus ring and the focus border on inputs.
- **Brick Text** (accent-text / anomaly): brick as text. Declined tag labels, the active facet's name and count, the "Unmatched" count, verdict leads, links in popovers. Measured 5.84:1 on its wash in light, 6.18:1 in dark.
- **Brick Wash** (accent-wash / anomaly-wash) with **Brick Line** (anomaly-line): the tinted ground and thin frame for declined marks in lists, the active facet row, the selected table row, and the Unmatched stat tile.
- **On Brick** (accent-ink): white text on brick, 5.44:1 in light and 5.47:1 in dark.

### Neutral
- **Paper** (page, surface): the page and every module. In light both are white; in dark the page is #0e0e0e and modules #181818, so modules lift off the page by tone.
- **Inset** (surface-2): table headers, the stat strip's note, panel notes, passed steps, row hover.
- **Recess** (surface-3): skeleton shimmer only.
- **Ink** (ink): headings, names, counts, the status dot when live, the toggle when on, the active sort header, the "this record" step, black-tag text.
- **Ink 2** (ink-2): secondary text, table detail, outline-tag text, icon buttons.
- **Ink Muted** (ink-muted): hints, timestamps, field labels, facet counts, chevrons. Every text token measures at least 4.97:1 on surface and surface-2 in both themes.
- **Rule** (rule): every module border and internal divider.
- **Strong Rule** (rule-strong): control borders (buttons, inputs, segments, steps), the off toggle track, rank squares.

Functions (Offboard, Reconcile, Onboard, Licenses) are told apart by name, not hue: their line tokens map onto the ink ramp. Good is ink; bad is brick text.

### Named Rules
**The Three Uses Rule.** Brick does exactly three things: the primary action (Sync), the active filter or segment, and what the sync declined. The status dot, the toggle, the sort header, and counts are ink. Keyboard focus borrows the brick ring because focus is a hand on a control.

**The Only Actions Are Filled Rule.** Solid brick fills belong to actions (the Sync button, the active segment). Declined marks in lists are a brick wash with a thin brick line. The single exception is the declined step in the inspector's step strip: the one solid brick box on screen, deliberately the strongest mark in the strip. Small 8px key squares (facet marks, the Unmatched tile's key, the "Needs a look" title key) are legend swatches, not fills.

**The Unknown Is Not Zero Rule.** A failed or pending load shows "–", "Unknown until the audit trail loads", or "These rows are missing, not empty". It never shows an empty-range message or an all-clear.

## Typography

**Display Font:** Bahnschrift (with DIN 2014, DIN Alternate, Segoe UI fallback)
**Body Font:** Segoe UI Variable Text (with Segoe UI, system-ui)
**Label/Mono Font:** Cascadia Mono (with Consolas) for timestamps, partitions, and the sync log

**Character:** Condensed DIN for signage and numbers, a neutral Windows sans for reading. Bahnschrift is a Windows system face, chosen deliberately: the app ships only as a Windows Edge app window, so no webfont is loaded.

### Hierarchy
- **Display** (700, 28px, 1.05, -0.01em): stat strip counts only. DIN numerals.
- **Headline** (700, 24px condensed, 1.15): the inspector's record title.
- **Title** (700, 17-19px condensed): module titles ("Audit trail" at 19px, side panels at 17px, popover heads at 16px).
- **Title small** (700, 15px condensed): filter group titles, stat tile labels, the trail title.
- **Body** (400, 13px, 1.5, tabular figures): base text, inputs, facts. Table and lists step down to 12.5px.
- **Label** (600, 11.5px): field labels and tag text (tags at 700). Small meta at 11-12px in ink-muted.
- **Data** (400, 12px mono): absolute UTC time, month partition, sync log at 11.5px.

### Named Rules
**The DIN Numeral Rule.** Every count (stat values, facet counts, rank squares, attempt counts) is set in Bahnschrift. Body numerals stay tabular.

**The Condensed Heading Rule.** Headings are Bahnschrift 700 at 87.5% width. Headings are never uppercase and carry no kicker above them.

## Layout

A sticky 64px top bar (logo, wide search up to 640px, status, auto-refresh, help, theme, Sync) over a shell capped at 1720px. The shell is a 248px filter rail and a workspace, separated by a 12px gap that also separates every module. The workspace stacks the stat strip, then a desk: the audit table module takes the flexible column and a 392px side column holds "Needs a look" and the chart. Inspecting widens the side to 440px, replaces the chart with the inspector, and keeps the anomaly list below it in compact form (max 210px). The rail and side column are sticky and scroll internally.

Module padding is 14px horizontally (16px in the inspector), headers 10-11px vertically. Table rows are 7px by 10px; facet rows 30px minimum.

Responsive: at 1360px the rail drops to 224px and the side to 348px (400px inspecting). At 1080px the shell goes to one column, the rail lays out as a two-column grid across the top, the side panels sit two-up below the table, and the inspector becomes a 480px right drawer over a 40% black scrim. At 860px the top bar wraps search onto its own line and the stat strip goes 2x2. At 600px the rail is single-column and the status text hides.

### Named Rules
**The One Strip Rule.** Stats are one ruled strip of four counts divided by hairlines, never four cards. The "no prior period" note appears once, beneath the strip, not per tile.

**The White Ground Rule.** The page ground is #fff in light. Modules are told apart by their own 1px rules, never by a grey gutter.

## Elevation & Depth

Flat. Modules are white on white, defined only by 1px rules; in dark they lift by tone (#181818 on #0e0e0e). The only shadow is a soft pop shadow on things that float above the mosaic.

### Shadow Vocabulary
- **Pop** (`box-shadow: 0 12px 32px -8px rgba(0,0,0,0.22), 0 0 0 1px rgba(0,0,0,0.06)`; dark `0 16px 40px -8px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.06)`): help and sync popovers, and the inspector as a tablet drawer.
- **Focus wash** (`box-shadow: 0 0 0 3px var(--accent-wash)`): focused search and inputs, and the running sync step.

### Named Rules
**The Ruled Not Raised Rule.** A module at rest has no shadow. If it needs separating, it gets a rule.

## Shapes

Square. Controls, tags, modules, steps and rank squares use a 2px corner; 8px key marks (facet marks, tile keys, trail dots, the route key) use 1px. The only circles are the status dot and the white disc holding the X on the declined step. Borders are 1px throughout; the exceptions are the 3px bottom rule on a line-change step and dashed borders for "not drawn" states (dry-run chip, not-reached step, every step of a dry-run route, skipped sync step).

## Components

### Buttons
- **Shape:** square (2px), 34px tall, 0 14px, 13px bold.
- **Primary:** brick fill, white text. Used for Sync only. Hover mixes 14% black into the brick.
- **Ghost:** white with a strong-rule border and ink text; hover turns the border ink and the ground inset. Export, Try again, Refresh.
- **Icon:** 34px square with a strong-rule border, ink-2 icon; hover or open turns border and icon ink. A quiet 28px borderless variant closes modules.
- **Disabled:** 50% opacity.

### Tags
- **Style:** square (2px), 20px tall, 0 6px, 11.5px bold, 1px border.
- **Declined:** brick wash, brick line, brick text. The loudest tag in any table.
- **Black** (offboarding changes): ink-muted outline, ink text. In dark: strong-rule outline, ink-2 text.
- **Outline** (everything else recorded): pale rule outline, ink-2 text, 600 weight. In dark: ink-muted text.
- **Dry-run chip:** 18px, dashed ink-muted border. Live runs are unmarked.

### Inputs / Fields
- **Search:** 40px, strong-rule border, 2px corner, magnifier in ink-muted. Hover darkens the border to ink-muted; focus turns it brick with a 3px brick wash.
- **Fields** (dates, selects): 32px, same border and focus treatment, 11.5px 600 label above.
- **Toggle:** a real checkbox drawn as a square 30x16 switch; strong-rule track off, ink track on. Never brick.

### Navigation
- **Filter rail:** one module of counted facet lists (Date range, Run type, Action, Function). Each facet row is 30px with an 8px key mark (brick for declined actions, ink for offboarding changes, outline for the rest), a name, and a DIN count right-aligned in ink-muted. The active row gets the brick wash, brick text and bold, and its count turns brick.
- **Segmented control:** strong-rule frame with hairline dividers, 28px; the active segment is a brick fill with white text. Used for Run type and the chart's By action / By month.

### Cards / Containers (modules)
- **Corner Style:** 2px.
- **Background:** surface.
- **Shadow Strategy:** none (see Elevation).
- **Border:** 1px rule. Internal dividers are rules too.
- **Internal Padding:** 14px; header 11px 14px 10px with a rule beneath. Notes sit on inset with a rule beneath.
- **Declined module:** "Needs a look" carries an 8px brick key before its title. The Unmatched stat tile takes the brick wash and its count goes brick.

### Audit table
Sticky 32px header on inset, 12px bold ink-2; sort headers turn ink when active and show an arrow. Rows are hairline-divided, 12.5px; hover is inset. Selection is the brick wash plus a full 1px brick-line outline, never a side stripe. Keyboard focus is a 2px brick outline inset.

### Needs a look
A ranked list of people with no Snipe-IT record. Ranks are 22px square boxes with an inset strong-rule frame and DIN ink numerals; tied people share a rank (competition ranking: 1, 1, 1, 4). No avatars. Each row shows name, mail, attempt count in DIN, and first/last seen.

### Step strip (signature)
The inspector's route, drawn as a row of boxed steps joined by small ink-muted chevrons. Boxes are 52px tall, 2px corners, strong-rule border, 11.5px labels. Six states:
- **Passed:** inset grey fill.
- **This record:** ink fill, white bold label.
- **Declined:** solid brick fill with a white disc holding a brick X and a white bold label; the strongest mark in the strip and the one solid brick box on screen.
- **Reached by another loaded record:** ink outline, ink label, linked to that record.
- **Not reached:** dashed border, ink-muted label.
- **Line change:** ink outline with a 3px bottom rule and a small "Reconcile line" note.
A dry-run route dashes every reached step. Beneath the strip, a declined record's verdict sits in a brick-wash box with a brick-line frame and a brick lead.

### Named Rules
**The One Authored Motion Rule.** Steps stagger in (0.4s, 70ms apart, ease-out from 6px left). That is the only authored motion. Everything else is functional: the inspector and popovers settle in once, spinners turn while work runs, skeletons shimmer while data loads. No infinite pulses.

**The Selection Frame Rule.** Selection is a fill plus a full 1px outline. Never a side stripe.

## Do's and Don'ts

### Do:
- **Do** keep brick to its three uses: the primary action, the active filter or segment, and what the sync declined.
- **Do** draw declined marks in lists as a brick wash with a thin brick line; reserve solid brick for actions and the declined step.
- **Do** keep routine tags quieter than declined tags: black tags are an ink-muted outline with ink text, outline tags a pale rule with ink-2 text, and both step down further in dark.
- **Do** separate modules with their own 1px rules on a #fff ground.
- **Do** set every count in Bahnschrift and keep body numerals tabular.
- **Do** show "–", "Unknown until the audit trail loads", or "These rows are missing, not empty" when a load fails or is pending.
- **Do** give tied ranks the same rank (competition ranking).
- **Do** keep ThemeToggle setting `data-theme` synchronously; ActivityChart reads tokens from computed styles during render.
- **Do** keep every text token at or above 4.97:1 on surface and surface-2 in both themes.

### Don't:
- **Don't** make the status dot, toggle, sort header, or counts brick; they are ink.
- **Don't** fill a declined tag, row, or tile with solid brick.
- **Don't** mark selection with a side stripe.
- **Don't** show zero, an empty-range message, or an all-clear when the data is unknown.
- **Don't** repeat the "no prior period" note on each tile.
- **Don't** add avatars to the ranked list.
- **Don't** add infinite pulses or decorative motion beyond the step stagger.
- **Don't** load a webfont; Bahnschrift and Segoe UI come from Windows.
- **Don't** put a grey gutter between modules or a shadow under a module at rest.
- **Don't** reintroduce per-function hues; functions are told apart by name.
