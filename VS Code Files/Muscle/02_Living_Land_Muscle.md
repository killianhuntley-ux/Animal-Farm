# Muscle 02 — The Living Land

**Skeleton status:** complete (till/water/sow/dig, watered-growth bonus, requirement engine + F1 overlay).
**Problem:** the land is a spreadsheet with a tint — it never acts on its own, and "lush grassland" is the only implied goal.
All verdicts are owner decisions, 2026-09-30 (question-round format).

## Build status (2026-10-01, wave 2)

Compiled clean, awaiting owner playtest. (Note: verdict 10 "No seasons" is superseded by the Muscle 05 calendar; rain frequency now varies by underworld season.)

**BUILT (by build-list item)**
1. Surface expansion - Sand surface, shallow-rim render state, edge-blended surfaces.
2. BiomeScorer - per-base census (Desert at 40% sand, Swamp at 12% water + 2 plants beside it, Grassland at 40% grass, else Barren), F1 overlay, `BiomeIs` condition with a minimum-score field.
3. Spirit biome affinity - 5-step table on every species; mood gain multiplier; silhouette spawn weighting; road strain; journal "Biomes:" line; Hard No refuses new visitors' feeding and new homes, existing residents drift down to a floor (never evicted).
4. World restructure - PARTIAL: 3x3 home cluster, buffer land, West Road Rights, Reedmire swamp base (4 parcels). Land Office is a deed list (The Registrar), not a graphical base/road map.
5. Rain - weather scheduler by season, visuals + sound, auto-water, pond-edge flood, swamp-lovers enjoy rain (shelter code exists but no species triggers it).
6. Water life - PARTIAL: shallow rim, plantable reed and glowcap lily, water-habitat spirits idle at ponds.
7. Grass bounded spread - built.
8. Crop depth - regrow flags (murkberry, reed), 3 quality tiers, wilt visuals, star badges, sell and feed multipliers.
9. Compost - leavings from happy residents, apply to soil or crop, weed fiber as the humble source.
10. Terrain materials - PARTIAL: Sand loads for sale.

**NOT BUILT**
- 4x4 expansion of the home cluster; Land Office as a world map of bases and roads.
- Shallow-water wading (rim still blocks movement; owner question 5) and plant wind-sway (question 4).
- Rich mud (what it does mechanically is open; question 8) and a separate landscaper vendor (sand is sold by the town vendor).
- Desert-native species, so rain-shelter behavior and Desert bases attracting anyone are untestable (question 9).
- Hard No visitors retreating and fading like silhouettes (question 10) and blocking a home on Hard No ground (question 11).

**ASSUMPTIONs made in build**
- Crop quality: Fine at watered-share score >= 0.55, Gleaming >= 0.90; sell x1.5 / x2.5; spirits fed Fine/Gleaming gain x1.5 / x2.
- Compost: x1.25 growth and +0.15 quality score; each happy resident (Spirit >= 70) rolls a 25% leaving every ~40 s, max 6 uncollected; 3 fiber substitute for 1 compost; fiber sells at 1 obol (owner question 1).
- Pond flood: rises after 1 game hour of rain, recedes 3 game hours after it stops, affects ground within 1 cell of water; never destructive.
- Grass spread: one scrub cell every ~3-5 game hours, only within 2 cells of hand-sown grass.
- Sand: 8 obols for 4 loads at the town vendor, painted via the seed picker on open scrub/grass.
- Affinity spawn weights: Love 6 / Like 3 / Neutral 1 / Dislike 0.2 / Hard No 0; mood gain multipliers Love x1.5 / Like x1.25 / Neutral x1 / Dislike x0.5 / Hard No x0.5; wrong-ground drain 3 Spirit per game hour to a floor of 35; Love ground adds +1.2/h up to 85.
- Rain: enjoyers get +0.25 Spirit per beat (cap 80).

---

## Verdicts (locked)

### 1. BIOMES — the land game's new spine
You deliberately sculpt an area's terrain composition to create a **biome**, and every biome type is a valid goal (not just lush grassland):
- **Swamp** = X amount of shallow water + water plants growing in it
- **Desert/arid** = sand covering most of the land
- Grassland, and more types per region later
Each biome is scored from its terrain mix (surfaces, plant types, water coverage) — the old "vitality" idea becomes **per-biome identity + quality score**.

