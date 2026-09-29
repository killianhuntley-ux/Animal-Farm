# 03 — First Spirits

**Question this slice answers:** *Does luring a creature by changing the land produce the "it chose ME" thrill?* (This is the owner's favourite VP system — the emotional core. GDD §2.2)
**GDD:** §1.3 (spirits), §2.2 (lifecycle front half), §2.3 (agent sim), §4.3 (silhouettes, journal)

---

## Builds (systems)

- **Spirit agent:** rigged 2D puppet, wander/idle/approach behaviours, needs model (hunger, comfort), and the **Spirit stat** (morale, 0–max) — the master resource (GDD §2.7). Locomotion as shared templates (ground first; fly/swim later — research §8.5).
- **Lifecycle state machine per individual:** `Distant → Silhouette → Visitor → Resident` driven by requirement gates from slice 02. Individuals are *named on residency* (player prompt) — naming is the investment hook (research §5.1).
- **Ghost silhouette rendering:** when Appear passes but Visit doesn't, the species haunts the border as a monochrome outline (GDD §4.3 — VP's best idea). Must be legible at normal zoom and slightly eerie-cute.
- **Care verbs on the interact system:** feed by hand (from harvested plants), soothe/pet. Each nudges needs/Spirit. Flute is a stretch goal — one lure-note ability if time allows (GDD §2.6).
- **Journal v1:** species pages with the condition chain, progressively revealed (`???` until discovered); resident roster with name, Spirit level, needs. No weave content yet.
- **Runaway (loss model floor):** sustained low Spirit → the spirit flees to the border and waits; walk out and re-lure/soothe it back (full herding minigame comes with 05's minigame tech; here it's approach-and-soothe). GDD §1.7: losses cost effort, never erase investment.

## Content

- **4 base spirit species** (placeholder skins on shared rig), data-authored via slice-02 assets:
  - two trivially lurable (grass %, a plant type) — the unblockable on-ramp (GDD §3.2)
  - one requiring another species resident (first dependency edge)
  - one night-gated (proves time-of-day conditions, GDD §2.5)
- Per-species: needs profile, favoured foods, activity window, 2–3 idle behaviours. Human-gibberish voice = your own mic, placeholder (GDD §4.2).

## Out of scope

Fulfilment/Ascension (04), competitions, weaving, villains, economy (food from your plot).

## Depends on

01 (interact/save), 02 (land state + requirement engine — spirits are its first real consumer).

## Definition of Done

- [ ] From an empty field, an unguided player can lure and house-train (make resident) two species within ~15 minutes using only the journal
- [ ] The silhouette moment lands: testers ask "what is THAT and what does it want?"
- [ ] The dependency-edge species creates a visible "aha" (journal reveals it needs a resident of X)
- [ ] Naming a new resident feels like a small ceremony, not a text box
- [ ] Neglect → runaway → recovery works end-to-end and feels fair, not punishing
- [ ] All 4 species authored as data; adding a 5th takes <30 min including placeholder skin
- [ ] Save/resume preserves every individual (name, stats, lifecycle state, position)

## Risks

- **Agent behaviour is charm.** Spirits that stand still are furniture; budget real time for idle personality (VP spent per-species animation lavishly — research §8.5 — you have shared rigs, so invest in *behaviour* variety instead).
- Needs-tuning rabbit hole: keep the needs model tiny (2 needs + Spirit). Depth comes from conditions, not sims.
