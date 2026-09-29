# 08 — The Frontier

**Question this slice answers:** *Does buying land in a chosen direction feel like ambition — and does being far from home create tension instead of tedium?*
**GDD:** §1.4 pillar 2 (frontier), §2.3 (absence model — locked "C with a dash of A"), §3.3 (regions), §6.1 (embodiment tax)

---

## Builds (systems)

- **Land parcel system:** the map beyond your fence is visible, parcelled, and priced. Buy a parcel in **any direction** → fence moves, terrain becomes tendable. Price scales with distance-from-home (GDD §3.3: distance = difficulty = cost).
- **Region framework:** parcels belong to regions; a region defines terrain palette, native species pool, hazard profile, ambience. Built as **data** — this framework is what slice 11's chunk template stamps.
- **Second region: Forest** (first non-starter biome, GDD §3.3 order) — enough parcels to make travel distance real.
- **Absence model implementation (GDD §2.3, locked):**
  - deterministic background sim everywhere: plant growth, food-stock depletion
  - **no** full agent sim in unattended regions (performance + sanity)
  - bad events roll on a randomized timer *per unattended region*, gated by region hazard profile; each fires the alarm contract (07's tech, reused)
- **Helpers / supervisor equipment (GDD §2.3):** first two purchasables — e.g. a **Watchlight** (equipment: extends alarm lead time in its region) and a **Helper spirit** (assignable: auto-soothes one troubled spirit per day). Automation as a purchase (research §4.4).
- **Travel support:** map screen with alerts pinned; evaluate walk times honestly — if the Forest round-trip annoys *you*, log it against the deferred movement-aids decision (GDD §6.1/§6.3) now, with numbers.

## Content

- Forest region: terrain set, ambience, **3 new species** (one needing a *Prairie* resident — first cross-region chain, GDD §3.2 "handful, deliberate"), 1 biome event type (e.g. tangling brambles that trap a wanderer until freed).
- 2 helper/equipment definitions, parcel pricing first pass, region border art (greybox).

## Out of scope

Regions 3–7, Rival Shepherd, vendor NPCs for helpers (debug-purchase until 09), competition venue #2.

## Depends on

07 (alarm contract, alpha loop), 02 (region-parameterised land model).

## Definition of Done

- [ ] Choosing *which* direction to expand is a real decision (testers articulate a reason)
- [ ] First Forest silhouette pulls the player into the new biome within minutes of purchase
- [ ] A bad event two regions away is survivable by running back **and** by having bought coverage — both paths tested
- [ ] Cross-region species chain produces the intended "my whole frontier is one system" aha
- [ ] Background sim: a region left alone for a full day-cycle has grown plants and depleted stocks — nothing else changed
- [ ] Walk-time report written: longest plausible round trip measured, verdict on movement aids
- [ ] Region authored entirely as data confirms the chunk template is real

## Risks

- **Embodiment tax shows up here first** (GDD §6.1) — measure it, don't guess.
- Event randomizer must never fire the moment the player leaves (feels scripted-cruel); add a grace window per departure.
- Parcel pricing is the whole progression economy's spine (GDD §3.4) — placeholder numbers fine, but structure (distance × region multiplier) locked this slice.
