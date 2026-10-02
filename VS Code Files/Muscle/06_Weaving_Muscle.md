# Muscle 06 — Weaving

**Skeleton status:** complete (Loom buildable, weave recipes, essence toll, WeaveUI with toll display, cryptid output).
**Problem:** weaving is a menu transaction — the game's strangest idea deserves its strangest scene.
All verdicts are owner decisions, 2026-09-30 (question-round format).

## Build status (2026-10-01, wave 2)

Compiled clean, awaiting owner playtest.

**BUILT (by build-list item)**
1. Weave rite sequence - night loom rite (~16 s): parents walk to the Loom, overlay dims everything but parents/shepherd/Loom, both unravel into species-coloured threads, the Loom weaves a cloth, the cryptid steps out, the cloth folds into a banner, naming opens with a blended name suggestion. Esc skips to the committed end state.
2. Tapestry banners - one per weave in the pouch (`tapestry_N`), hang anywhere on usable land with nothing built (one per cell), Read / Move / Take down, saved.
3. Inheritance - stat bias from both parents' band positions, one trait from each parent (filtered by the cryptid species and conflicts), name-blend suggester.
4. Rumor hints - vendor and guide-light lines for undiscovered recipes whose parents are both resident.
5. Journal weave pages - "???" silhouette pages with a rumor slot, "Woven from ..." lines, "Woven away" section in the Legacy tab.

**NOT BUILT**
- Cross-base weaving as its own expedition feature (an escorted swamp spirit can reach the single home Loom by road, but nothing else is special-cased).
- More than the two recipes (Wailpertinger, Mothmaus) - content, not systems.

**ASSUMPTIONs made in build**
- The rite is allowed at any hour (it always plays in a night look; no time gate).
- Quitting or saving before the cloth completes restores "rite never started" (both parents full-spirit residents by the Loom); the weave commits in one block.
- Name blend: head of one parent + tail of the other, 3-10 letters, ASCII; never just one parent's name.
- Rumors: at most one per game-day (70% chance, 40% of them told by the guide-light), only while both parent species are resident, vendors speak within 3.5 units of the stall, up to 3 rumors kept per recipe; Charon is never a rumor source (owner rule).
- Toll stays 6 essence per weave.

---

## Verdicts (locked)

### 1. The weave: night loom rite
Both spirits step to the Loom, the world dims (kinship with the Styx ceremony), their forms **unravel into two colored threads** that the Loom weaves in the air — the new cryptid **steps out of the finished cloth**. Eerie-beautiful, mirrors the Charon scene. (Instant — no cocoon gestation.)

### 2. Recipe discovery: experiment + rumors
Journal shows "???" silhouette pages for unknown cryptids; vendors and the guide-light occasionally drop cryptic hints ("moth and lantern-folk share a thread..."). Experimenting always works when the pair is valid.

### 3. Inheritance: all three channels
- **Stat bias**: the cryptid's roll inside its own species band is biased by the parents' rolls — strong parents, strong thread.
- **Traits**: one trait from each parent carries through (filtered by what the cryptid species allows).
- **Name echo**: the naming ceremony pre-fills a suggested blend of the parents' names, fully editable.

### 4. Remembering the woven: journal + tapestry banner (owner-specified)
Woven-away parents are recorded in the **journal**, and each weave produces a **small tapestry banner the player can hang anywhere as decor** — a placeable memorial item woven from the two parents' thread colors. (Garden stones stay exclusive to Styx-crossed spirits: garden for the crossed, banners for the woven.)

---

## Build list (ordered)

1. **Weave rite sequence** — world-dim vignette, walk-to-loom behavior for both parents, unravel-to-threads VFX (two tint colors from species palettes), air-weave animation, cryptid step-out + name echo into the naming ceremony (Muscle 03's light-descends variant).
2. **Tapestry banners** — generated banner item per weave (parent colors), inventory item, place/hang anywhere ownable as decor, ISaveable.
3. **Inheritance** — stat-bias math on spawn roll, trait pass-through with species filter, name-blend suggester.
4. **Rumor hints** — hint-line pools on vendors + guide-light, triggered occasionally when an undiscovered valid recipe's ingredient species are both resident.
5. **Journal weave pages** — "???" silhouettes, parent record lines on cryptid pages, hint text slot.

## Carried forward
- Cross-base weaving (swamp spirit escorted to the hell base's loom) → slice 08 muscle: roads + danger make rare weaves an expedition.
