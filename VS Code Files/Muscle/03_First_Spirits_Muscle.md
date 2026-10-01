# Muscle 03 — First Spirits

**Skeleton status:** complete (full lure lifecycle, silhouettes, naming, journal, feed/soothe/follow, runaway + herding, Nature stats, treats).
**Problem:** spirits are wandering stat-bags — no personality in motion, wants legible only in the journal, no reaction to the shepherd.
All verdicts are owner decisions, 2026-09-30 (question-round format).

---

## Verdicts (locked)

### 1. Want bubbles — event-driven
A small thought bubble appears **when the want first occurs** (brief, then fades), and again **when you walk by / interact**. No always-on icon farm; no journal-tab-flipping to learn basics. Journal stays the deep record.

### 2. The shepherd matters
- **Mood-based drift**: happy spirits drift toward you and tag along a few steps; low-mood spirits edge away. Mood becomes body language.
- **Greeting on first meet of the day**: little hop + chirp the first time each resident sees you. Daily-rounds ritual.
- **Sprint startle (visual only)**: spirits flinch if you sprint past, but it NEVER lowers mood — players must not be discouraged from sprinting.

### 3. Idle personality library — all four
- **Habitat habits**: species-specific hangouts (rocks, lights at night, pond edges — ties to Muscle 02 water-idle).
- **Micro-moments**: naps, stretches, hops, leaf-chasing — shared micro-animations with per-species frequency weights.
- **Pair interactions**: two spirits meet → play or squabble by species compatibility (seeds the social layer weaving can later read).
- **Night shift**: some species visibly sleep curled up; others wake and get active at night.

### 4. Voices: synth chirps now, mic gibberish at skin phase
Extend the Bleeps synthesizer with per-species pitch/contour profiles — each species gets a recognizable voice. Owner mic recordings replace them later.

### 5. Naming ceremony: "Light descends"
Time softens, the guide-light drifts down over the spirit, gentle chord, the spirit bows as the name field appears; the name echoes as floating text and the journal page flips open. ~6 seconds, skippable.

### 6. Silhouettes: shy fade-back
Approach one and it drifts back / fades, reappearing elsewhere on the border later. You cannot touch it — you can only make the land it wants. Mystery preserved; core loop taught.

### 7. Visitor → Resident: conditions only
If the land is right, it moves in. VP purity — care verbs matter after residency, not as a courtship gate. (No trust meter.)

### 8. Mood at a glance: posture + pace, per-species animation sets
Happy / neutral / sad-sick read through **per-species** happy, neutral, and sad/sick animations (upright+bouncy vs drooped+dragging as the general grammar, but species-dependent expression). No aura rings, no UI.

---

## Build list (ordered)

1. **SpiritAnimState** — per-species 3-state animation profile (happy/neutral/sad-sick) driving posture, bob style, and move speed; plugs into the existing procedural animation.
2. **WantBubble** — world-space bubble widget; fires on want-onset (brief) and on proximity/interact; icon set for food/water/lonely/home needs.
3. **Presence reactions** — mood-drift steering layer, first-meet-of-day greeting (per-resident daily flag), sprint flinch (no mood change).
4. **IdleBehaviorLibrary** — shared micro-moment actions + per-species weights; habitat-habit targets (rock/light/pond anchors); night-shift schedule per species.
5. **PairInteraction** — proximity pairing check, compatibility table on species defs, play/squabble behaviors (no mechanical effect yet beyond small mood nudges).
6. **SpiritVoice** — Bleeps extension: per-species voice profile (base pitch, contour, timbre); hooks: greeting, feed, soothe, startle, night settle.
7. **Naming ceremony** — light-descends sequence (time-soften, guide-light move, chord, bow, name field, echo text, journal flip). Skippable.
8. **Silhouette shyness** — approach detection → drift-back/fade + border respawn elsewhere.

## Carried forward
- Pair compatibility data → weaving affinity (slice 06 muscle).
- Mic gibberish recording pipeline → skin phase.
- Escort-spirits-between-bases behavior → slice 08 muscle (world restructure).
