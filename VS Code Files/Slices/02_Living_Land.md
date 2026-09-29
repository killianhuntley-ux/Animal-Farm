# 02 — The Living Land

**Question this slice answers:** *Is shaping the land satisfying in itself — and can a designer author a new "condition" without writing code?*
**GDD:** §2.2 (env conditions are the core machine), §2.6 (verbs), §4.1 (barren→lush), §6.2 (the one system worth over-engineering)

Two deliverables share this slice on purpose: the player-facing land loop, and the invisible **requirement engine** that the entire game hangs off. They're built together because conditions are *about* land state.

---

## Builds (systems)

### A. Land & plants (player-facing)
- **Terrain painting/terraforming:** convert tiles between at least 3 surface types (dirt / grass / water). Tile-based under the hood; brush-like feel in hand.
- **Planting loop:** seed → plant → grows over game-time → harvestable. 2–3 growth stages, visible. Watering only if it earns its keep — decide during the slice (research §3.3: VP's watering was chore-adjacent; a passion project can cut it).
- **Barren→lush readout:** tended tiles visibly warm/brighten (GDD §4.1). Even in greybox: saturation shift. This is the progress bar — prove it reads.
- **Land state model:** authoritative queryable state — % of each surface type in an area, plant counts by species/stage, placed objects. This model is what conditions interrogate.

### B. The Requirement Engine (the over-engineered one)
- **Condition assets (ScriptableObjects):** composable atoms — `SurfacePercent(area, type, ≥ x)`, `PlantCount(species, stage, ≥ n)`, `TimeOfDayIs(window)`, `ObjectPresent(id)`, `ResidentPresent(species, ≥ n)`, `SpiritStat(target, stat, ≥ x)` … designed so **new condition types are new assets/classes, never edits to existing logic**.
- **Requirement sets:** ordered gates per species — Appear / Visit / Resident / Fulfil — each a list of conditions (GDD §2.2 lifecycle). Difficulty = swappable rule-set per set (research §2.3 — TiP shipped three).
- **Evaluation service:** ticks cheaply, events on state change (`ConditionMet`, `GateOpened`), no per-frame polling per condition.
- **Designer test harness:** an editor window or debug overlay listing every gate and live condition truth-values. *You* are the designer it serves.

## Content

- 3 surface types, 3–4 plant species (data-defined), the Prairie field from 01 made malleable.
- 2–3 dummy requirement sets exercising every condition atom (no spirits yet — gates just light a debug lamp).

## Out of scope

Spirits/AI, economy (seeds are free from debug), weeds/hazards, weather.

## Depends on

01 (interact system, save, time).

## Definition of Done

- [ ] Reshape a patch of land and plant a small plot inside 2 minutes of unguided play, and it *feels* like gardening, not spreadsheet-editing
- [ ] Tended area visibly reads lusher than untouched land from normal camera distance
- [ ] A new species' full gate chain can be authored **entirely in the editor** (assets only) in under 10 minutes
- [ ] Gate states update live in the debug overlay as land changes; no measurable frame cost at field scale
- [ ] Save/resume preserves every tile, plant stage, and gate state

## Risks

- **This is the architecture keystone** (GDD §6.2). A shortcut here (hardcoded checks, string-matched conditions) taxes all ~50 species forever. Over-engineer deliberately, once.
- Tile terraforming UX in 3/4 view needs care — cursor-to-tile mapping must be obvious. Steal VP's footprint-highlight cursor idea (research §6.3).
