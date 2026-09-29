# 11 — Region Chunk Template ✂ MILESTONE 4: EA & BEYOND

**Question every chunk answers:** *Does this region give a returning player a reason to come back for a week?*
**GDD:** §5.2/§5.4 (chunk model), §3.3 (region roster), §3.6 (villains 2–3), §9.3 research (EA cadence)

This is not one slice — it's the **repeatable pattern** that carries the game from demo to EA launch (3–4 regions complete, GDD §5.4) through 1.0 (all 7). If slice 08 did its job, a chunk is *content authoring*, not engineering.

---

## The chunk recipe (per region)

Every region chunk ships as one coherent update:

| Component | Amount | Notes |
|---|---|---|
| Biome | terrain palette, ambience bed, weather flavour | region framework data (08) |
| Native species | 3–5 base spirits | conditions reference *this region's* land + at most 1 cross-region edge (GDD §3.2) |
| Woven cryptids | 1–3 recipes using new species | silhouettes seed the journal *before* players can make them — anticipation is content |
| Biome events | 1–2 unique hazards | alarm-contract compliant (GDD §2.4) |
| Competition | 1 new event or venue-variant of an existing one | new *event* every other region is enough; venues re-skin cheaply |
| Final tasks + barks | per new species | the tone budget — never skip; it's why regions feel different, not just recoloured |
| Helper/equipment | 0–1 new purchasable | matched to the region's hazard |
| Price tier | parcel costs, prize money | one tuning-asset edit (09) |

## Region order & specials (GDD §3.3, difficulty-ordered)

1. ~~Prairie~~ (slices 01–07) · 2. ~~Forest~~ (slice 08)
3. **Beach** — first water-heavy conditions; swim locomotion template joins the rig set
4. **Swamp** — introduce **Feral Spirits** (villain 3, GDD §3.6): grief-twisted wanderers, soothe/tame → permanent ward + parallel collection track (recolour + behaviour swap = near-free content, research §5.4)
5. **Mountains** — introduce the **Rival Shepherd** (villain 2, GDD §3.6): border-poaching escalating with fame; his counter-tree spans remaining regions (research §5.3 escalation pattern)
6. **Desolate Underworld** — prestige-tier species; movement-aid decision (GDD §6.3) is due by here at the latest
7. **Hell-like** — hardest lures, the final competition circuit, and the **shepherd's own Ascension** (GDD §3.8): the soft ending, NG+/sandbox continue

## EA rhythm (research §9.3 playbook)

- **EA launch gate:** regions 1–4 complete + stable = "3–4 regions feel complete" (GDD §5.4). Entry price below $19.99 (GDD §5.5).
- One region chunk ≈ one headline EA update. Publish the roadmap as region names only — never dates (passion-project cadence, GDD §5.1).
- Each update: patch-note personality in the tone register; the Repo-man "signs" the balance changes.
- 1.0 = region 7 + ending + price step toward $19.99–24.99.

## Per-chunk Definition of Done

- [ ] Region authored ≥90% in data/assets; engine edits logged as framework debt
- [ ] A returning player finds: new silhouettes within minutes, a new hazard story, a new competition experience, ≥1 weave hunt
- [ ] Difficulty sits above the previous region (lure complexity, event pressure, prices) — verified by a tester who's finished prior regions
- [ ] Villain introductions (Swamp/Mountains) follow the Repo-man's law: first encounter is a *warning/teaching* beat, counters learnable before they bite
- [ ] Cross-region chains tested from a fresh save, not just an endgame save
- [ ] Tone pass: every new species' final task read aloud — did anyone smile?

## Standing risks

- **The 7-region plan is the scope monster (GDD §6.2).** The chunk model is the containment: nothing beyond the current chunk is promised. If energy flags, the game can *ship at 1.0 with 5 regions* — Underworld/Hell compress into one finale region; note this exit early so it never feels like failure.
- Villains 2–3 are the largest post-alpha engineering items hiding in "content" chunks — schedule Swamp and Mountains with engineering time, not pure authoring time.
- Watch the two doors and the essence economy every chunk — new species shift both.