**Spirit biome affinity — 5-step scale:** Hard No (won't live there) / Dislike (will live, unhappy) / Neutral / Like / **Love**. This becomes a core requirement-engine condition and mood driver.

### 2. WORLD STRUCTURE — satellite bases + road rights (owner, verbatim intent)
- Starting land is a **3x3 parcel cluster**, expandable to **4x4**.
- Deliberately **unbuyable land** surrounds bases — the world must not homogenize into one giant farm.
- You **buy road rights** to reach a new biome area, then buy its parcels one at a time, growing that satellite base toward its own max multi-parcel size.
- Multiple smaller bases, **bringing spirits to and fro** along roads. **Danger on roads is a hook**: escorting a swamp spirit through hell territory because you really want to weave it with a spirit at the hell base.
- Biome unit = the base (parcel cluster). Each base is one biome canvas with its own score.
(This supersedes the current 2-parcel setup and redefines slice 08 Frontier's muscle. Land Office (The Registrar -- Charon appears ONLY at the Styx, owner rule 2026-10-01) parcel overview becomes a world map of bases + roads.)

### 3. Terrain look: edge-blended autotiling
Rounded, blended transition edges (grass laps over dirt, water gets banks). Grid underneath, organic read. No marching-squares experiment this pass.

### 4. Grass spreads a little, bounded
Sown grass softens its edges and fills gaps near the patch over game-days. **Never** a one-tile-covers-the-map colonizer — spread is radius-bounded around deliberately sown ground.

### 5. Weather: simple rain
Occasional rain days — free watering, ambient sound, spirits react (some love it, some shelter). One weather type this pass. **Rain temporarily floods pond edges a little** (shallow rim expands, recedes after).

### 6. Water becomes a place
- Banks + shallow rim (autotiled); shepherd wades the rim slowly (pairs with water-slosh footsteps from Muscle 01).
- **Plantable water species** (reeds, glowcap lilies) — ponds join the planting economy and feed swamp-biome scoring.
- **Spirits hang out at water**: water-liking species path to pond edges to idle.

### 7. Crops: regrow + quality + wilt
- **Regrowing species** (berry-bush style) alongside one-shot crops.
- **Quality: 3 tiers** — Normal / Fine / Gleaming. Driven mostly by watered-percent of grow time; compost nudges up. Star badge on harvest, higher sell price, spirits prefer higher tiers.
- **Wilt visual** when unwatered: droop + desaturate. Growth slows (already does); nothing ever dies (work-never-wasted).

### 8. Compost loop
Happy residents occasionally drop essence-rich leavings; applied to tilled soil they boost growth/quality. Spirits feed the land that feeds the spirits.

### 9. Terrain materials are purchased
Sand, rich mud, and future biome surfaces are **bought by the load from vendors** and painted with existing tools. Economy sink; no tool sprawl. (Fits staggered vendor arrivals — the "landscaper" vendor can be a move-in.)

### 10. No seasons
Regions are the variety axis (prairie, swamp, hell — fixed moods). Day/night + weather carry the rhythm. Time is strange in the underworld.

---

## Build list (ordered)

1. **Surface expansion** — add Sand (and ShallowWater rim as a render state); autotile edge blending for all surface pairs.
2. **BiomeScorer** — per-base terrain-mix census → biome type + quality score; F1 overlay panel; requirement-engine condition atoms (`BiomeIs`, `BiomeScore >=`).
3. **Spirit biome affinity** — 5-step affinity table on SpiritSpeciesDefinition; mood modifier + residency gate wiring.
4. **World restructure** — 3x3 starting cluster (expandable 4x4), unbuyable buffer land, road-rights purchase, satellite base scaffolding (at least ONE second biome base reachable by road this pass). Land Office overview → base/road map.
5. **Rain** — weather scheduler, rain day visuals/sound, auto-water, pond-edge temporary flood, spirit rain reactions.
6. **Water life** — banks/shallow rim, wade-slow, plantable water species, water-idle spirit behavior.
7. **Grass bounded spread** — radius-bounded creep around sown patches.
8. **Crop depth** — regrow flags, 3-tier quality (watered-percent driver), wilt visuals, star badges, price/preference hooks.
9. **Compost** — resident leavings item, apply-to-soil boost.
10. **Terrain material goods** — vendor stock (sand/mud loads), paint via existing tools.

## Carried forward
- Road danger / spirit escort events → slice 08 muscle (Frontier).
- More biome types + region-specific surfaces → region chunks (slice 11).
