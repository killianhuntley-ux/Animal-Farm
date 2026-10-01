# Muscle 05 + 05b — Competitions on Hold, Identity Deepened

**Skeleton status:** Boulder Trial + The Crossing race built and piloted; spirit identity (naming, journal, Nature stats 2-9, inspect UI) built.
All verdicts are owner decisions, 2026-09-30 (question-round format).

---

## Verdicts (locked)

### 1. COMPETITIONS: HELD ENTIRELY for the muscle phase
Boulder Trial: cut to zero active development. The race: **held too** — the lifecycle/Styx rework (see Muscle 04) claims "crossing" as a concept and the competition layer sits out this phase. The board/town stalls stay in the build but receive no muscle. Revisit when the calendar's scripted events need anchor content.
- Naming note: "The Crossing" as a race name is retired — the Styx crossing owns that word now.

### 2. Nature stats: species band + individual roll
Each species defines a **base (min) and max** per stat; an individual spawns with a random roll inside that band. A big boar has a larger max Vigor than a mouse. Species identity lives in the bands; individual luck lives in the roll.

### 3. Stat training: passive-use buildings
Stats grow through **buildings spirits use on their own** (training equipment they wander to). Placing **certain foods attracts certain spirits to train**. No player-driven drill minigame — the farm trains its residents while you garden. Capped by the species max.

### 4. Gleam stays passive
No pageant. Gleam drives essence shed rate and sell/preference values only.

### 5. Individual traits: yes, light
Small trait pool (Brave, Lazy, Greedy, Skittish, Show-off, ...), 1-2 rolled at spawn. Traits weight idle-quirk frequencies (Muscle 03 library), training-building appetite, and journal flavor text. Individuals stop being interchangeable.

### 6. CALENDAR + UNDERWORLD SEASONS (new system)
- **15-day seasons** — time works differently in the underworld.
- Seasons are NOT summer/winter/spring/autumn but **underworld analogs**: similar in function, each bringing something unique (names/moods to be authored — e.g. the quiet season, the drifting season, the hungry season...).
- **Light gameplay touch**: palette/light/ambience shifts, festivals and calendar beats live here, SOME silhouettes are season-gated, rain frequency varies by season. **Crops never die; biomes untouched** (work-never-wasted).
- Browsable month-grid calendar (board in town + menu), HUD date. Scripted Stardew-style events slot in later; scheduled competitions join when competitions return.

---

## Build list (ordered)

1. **Calendar core** — 15-day season cycle, season defs (name, palette tint, ambience set, rain weight), HUD date, calendar board UI + menu page.
2. **Season hooks** — requirement-engine atom (`SeasonIs`), season-gated silhouette support, rain-frequency wiring into the Muscle 02 weather scheduler.
3. **Species stat bands** — min/max per stat on SpiritSpeciesDefinition; spawn roll inside band; journal shows the band ("Vigor 4 of 3-7") so rolls are legible.
4. **Traits** — trait pool asset, 1-2 per spawn, idle-weight + flavor-text wiring, journal display.
5. **Training buildings** — 2-3 passive-use structures (build menu, obol-priced); spirits path to them by trait/species appetite; food-bait slot attracts specific species; slow stat gain to species cap.
6. **Competition mothball** — board text updates ("races return with the festival season..."), no new work.

## Carried forward
- Race redesign + rename, rivals, festival editions → post-muscle, hung off the calendar.
- Trait inheritance → weaving muscle (06).
