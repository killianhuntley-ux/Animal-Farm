# Muscle 02 — The Living Land

**Skeleton status:** complete (till/water/sow/dig, watered-growth bonus, requirement engine + F1 overlay).
**Problem:** the land is a spreadsheet with a tint — it never acts on its own, and "lush grassland" is the only implied goal.
All verdicts are owner decisions, 2026-09-30 (question-round format).

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
(This supersedes the current 2-parcel setup and redefines slice 08 Frontier's muscle. Ferryman parcel overview becomes a world map of bases + roads.)

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
4. **World restructure** — 3x3 starting cluster (expandable 4x4), unbuyable buffer land, road-rights purchase, satellite base scaffolding (at least ONE second biome base reachable by road this pass). Ferryman overview → base/road map.
5. **Rain** — weather scheduler, rain day visuals/sound, auto-water, pond-edge temporary flood, spirit rain reactions.
6. **Water life** — banks/shallow rim, wade-slow, plantable water species, water-idle spirit behavior.
7. **Grass bounded spread** — radius-bounded creep around sown patches.
8. **Crop depth** — regrow flags, 3-tier quality (watered-percent driver), wilt visuals, star badges, price/preference hooks.
9. **Compost** — resident leavings item, apply-to-soil boost.
10. **Terrain material goods** — vendor stock (sand/mud loads), paint via existing tools.

## Carried forward
- Road danger / spirit escort events → slice 08 muscle (Frontier).
- More biome types + region-specific surfaces → region chunks (slice 11).
