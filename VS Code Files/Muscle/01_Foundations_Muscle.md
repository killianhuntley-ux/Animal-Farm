# Muscle 01 — Foundations (The Shepherd's Body)

**Skeleton status:** complete (see `Slices/01_Foundations.md`). Walking, camera, interaction, time, save all work but are *inoffensive* — nothing fights you, nothing rewards you.
**Goal of this muscle pass:** make it pleasant to simply exist in the world. Every verdict below is an owner decision (2026-09-30, question-round format).

## Build status (2026-10-01, wave 2)

Compiled clean, awaiting owner playtest (QA checklist: F8 in-game tool).

**BUILT (by build-list item)**
1. ToolUseAction - committed ~0.5s swing, movement locked for the beat, held-button chain with a ~0.12s breath.
2. ShepherdXP + Level - XP per verb, level N needs 100*N more XP, level-up fanfare, saved.
3. ToolTier data - tier 2 = x0.65 action time, tier 3 = x0.5; Hoe and Pail tier 3 hit a 3-cell row, Shovel gets speed only.
4. Blacksmith - tier 2 = 40 obols + level 3, tier 3 = 100 obols + level 6.
5. VendorArrivals - Blacksmith moves in at 40 tiles tilled.
6. Four-direction facing - separate up / down / side placeholder sprites per skin (left = flipped side).
7. Sprint character - dust puffs, scamper bob, visual-only spirit startle.
8. CameraFollow clamp - owned-land union + soft margin, grows with parcels.
9. Sit & rest - Z / gamepad East: nearby happy spirits gather, mood ticks up, rest theme, free camera roam.
10. Walk juice - PARTIAL: per-surface footsteps (grass swish, dirt crunch, scrub rustle, sand hiss) and the grass leaf-puff trail.
11. Interaction unification - PARTIAL: facing-weighted focus.

**NOT BUILT**
- E opening the same context menu as the mouse, and the outline highlight (needs keyboard/gamepad menu nav; owner question 3).
- Shallow-water wading: the pond rim still blocks movement (owner question 5).
- Grass bend in your wake and ambient plant wind-sway (owner question 4).
- Flute. The lantern exists only as road gear (see Muscle 08); its night identity (spirits drift to the glow) is not built.

**ASSUMPTIONs made in build**
- Sit uses a dedicated Rest action (not hold-Interact): up to 6 happy spirits within 8 units, +0.15 Spirit/s each (about +9/min), capped at 90; camera pan radius 16; 24 s rest-theme loop; sitting refused mid-swing, in modals, during competitions and while riding.
- Interaction focus: range 1.4 units, distance squared scaled x1.8 (behind) to x0.5 (ahead), x0.8 stickiness for the current focus.
- Footstep stride 0.95 units, sprint ~25% louder; leaf puffs only on grass.
- Camera margin 4 units past owned land; Blacksmith threshold 40 tiles; tier prices/levels as above.
- Audio safety net added alongside (AudioGuard rate limiter, output limiter, diag log, Ctrl+M panic kill, Voice and Ambient volume sliders).

---

## Verdicts (locked)

### 1. Embodied, weighty tool use + upgrade path
Tool use stops being an instant click. Base tools are **weighty (~0.5s)**: wind-up, swing/pour arc, movement locked for the beat, particles + sound on contact. Held button = rhythmic re-apply for row work.

Classic farm-sim upgrade ladder on top:
- Tiers upgrade **speed and area of effect** (e.g. Hoe I tills 1 tile weighty → Hoe II faster → Hoe III tills a row).
- Purchased with a **farmer level + obols combo** from the **Blacksmith** (a forge-spirit vendor in town).
- The deliberate arc: base tools feel slow → "I wish this hoe was faster" → the Blacksmith moves in → the "ahh" moment.

### 2. Shepherd XP — "doing the work"
New system. Every verb drips XP: till, water, harvest, build, soothe, feed, herd, win trials. You level by playing; no action is wasted (consistent with the work-is-never-wasted law). Level gates tool tiers (and later, probably other vendor stock).

### 3. Staggered vendor move-ins — hidden play milestones
Vendors do NOT all exist from the start. Each moves into town when the game notices the matching behavior (Blacksmith after ~N tiles tilled, etc.). Town visibly grows as a response to how you play. Each goods category gets its own vendor over time (seeds vendor exists; blacksmith, and later others). Arrival = a small event (stall appears, guide-light or Ferryman remark).

### 4. Four-direction facing — now
Up/down/side placeholder poses (not just flipX). Proves the directional pose pipeline the artists will inherit; makes up/down tool swings read.

### 5. Characterful sprint
Sprint stays, with personality: scamper feel, dust puffs, and **spirits startle if you sprint right past them** — speed has a small social cost.

### 6. Camera: clamp with soft drag
Clamp to owned land + a dressed margin (scrubland/mist band so the edge reads "unclaimed wilds" not "end of data"). Keep the existing velocity-lead and smooth-damp drag *inside* the clamp — soft clamp, never a hard wall stop. Clamp grows when parcels are bought. (Ferryman's parcel overview still flies free — separate mode.)

### 7. Expressive verbs
- **Sit & rest** — free, taught in the tutorial. Nearby happy spirits gather and settle; mood slowly ticks up; **a musical theme kicks in**; **camera roams free while seated** until you stand up. The game's signature "moment."
- **Flute** — purchasable item later (ties into luring/herding: notes make spirits turn, melodies calm).
- **Lantern** — purchasable item later (night identity, spirits drift to the glow).

### 8. Walk juice — all four green-lit (procedural, no art)
- Footsteps per surface (grass swish / dirt crunch / water slosh; shallow water slows movement)
- Grass rustle + brief bend in your wake (walking leaves a living trail)
- Sprint dust puffs
- Ambient wind-sway on plants (world never looks frozen)

### 9. Unified interaction — E = context menu
Facing-weighted focus (not pure nearest-wins) with a clearer highlight (outline over scale-pop). E opens the SAME context menu the mouse click gets. One flow, full gamepad parity.

---

## Build list (ordered)

1. **ToolUseAction** — committed swing/pour state on ToolController: wind-up, lock movement, contact FX, held-repeat cadence. Weighty base timings.
2. **ShepherdXP + Level** (new system, ISaveable) — XP table per verb, level curve, level-up toast. HUD hook (small ring or number).
3. **ToolTier data** — per-tool tier defs (speed multiplier, AoE pattern), ToolController consumes them.
4. **Blacksmith vendor** — forge-spirit stall + upgrade shop UI (level + obol gating). Spawns via milestone system, not bootstrapped.
5. **VendorArrivalManager** — hidden milestone counters (tiles tilled, etc.) → move-in events with a visible beat.
6. **Four-direction facing** — SkinDefinition gains up/down/side sprites; ShepherdVisual picks pose off FacingDir dominant axis.
7. **Sprint character** — dust puffs, scamper bob tweak, spirit startle radius while sprinting.
8. **CameraFollow clamp** — bounds from ParcelManager (owned rect union + margin), SmoothDamp preserved; margin dressing (scrub/mist band) in bootstrapper.
9. **Sit & rest** — new input (hold interact on empty ground or dedicated key), seated state: spirits gather, mood tick, theme music layer, free camera roam until stand.
10. **Walk juice** — per-surface footstep synth, grass bend/rustle, wind-sway shader or transform wobble.
11. **Interaction unification** — facing-weighted focus scoring, outline highlight, E opens SelectionMenuUI for the focused target.

## Out of scope (noted for later)
- Flute + lantern items (purchasable; design when vendor roster grows)
- Real rigged animation (skin phase)
