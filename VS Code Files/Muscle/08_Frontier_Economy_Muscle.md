# Muscle 08 + 09 — Frontier (World) + Economy (Vendors)

**Skeleton status:** parcels/gates/LandOffice built (2-parcel version); vendor + Land Office (was Ferryman; Charon rule 2026-10-01: Charon appears ONLY at the Styx) stalls, obols, essence dual-purpose all live.
**Superseded by:** the Muscle 02 world restructure (3x3 start cluster, unbuyable buffers, road rights, satellite bases).
All verdicts are owner decisions, 2026-09-30 (question-round format).

## Build status (2026-10-01, wave 2)

Compiled clean, awaiting owner playtest.

**BUILT (by build-list item)**
1. World restructure core - 3x3 start cluster, unbuyable buffer, West Road Rights at the Land Office (The Registrar; Charon appears ONLY at the Styx), road corridor, Reedmire swamp base bought parcel by parcel.
2. Road travel - escort-follow ("Come along" / treat), dark stretch + Road Lantern, biome-mismatch strain, toll imps, ambush with bolt + herd recovery, defense-item checks.
3. Swamp content - plantable reed and glowcap lily, "Mirewort" weed variant, three swamp species (Bogwick, Reedhen, Sloughling), swamp scoring target, one swamp-exclusive vendor (the Mire Peddler).
4. Traveling merchant - calendar-driven stall (days 5, 10, 15).
5. Pouty Mount - "Grudge": joins at 5 owned fields, join scene, V ride toggle, pouts when ignored, perks when fed, never in ascension/weave/repo lists, excludes sitting.
6. Defense items - Bell Charm (Scarer), Bitter Bait (Devourer), Mud-Stone (Digger, swamp only).

**NOT BUILT**
- 4x4 expansion of the home cluster; Land Office as a world map of bases and roads.
- Hell region and the ice biome with its cross-biome vendor (slice 11 template); scripted merchant personalities.
- Biome-exclusive vendors beyond the swamp one; rich mud; a desert-native species (owner questions 8 and 9).
- The Registrar is a placeholder character (owner question 7).

**ASSUMPTIONs made in build**
- Pouty mount joins at 5 owned FIELD parcels (free hearth + 4 bought; road rights do not count). Contentment starts at 65, drains 2/h after 4 ignored game-hours, pouts under 30 and refuses rides; berries +40 (perked 3 h), other crops +15 (1 h), petting +25 with a 20 s cooldown; riding x1.5 speed (x1.75 perked), half the bonus on sprint. Name "Grudge" is a placeholder (owner question 12).
- Road territories are stylised: Hearth Verge (Grassland), Dust Reach (Desert), Mire Fringe (Swamp); strain Dislike 0.9 / Hard No 2.2 Spirit per second, floor 30.
- Toll imps: ~40% of days (deterministic per day), 06:00-20:00, 2 obols + 1 per escort, cap 6; refusing costs each escort 5 Spirit (floor 35).
- Ambush: at most once per day, ~35% per crossing with escorts; Scarer frightens escorts within 4.5 units (-6, floor 25), 60% bolt each; Devourer lunges at the nearest (always bolts) + 35% a second; max 2 bolts; bolted spirits need 2 herding tags.
- Dark stretch: x0.55 escort speed and -1.1 Spirit/s (floor 35) without a lantern.
- Lantern is DURABLE (25 obols, carrying one is enough); ward charms are CONSUMABLE one-shots (Bell Charm 8, Bitter Bait 8, Mud-Stone 10); Watchlights also fend ambushes.
- Mire Peddler stock: Peat Compost x3 for 12 obols and Mud-Stone for 10 (which problems he solves is a guess).
- Sand: town vendor, 8 obols per 4 loads. Land deed prices: West 120, North 140, East 150, South 160, NW 220, NE 240, SW 260, SE 280, West Road Rights 200, Reedmire 240 / 260 / 280 / 300.

---

## Verdicts (locked)

### 1. Travel: walk the roads; the Pouty Mount comes later
Roads are real dressed corridors you walk, escorted spirits trailing. After a certain number of parcels are unlocked, a **horse-like spirit with a pouty, characterful personality joins you** — it becomes a **rideable mount** and **never wants to ascend**. The permanent companion. (No ferryman fast-travel; the mount IS the speed upgrade.)

### 2. Road dangers: all four, plus purchasable defenses
- **Villain ambush** — a Scarer/Devourer bursts out; escorted spirits may bolt and scatter; on-the-spot herding recovery (reuses chase tech).
- **Dark stretches** — pitch-dark segments; without the purchasable lantern, spirits slow and moods dip.
- **Biome mismatch strain** — a swamp spirit walked through hostile territory drains mood over the trip; route planning matters (the weave-expedition hook).
- **Toll imps** — cheeky checkpoint imps demand small obol tolls on some days.
- **Defense items**: vendors sell specific wards/items that fend off specific villain types — gear up for the road you're about to walk.

### 3. First satellite base: SWAMP
Exercises everything Muscle 02 built (shallow water %, water plants, water-idle spirits, rain). Region ladder begins: prairie, swamp, (hell later).

### 4. Selling: vendors only — Charon carries souls, not cabbage
No shipping crate: **Charon only ever collects the ascended** (fiction discipline). All selling is face-to-face at vendors — it pushes map exploration and road-danger engagement. On top:
- **Traveling merchants** visit your bases periodically (calendar-driven).
- **Biome-exclusive vendors**: certain vendors only appear in certain biomes, and their stock solves OTHER biomes' problems (an ice-biome vendor sells the tool that helps in the hell-like biome) — cross-biome dependency shopping that makes every region commercially relevant.

### (Carried from earlier rounds, restated for this doc)
- Staggered vendor move-ins via hidden play milestones (Muscle 01).
- Terrain materials (sand, mud) bought by the load (Muscle 02).
- Tool tiers at the Blacksmith, farmer-level + obols (Muscle 01).

---

## Build list (ordered)

1. **World restructure core** (shared with Muscle 02 item 4) — 3x3 start cluster, unbuyable buffer, road-rights purchase at the Land Office (The Registrar), road corridor generation, swamp satellite base (parcel-by-parcel purchase).
2. **Road travel** — corridor dressing, escort-follow for selected spirits, dark stretches + lantern item, biome-strain mood drain, toll imp encounters, ambush events with scatter+herd recovery, defense-item checks.
3. **Swamp content** — swamp surfaces/plants/weed variant, 2-3 swamp spirit species (data), swamp biome scoring target, one swamp-exclusive vendor.
4. **Traveling merchant** — calendar-scheduled visitor stall at your base with rotating stock.
5. **Pouty Mount** — spawn trigger at parcel-count threshold, join scene, ride toggle (speed boost, dismount to interact), personality beats (pouts when ignored, perks when fed), never appears in ascension-eligible lists.
6. **Defense items** — per-villain ward items in vendor stock (consumable or durable, decide in build).

## Carried forward
- Hell region + ice biome (and its cross-biome vendor) → region chunks (slice 11 template).
- Scripted merchant characters/personalities → post-muscle content pass.
