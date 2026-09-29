# 00 — Slice Roadmap

**Source of truth:** [Game Master File.md](../Game%20Master%20File.md) (GDD) · [Initial_Research.md](../Initial_Research.md)
**Model:** GDD §5.4 — core loop first, then coherent chunks. Nothing beyond the current slice is promised, including to yourself.

---

## The sequence

| # | Slice | Proves | GDD Milestone |
|---|---|---|---|
| 01 | [Foundations](01_Foundations.md) | Controls feel good; the shepherd is fun to *be* | — |
| 02 | [The Living Land](02_Living_Land.md) | Terraform/plant loop + the data-driven requirement engine | — |
| 03 | [First Spirits](03_First_Spirits.md) | Attraction works: conditions → silhouette → visitor → resident | — |
| 04 | [The Full Lifecycle](04_Full_Lifecycle.md) | Fulfilment → Ascension; goodbye feels *good* | **M1: Vertical Slice** ✂ |
| 05 | [The Boulder Trial](05_Boulder_Trial.md) | Competitions (the hook) are actually fun | — |
| 06 | [Weaving](06_Weaving.md) | The second door; cryptid creation lands | — |
| 07 | [The Repo-man](07_Repo_Man.md) | Friction with a face; easy mode | **M2: Core-Loop Alpha** ✂ |
| 08 | [The Frontier](08_Frontier.md) | Expansion, second region, absence management | — |
| 09 | [Economy & Vendors](09_Economy_Vendors.md) | Money in/out balances; NPC services | — |
| 10 | [Presentation & Onboarding](10_Presentation_Onboarding.md) | Real art/audio on the rigs; a stranger can learn it | **M3: Demo** ✂ |
| 11 | [Region Chunk Template](11_Region_Chunk_Template.md) | The repeatable EA content pattern (regions 3–7, villains 2–3) | **M4: EA launch** when 3–4 regions feel complete |

✂ = a build you could hand to another human.

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
