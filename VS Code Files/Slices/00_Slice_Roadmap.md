# 00 — Slice Roadmap

> **SKELETON COMPLETE (2026-10-01).** Every core system exists and compiles: lure→tend→compete(x2)→weave→defend→expand→ascend, economy, onboarding, friction, placeholder audio. Owner's phase plan: skeleton → **muscle** (evaluate each aspect, flesh out depth/feel — STARTS NOW) → skin (art for locked-in systems). Herding chase shipped; The Crossing shipped; weeds/watchlight shipped. Remaining content-shaped work (region chunks, species roster growth) belongs to the muscle/chunk cadence, not the skeleton.

**Source of truth:** [Game Master File.md](../Game%20Master%20File.md) (GDD) · [Initial_Research.md](../Initial_Research.md)
**Model:** GDD §5.4 — core loop first, then coherent chunks. Nothing beyond the current slice is promised, including to yourself.

---

## The sequence

| # | Slice | Proves | GDD Milestone |
|---|---|---|---|
| 01 | [Foundations](01_Foundations.md) | Controls feel good; the shepherd is fun to *be* | ✅ built, playtested |
| 02 | [The Living Land](02_Living_Land.md) | Terraform/plant loop + the data-driven requirement engine | ✅ built, playtested |
| 03 | [First Spirits](03_First_Spirits.md) | Attraction works: conditions → silhouette → visitor → resident | ✅ built, playtested |
| 04 | [The Full Lifecycle](04_Full_Lifecycle.md) | Fulfilment → Ascension; goodbye feels *good* | ✅ built — **M1** (emotional DoD pending owner verdict) |
| 05 | [The Boulder Trial](05_Boulder_Trial.md) | Competitions (the hook) are actually fun | ✅ built (fun verdict pending) — venue moved to TOWN |
| 05b | [Spirit Identity](05b_Spirit_Identity.md) | Per-individual stats + inspection make duplicates desirable | ✅ built |
| 06 | [Weaving](06_Weaving.md) | The second door; cryptid creation lands | ✅ built (Wailpertinger, Mothmaus; the Loom, west field) |
| 07 | [The Repo-man](07_Repo_Man.md) | Friction with a face; easy mode | ✅ built — **M2**. Runaway = warning, repossession = escalation, first visit = lecture. Holding Office fees. Gentle Passage now in the pause menu. |
| 08 | [The Frontier](08_Frontier.md) | Expansion, second region, absence management | ✅ built incl. **08b**: parcels are REAL tillable terrain (usable-cell mask; spirit borders grow with the land). Absence pressure = weeds (mood-souring, telegraphed, never erase work) + purchasable Watchlight wards. |
| 09 | [Economy, Vendors & The Town](09_Economy_Vendors.md) | Money flows; walking to town is a pleasure | ✅ core built: **obols** (coin, themed), essence (sell 8 obols OR weave-toll 6), Vendor (sell produce/essence, buy treats + watchlights), **Ferryman land office** (parcel overview, distance-priced deeds), competition fees/prizes, follow treats. Balance = muscle phase. |
| 10 | [Presentation & Onboarding](10_Presentation_Onboarding.md) | Real art/audio on the rigs; a stranger can learn it | 🟡 skeleton: Guide light (7 pointed steps, skippable, old-save-aware) + procedural placeholder audio (synth blips, no assets). Real art/audio + stranger test = skin phase. |
| 11 | [Region Chunk Template](11_Region_Chunk_Template.md) | The repeatable EA content pattern (regions 3–7, villains 2–3) | **M4: EA launch** when 3–4 regions feel complete |

✂ = a build you could hand to another human.

## MUSCLE PHASE (2026-09-30 — verdicts locked via owner question rounds)

All muscle design lives in [../Muscle/](../Muscle/) — one doc per area, every verdict owner-decided. Cadence: **ask/answer (done) → build all → playtest via the in-game feedback tool → re-examine → repeat.**

| Doc | Covers | Headlines |
|---|---|---|
| [01 Foundations](../Muscle/01_Foundations_Muscle.md) | Shepherd body | Weighty tool actions + Blacksmith tier upgrades, shepherd XP ("doing the work"), staggered vendor move-ins, 4-dir facing, sit & rest, camera clamp, walk juice |
| [02 Living Land](../Muscle/02_Living_Land_Muscle.md) | Land + biomes | **Player-sculpted biomes** (swamp/desert/grass, 5-step spirit affinity), **satellite-base world** (3x3→4x4 + road rights), rain, water life, crop quality/regrow/wilt, compost |
| [03 First Spirits](../Muscle/03_First_Spirits_Muscle.md) | Spirit charm | Want bubbles, presence reactions, idle quirk library, pair interactions, night shift, synth voices, light-descends naming, shy silhouettes, per-species mood animations |
| [04 Full Lifecycle](../Muscle/04_Full_Lifecycle_Muscle.md) | The goodbye | **The Styx crossing replaces ascension** (Charon, spirit-orb payment, headstone keepsake), buildable ascension pad, memorial garden (no buffs), no anti-hoarding pressure |
| [05+05b Competitions/Identity](../Muscle/05_Competitions_Identity_Muscle.md) | Stats + calendar | **Competitions HELD**, species stat bands, passive training buildings, individual traits, **15-day underworld seasons + calendar** |
| [06 Weaving](../Muscle/06_Weaving_Muscle.md) | The second door | Night loom rite, experiment+rumor recipes, full inheritance (stats/traits/name echo), tapestry banner keepsakes |
| [07 Friction](../Muscle/07_Friction_Muscle.md) | Adversity | Repo-man walks in + bribeable, useful weeds, **untamable villain spirits** (Digger/Devourer/Scarer — small recoverable losses) |
| [08+09 Frontier/Economy](../Muscle/08_Frontier_Economy_Muscle.md) | World + money | Walk the roads (all 4 dangers + defense items), **swamp satellite first**, vendors-only selling, traveling merchants, biome-exclusive vendors, the **Pouty Mount** |
| [10 Presentation](../Muscle/10_Presentation_Muscle.md) | Polish + loop | Guide-light personality (tutorial-scoped, "Navi but less intrusive"), procedural ambient music, options+rebinding, **in-game playtest feedback tool** |

