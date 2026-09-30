# 09 — Economy, Vendors & The Town

**Question this slice answers:** *Does money flow at a rate that makes expansion feel earned — and is walking to town (spirits in tow) a pleasure rather than a chore?*
**GDD:** §3.5 (economy), §3.6 (service NPCs + The Town), §3.4 (economic gating IS progression)

Until now everything was debug-free. This slice turns the taps on and prices the world.

**OWNER DECISION (2026-09-29): shops are a physical TOWN, not a menu.** A settlement a short walk from the starting field: vendor stalls, the Ferryman's landing, later the competition board. Spirits can accompany you via a **"follow me" treat** — a purchasable item that triggers the follow behaviour (built in slice 04 for altar-guiding) for a duration. Scope adds: town zone layout (greybox), walk-distance tuning, treat item + duration, and NPC stall interactables replacing abstract shop UI-only access.

---

## Builds (systems)

- **Currency & wallet**, transaction log (tuning depends on seeing flows).
- **Income (GDD §3.5):**
  - **sell plants/produce** at the vendor
  - **essence** (v0 from slice 04) becomes sellable — happy spirits literally pay the bills; rate scales with Spirit
  - **competition prizes** (05's placeholders become real)
  - *spirits are never sold* — enforced, fictional law (GDD §3.5)
- **Sinks:** land parcels (primary, 08) · entry fees · helpers/equipment · seeds/materials · Repo-man fines · building placements.
- **Vendor NPCs (GDD §3.6 proposed roster, now real):**
  - **General vendor** — seeds, materials, food (economy floor)
  - **The Ferryman** — land deeds (expansion is buying passage) + competition master of ceremonies; one character, two hats, maximum reuse
  - **Builder** — structures, supervisor equipment, helper contracts
  - Healer/medium deferred until a system needs it (soothing may suffice — don't build an NPC without a job)
- **Shop UI** (one pattern reused for all vendors) + first-pass price table as a single tuning asset.
- **Decision point (deferred → due now, GDD §6.3):** *is essence also the Weave ritual cost?* Recommendation: **yes, small cost** — unifies "keep everyone happy" with "make cryptids" and gives essence a non-money identity. Decide during this slice with live numbers.

## Content

- 3 vendor NPC rigs/skins + barks (tone register: the Ferryman is dry; the vendor is cheerfully morbid), shop stock tables, price pass #1 across every existing sink/source.

## Out of scope

Full balance (EA's job), fame-gated prestige stock, Switch-era currency polish, spectator income (deferred idea — GDD Q18 candidates not chosen).

## Depends on

08 (parcels to price), 05 (prizes), 04 (essence), 07 (fines).

## Definition of Done

- [ ] A fresh playthrough reaches its **first Forest parcel in 2–4 hours** of intent-driven play (first structural pacing target — adjust to taste, but *measure* it)
- [ ] Early game feels tight (real choices between seed/fee/parcel), without ever hard-blocking the core loop (tending is always free)
- [ ] Every income stream is used by testers; if one dominates >70% of earnings, flag for rebalance
- [ ] Each NPC's job is nameable by a tester after one interaction (research §4.4 standard)
- [ ] Essence-and-weaving decision made and recorded in the GDD (§3.5/§6.3 updated)
- [ ] All prices live in one tuning asset; a full economy pass = one file edit

## Risks

- **Economy tuning is endless by nature** — timebox to "feels roughly right," then let alpha/EA telemetry (even just friend feedback) drive iteration. Structure now, balance forever.
- Essence overpaying turns the game idle; underpaying makes happiness cosmetic. It's the identity income — tune it to *meaningful but never sufficient alone*.
