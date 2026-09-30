# 05b — Spirit Identity (stats & inspection)

**Question this slice answers:** *Does knowing that THIS mouse is different from the next mouse make the player want both?*
**Origin:** owner decision (2026-09-29), added mid-build after slice 05 playtest: "like Pokemon, not all will be equal... incentivises getting more of one type to do better in competitions." Reference: VP's per-piñata journal pages (name, value, happiness — research §4.6).

## Built (same-day as slice 05)

### Individual stats — "Nature"
- Every spirit individual rolls three stats at spawn, **2–9 each**:
  - **Vigor** — strength/stamina. Drives the Boulder Trial (stamina pool = 40 + Spirit×0.3 + Vigor×4).
  - **Grace** — speed/agility. Small boulder push-speed factor now; the future race event's primary.
  - **Gleam** — charm/shine. Reserved: future show events + essence yield (economy slice).
- Persisted per individual; old saves reroll (vigor==0 sentinel).
- Design intent: **stats create the collection incentive within a species** — a mediocre Vigor Mausoleum still ascends beautifully, but the competition player hunts a 9.
- Species-level stat *biases* (e.g. Bansheep skew Vigor) deferred — add when the roster grows (data field on species def).

### Inspection panel
- **R / right-stick-press** while a spirit is focused → right-side info panel: name (+ **Rename** inline), species + state, flavor, Spirit bar, hunger word, Nature block (three stat bars), fame (wins/entries), fulfilment checklist ([x] Home / [x] Full spirit / [x] Final wish + the wish text).
- Uses the modal input-lock (below); refreshes twice a second while open.

### The input-lock lesson (bug class, fixed for good)
Playtest found typing "compete" in the console **pushed the boulder with every space** — every direct `Keyboard.current` read bypassed `SetGameplayBlocked`. Fix: `UIInputLock` (static): `TextInputActive` (console, name prompt, rename) + `ModalOpen` (toolbelt, pickers, journal). **Rule going forward: any direct device read MUST check `UIInputLock.BlockDirectKeys`.** This is now a review-checklist item for every future minigame.

## Deferred within this slice
- Species stat biases (above)
- Gleam's actual mechanics (essence yield → slice 09; show event → region chunks)
- Stat display in the naming moment ("a sturdy one!" flavour on arrival — nice hook, later)

## Definition of Done
- [ ] Two residents of the same species show different Nature bars, and a tester articulates a preference
- [ ] High-Vigor beats low-Vigor in the boulder with equal hands and equal Spirit
- [ ] Rename works from the panel; no keystroke leaks into gameplay while typing