**Status (2026-10-01): wave 2 built, compiled clean, awaiting owner playtest.** Muscle docs 01-08 each carry a "Build status (2026-10-01, wave 2)" section (built / not built / assumptions); the in-game QA checklist (F8) has concrete checks for every new system and keeps only the genuinely unbuilt items as "Not built yet" rows.

**Owner answers (2026-10-01) to the wave-2 blockers:**

1. Compost & fiber: KEEP -- 3 fiber = 1 compost substitute, fiber sells 1 obol.
2. Villain seasons: YES -- overall rate flat, each underworld season favors one archetype.
3. E key: E ALWAYS opens the context menu on selectables (keyboard/gamepad menu nav + outline highlight).
4. Plant bend/sway: transform wobble in Plant.cs now.
5. Shallow water: rim becomes walkable and slows the shepherd; deep water stays solid.
6. Competition board: read-only schedule (upcoming competitions + who is competing) when no competition is taking place; on a scheduled event day it opens for entries -- bring a creature along (following you) and interact with the board to enter it.
7. Land office: keep The Registrar.
8. Rich mud: plantable swamp soil (always watered, counts toward Swamp), sold by the load at the Mire Peddler.
9. Desert species: author one desert-native species.
10. CORE LOOP VERDICT (overrides feed-to-convert): spirits come to visit because they got interested in what you are doing (e.g. a plant they like); when full criteria are met they visit in full body and DECIDE to stay on their own (chance roll). No feeding-to-join. Hard-No species never visit that ground.
11. Home placement on Hard-No ground: allow silently.
12. Pouty Mount: joins some time after you buy rights to a second area and have been crossing the roads to and fro (not a field count). Name "Grudge" kept as placeholder.

Status: wave 2b BUILT 2026-10-01 (all answers implemented; reviewed twice; compiled clean; scene regenerated). Awaiting owner playtest.

**Open (minor):** (a) should a deciding visitor wait for the shepherd within 15 units before its naming ceremony (current), or name wherever? (b) add met/unmet ticks to journal requirement lists?

## Playtest log (owner sessions)

- **2026-09-29 (slice 01):** all works; movement fine; walk-bob too fast (fixed: freq 2.2→0.55). Waystone flavor was log-only (fixed: floating world text).
- **2026-09-29 (slices 02-05):** map far too big → 40×26 fenced farm; toolbelt (T), seed-picker-on-dirt, inventory HUD, world labels added. Typing-in-console leaked into gameplay → `UIInputLock` (standing rule: every direct device read checks it). Spirits too small → 1.6×. Night unreadably dark → light floor raised; competitions force daylight.
- **2026-09-30 (big wave):** panels flashed (fixed: build-once + value refresh); button sprite artifacts (fixed); toolbelt overflow → 4-tool metaphor (Hands/Shovel/Pail/Hammer + B build menu); journal residents → in-journal detail pane; inspector close pinned top-right; homes 2.2×; label proximity-fade; silhouettes unrenamable + lightened. **Confirmed working in play:** wheat-priced parcel purchase opened the map; Repo-man repossessed a neglected spirit. Boulder/ascension fun verdicts still pending.

## Rules of engagement

1. **Order is dependency order.** Each slice assumes everything before it works. Skipping ahead creates the VP trap: "nothing at the top validates until everything under it works" (research §2.2).
2. **Greybox first, always.** No slice waits on final art. Skeletal rigs + placeholder skins from day one (GDD §6.2) — friend-artists swap skins later without rework.
3. **Every slice ends with a playable answer to one question.** The question is at the top of each doc. If the answer is "no," fix or redesign *before* the next slice — this is the cheap moment.
4. **Kill criteria are honest.** Some docs name what to cut/replace if the fun isn't there (esp. 05 — competitions are the pitch's headline and minigames are hard, GDD §6.1).
5. **The requirement engine (02) is the only thing worth over-engineering** (GDD §6.2). Everything else: simplest thing that works.

## Definition-of-Done conventions

Every slice DoD includes, implicitly:
- Runs in a fresh clone on the dev machine; no console errors in a normal session
- Save/quit/resume mid-activity loses nothing (GDD §2.5 — quit-anytime is sacred)
- New systems configured as **data** (ScriptableObjects), not hardcoded, where the GDD says so
- A 5-minute self-playtest note appended to the slice doc: what felt good, what didn't
