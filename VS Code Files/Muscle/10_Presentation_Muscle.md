# Muscle 10 — Presentation, Onboarding, and the Feedback Loop

**Skeleton status:** complete (9-step guide-light tutorial, synth bleeps, UIStyle pass, pause/settings basics).
All verdicts are owner decisions, 2026-09-30 (question-round format).

---

## Verdicts (locked)

### 1. Guide-light: full personality, strictly scoped
Rewrite every tutorial line in the dark-funny-kind register (a minor god doing community service, mildly embarrassed about it). BUT it only appears for the **tutorial and tutorial-type events** (introducing a new vendor move-in, a new system unlock). "**Navi but less intrusive**" (Ocarina of Time) — no idle nagging, no ambient quips outside its job.

### 2. Music: procedural ambient architecture
Generative ambient bed (synth pads/plucks keyed to time of day and season) + the sit-down theme (Muscle 01) + ceremony stings (Styx crossing, weave rite, naming). This sets the audio architecture the musician friend later replaces with real stems.

### 3. QoL: options + remapping
- **Options menu**: music/SFX volume sliders, screen shake toggle, text speed.
- **Controls remapping**: rebindable keys/buttons via the Input System (gamepad-heavy genre, do it early).
- (No photo mode, no multi-slot saves this pass.)

### 4. THE PLAYTEST FEEDBACK TOOL (owner-specified, build this)
An in-game **debug feedback checklist**: a menu listing every muscle aspect with a prompt and a text box —
> 1. Character movement — does it feel good? [text box]
> 2. Tool weight/rhythm — ...
Responses **save to a file in the game's data folder** that Claude can read afterwards. Loop: playtest → read responses → update all muscle markdowns → full fix pass → whittle. This tool IS the re-examination mechanism for the ask/build/test cycle.

---

## Build list (ordered)

1. **Feedback tool** — F-key debug menu: aspect list (seeded from every muscle doc's verdicts), per-aspect prompt + multiline text box (UIInputLock-respecting), JSON/markdown dump to a `playtest_feedback` file with date + save context.
2. **Guide rewrite** — tutorial line pass in register; vendor-move-in introduction beats; scoping audit (no appearances outside tutorial events).
3. **Ambient music system** — generative pad/pluck bed, time-of-day + season keying, ceremony sting hooks, sit-down theme integration, mixer with volume buses.
4. **Options menu** — volume sliders (music/SFX buses), screen shake toggle, text speed.
5. **Rebinding** — Input System rebind UI for keyboard + gamepad, persisted.
