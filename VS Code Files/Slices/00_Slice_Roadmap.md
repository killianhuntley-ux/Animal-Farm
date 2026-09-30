# 00 — Slice Roadmap

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
| 07 | [The Repo-man](07_Repo_Man.md) | Friction with a face; easy mode | 🔨 built this session — **M2** pending playtest. Design shift: repo targets runaways left unrecovered >2 game-hours (runaway = warning, repossession = escalation, first visit = lecture only). Fee: 2x favored food at the town Holding Office. `gentle on` = easy mode. |
| 08 | [The Frontier](08_Frontier.md) | Expansion, second region, absence management | 🔨 parcel machinery built (2 north parcels, produce-priced gates). **Tech debt:** parcels are traversal-only — TerrainGrid extension to parcels = slice 08b before biome regions. Absence/events model still pending. |
| 09 | [Economy & Vendors](09_Economy_Vendors.md) | Money in/out balances; NPC services | — |
| 10 | [Presentation & Onboarding](10_Presentation_Onboarding.md) | Real art/audio on the rigs; a stranger can learn it | **M3: Demo** ✂ |
| 11 | [Region Chunk Template](11_Region_Chunk_Template.md) | The repeatable EA content pattern (regions 3–7, villains 2–3) | **M4: EA launch** when 3–4 regions feel complete |

✂ = a build you could hand to another human.

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
