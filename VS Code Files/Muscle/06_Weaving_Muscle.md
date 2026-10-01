# Muscle 06 — Weaving

**Skeleton status:** complete (Loom buildable, weave recipes, essence toll, WeaveUI with toll display, cryptid output).
**Problem:** weaving is a menu transaction — the game's strangest idea deserves its strangest scene.
All verdicts are owner decisions, 2026-09-30 (question-round format).

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
