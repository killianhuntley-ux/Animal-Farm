# Game Master File — Master Design Document

**Project codename:** Animal Farm (working title)
**Engine:** Unity 6000.1.15f1, 2D URP
**Companion doc:** [Initial_Research.md](Initial_Research.md) — Viva Piñata analysis & 2026 market
**Status:** ✅ v1 complete — all six passes interviewed and locked (open items tracked in §6.3)
**Last updated:** 2026-09-28

---

## 1. Vision & Identity
*Status: ✅ locked (art look may evolve; functions stable)*

### 1.1 High Concept (one paragraph)
> **Animal Farm** is a 2D creature-attraction sim with a dark-comic streak. As a shepherd on foot, you complete tasks and shape the land to lure strange creatures into territory you control — expanding your holdings outward in any direction you can afford, each new region bringing harsher conditions and stranger residents. Prized creatures can be entered into **competitions**: minigames that raise their fame and value. The further you push, the harder creatures are to win — and to keep.

### 1.2 The Player Fantasy
> *"I am a shepherd."* An embodied avatar who physically walks the map — **not** a floating god-cursor (deliberate inversion of VP). The creatures *choose* to come because you made the place worth coming to; retention is earned, not owned. Fame layer: your flock becomes *prized* through competition.
>
> **Design consequence to resolve (Pass 2):** embodiment makes travel, presence, and attention into resources. Things can happen where you aren't. This is a core system decision, not a camera choice.

### 1.3 The Central Conceit — Spirits
> **The creatures are spirits wearing animal shapes** — including cryptids and impossible beasts. The player is a **shepherd of souls** (a psychopomp): the farm is a *waystation* between lives. Spirits arrive dim and troubled; a well-kept territory, good care, and competition glory raise their **Spirit** until they are so prized they *willingly move on to the next life* — **Ascension**, the game's crowning act and its piñata-breaking equivalent (loss reframed as graduation).
>
> The three jobs test (research §10.5) — passed:
> 1. **Art language:** one ghostly cartoon-drawing grammar wraps any silhouette, cryptid or animal, and is cheap to draw and animate. Look may evolve during production; functions stay stable.
> 2. **Loss is tonally safe:** they're already dead. Fleeing, fading, or sulking is dark-funny, never gory.
> 3. **Generates the core mechanic:** Spirit (morale) → prizing/fame via competitions → willing Ascension → permanent legacy reward. Built-in roster churn = built-in endgame and a fictional answer to the population cap.

### 1.4 Design Pillars (ranked)
> 1. **Attraction, not acquisition.** Creatures come because the place is worth coming to — and stay only while it remains so. Luring difficulty *and* retention difficulty scale together.
> 2. **The frontier is yours to draw.** No fixed plot, no discrete zones — contiguous expansion in any direction you can afford. Each direction/region carries unique challenges and unique creatures. The shape of your holdings is your signature.
> 3. **Walk the land.** The shepherd is a body on the map. Distance, presence, and attention are real costs.
> 4. **Cute with teeth.** Exaggerated dark humor — dark enough to be funny, never actually evil. Charm on the surface, real stakes underneath.

### 1.5 Target Player & Positioning
> The **Cult of the Lamb buyer**: adult Steam indie player, comfortable with cute-dark tonal blend, $19.99–24.99 comfort zone. The CotL tone choice is the positioning strategy — it pre-empts the "kids' game" misread that suppressed VP's adult audience (research §10.4). Depth is signalled by tone, not disclaimed in marketing.
>
> **Differentiator statement:** *"It's like Viva Piñata, but you're a shepherd on an open frontier — you expand wherever you can afford, the creatures get stranger than any real animal, and instead of sending them to parties you enter them in competitions."*

### 1.6 What This Game Is NOT
> - Not a combat game (competitions are minigames, not battles)
> - Not a crops-first farming sim — the land serves the creatures, not the other way round
> - Not a god-view zoo/park manager
> - Not live-service / always-online
> - Not a VP remake — it competes on structure (expansion, embodiment, competitions), not nostalgia

### 1.7 Friction Dial & Loss Model
> **Middling — losses cost time, effort, and resources; they never permanently erase investment.**
> - **Plants can die.** Real loss, replantable.
> - **Spirits never die — they run away.** Recovery via a fetch system or a **herding-them-back minigame** (reinforces the shepherd fantasy).
> - **Competitions can be lost**, and losing **hurts the Spirit of the spirits** (morale hit) — stakes without permanence.
> - No permanent creature death. The only way a spirit leaves forever is the *good* ending: Ascension.

### 1.8 Open Flags
> - **Title:** "Animal Farm" is a **placeholder** (confirmed). Orwell collision on storefront search — rename before any public-facing use. The psychopomp/waystation framing suggests naming directions (waystation, crossing, flock, passage, threshold…).
> - Art look expected to evolve during production; mechanical functions designed to be stable underneath.

---

## 2. Core Loop & Systems
*Status: 🟡 mostly locked — day/night details & weaving depth open*

### 2.1 Core Loop
> **Minute:** walk the land · tend (water, feed, soothe) · respond to trouble alerts (run back or trust helpers) · herd runaways · place/build · inspect silhouettes at the borders.
> **Session (~30–60 min):** progress a lure (terraform toward a target species' conditions) · enter a competition · push a spirit toward Fulfilment · perform an Ascension or a Weave · buy an expansion or helper coverage.
> **Arc (multi-session):** open a new frontier direction · chase a cryptid weave recipe · complete regional collection goals.
> **Quit-anytime is sacred:** pause and save anywhere, zero progress loss (see 2.5).

### 2.2 Attraction & the Spirit Lifecycle
> **Environmental conditions are the core attraction machine** — the land state is the lock (terrain %, plants, items, resident species, time of day). This is deliberately preserved from VP (owner's favourite system) and implemented as the research recommends: one requirement engine, per-species data (ScriptableObjects), difficulty as swappable rule-sets.
>
> **The lifecycle (replaces VP's four gates):**
> ```
> APPEAR (env conditions met → silhouette at border)
>   → VISIT → RESIDENT (env + care conditions)
>     → FULFILLED (fed well, competition glory, bond)
>       → Door 1: ASCEND — a final personal task, then the spirit
>          willingly moves on; player earns a permanent legacy reward
>          (goodbye is compensated, never punished)
>       → Door 2: WEAVE — fuse with another Fulfilled spirit (see 2.7)
> ```
> **Two doors competing for your best spirits is the game's core strategic decision.** Personal tasks live at the *end* of the lifecycle (Ascension), not the start — conditions stay systemic; tasks deliver the tone.

### 2.3 The Ecosystem Simulation (autonomy & absence)
> **Decided: "C with a dash of A"** (from Pass 1 Q9). While the shepherd is elsewhere:
> - **Bad events are timer-based with a randomizer** — trouble starts a *generous* clock and the shepherd physically runs back (tension from distance; alert must fire early enough to act — research §5.6 alert-latency lesson is a hard requirement).
> - **Coverage is purchasable:** "supervisor" equipment and helpers can be assigned to regions you leave — VP's "automation as a purchase" pattern.
> - **Lightweight background simulation only for deterministic systems:** plant growth, food-stock depletion. No full agent sim in unattended regions (performance + tuning sanity as the map expands).

### 2.4 Friction, Failure & Stakes
> Loss model locked in §1.7; friction cast locked in §3.6.
> **Bad-event framework:** two families — **villain events** (Repo-man approach, Rival poaching attempt, feral spirit incursion) and **biome-unique events** (per-region hazards, authored alongside each region; event list develops with region production).
> All events obey the alarm contract: fire early enough to act on foot from anywhere reachable, generous timers, helper coverage as the purchasable alternative (2.3).

### 2.5 Time, Weather & Offline Behaviour
> **No offline passage of time — locked.** Pause and save anywhere; the player must always feel safe walking away. Rationale (owner): "as an adult, if you are to lose progress, you lose interest." All simulation runs only while playing.
> **Day/night — locked:** full cycle = **20 real minutes**, with a **fast-forward option** for reaching a target time-of-day. Day and night are *equally* weighted; activity windows are **per-spirit** (some diurnal, some nocturnal) — time-of-day is a lure condition, as in VP.
> **Weather:** TBD (per-region, see 3.3).

### 2.6 Player Verbs & Tools
> Base set (locked as a starting point, will grow): **walk · plant · terraform/dig · feed by hand · soothe/pet · herd · play the shepherd's flute** (candidate unified "interact" verb: calm, lure, and herd are all flute-driven). Tool/ability progression track TBD — VP's dual-axis pattern (level-granted + purchasable) recommended.

### 2.7 Weaving (spirit fusion) — replaces breeding
> **No classic breeding.** Roster inflow = attraction; outflow = Ascension; *creation* = **Weaving**.
> - Two spirits, **both at MAX Spirit** ("a sad spirit won't want to ascend to a higher creature plane"), are woven into one new spirit. **Both parents are consumed** — locked. The weave inherits a trait from each.
> - **Cryptids come only from weaving — locked.** Folklore's hybrids (jackalope = rabbit + antelope; griffin = eagle + lion) are the recipe book. They never appear wild.
> - **The Journal shows unknown weaves as silhouette entries** — it never reveals recipes. Discovery is the experimentation/collection layer (replaces VP's variant-feeding).
> - **One generation only — locked.** Weaves cannot be re-woven. Deeper chains reserved as expansion/DLC headroom.
> - **Naming compounds:** a weave's default name is a portmanteau of its parents' *player-given* names.
> - **Failure is gentle:** invalid pairing fizzles — morale hit and a joke, nothing lost (friction dial §1.7).
> - Content economics: ~20–30 hand-drawn base species + ~15–20 authored woven cryptids = a 50-species roster where the rare tier is earned through play (research §9.2, CritterGarden multiplier).
> - **Spirit (morale) is the master resource:** it gates competitions performance, Ascension, Weaving, *and* essence income (3.5) — every system runs through "are they happy."

### 2.8 Competitions (the hook)
> **The player pilots the spirit** — active skill minigames, not stat-watching. Spirit stats/morale modulate performance (stamina pool, speed, etc.).
> - **Entry costs money; skipping costs nothing.** Losing hurts the spirit's morale — stakes without permanence.
> - **Single events** per discipline + **multi-discipline triathlon-style meets** for versatile spirits.
> - Confirmed events: **Boulder trial** (Sisyphus as a sport — time your pushes, manage stamina, don't let it roll back; first to the summit) · **Race** (track with Mario-Kart-style pop-up boosts).
> - **Design direction: competitions are afterlife ordeals reskinned as sports.** The myth canon (ferry crossings, judgment scales, three-headed-dog shows…) is the event roster's source book — every event doubles as a joke that fits the fiction.
> - Winning raises fame/prize status → feeds Fulfilment → feeds the two-door endgame.
> - The **herding trial** doubles as the runaway-recovery minigame — one system, two jobs.

---

## 3. Content Architecture
*Status: ⏳ interviewing — NPCs, antagonist, endgame open*

### 3.1 Creature Roster
> Target: **~20–30 base spirit species + ~15–20 woven cryptids** (see 2.7). Tiers emerge from region difficulty (3.3), not labels. Naming convention for species TBD (Pass 4 tone work) — research §8.2 rules apply: splice at a shared phoneme, rarest theme-word for latest species.

### 3.2 Dependency Graph Philosophy
> Regions are the natural tier structure — a species' conditions reference *its region's* terrain, plants, and neighbours, keeping chains mostly region-local (shippable and testable region by region; avoids VP's "everything blocks everything" trap, research §2.2). A handful of deliberate cross-region chains for late-game "wow." Every region keeps an unblockable on-ramp species.

### 3.3 World, Space & Biomes
> **Contiguous frontier expansion — buy land in any direction.** Distance from home = difficulty axis: stranger spirits, worse events, pricier upkeep.
> Region flavours (difficulty-ordered, first draft): **Prairie** (starter, most basic species) → **Forest** → **Beach** → **Swamp** → **Mountains** → **Desolate Underworld** → **Hell-like** (hardest).
> Each region differs by: **native species pool · environmental hazard · terrain palette/materials · competition venue.**

### 3.4 Progression & Unlocks
> **Economic/organic gating is primary — no explicit player level.** Money → new zones → new materials, new competitions, new spirit types. Expansion *is* the progression system. Fame accrues to individual spirits (competition record), not a separate player-XP track. ⏳ open: whether shepherd reputation exists as a soft secondary gate (e.g. prestige events requiring N ascensions).

### 3.5 Economy
> **Income:** selling plants/produce · **spirit essence** — happy spirits passively shed a little of their spirit as a harvestable byproduct (the candiosity analogue: contentment *is* production) · competition prize money.
> **Sinks:** land purchases (primary) · competition entry fees · helpers/supervisor equipment · buildings · seeds/materials.
> **Spirits are never sold** — essence replaces the sell-the-creature economy; fits the fiction.
> ⏳ open: is essence plain money, or the **ritual currency consumed by Weaving** (which would unify "keep everyone happy" and "make cryptids" into one pursuit)?

### 3.6 NPCs / Services
> **The friction cast — locked (all three):**
> - **The Repo-man of Souls** *(Dastardos analogue)* — a comically officious collector (clipboard, stamps, "I don't make the rules") who comes for **neglected spirits**. Summoned by low Spirit, never random; visible walk-in with a generous approach timer; counterable (soothe the spirit in time, bribes, deterrent purchases). Research §5.2 lessons baked in: telegraphed early, counters learnable before he first matters.
> - **The Rival Shepherd** *(Pester analogue)* — poaches lured spirits at your borders before they commit residency; escalates with your fame; each comfort-solution eventually gets a counter-counter (research §5.3 escalation pattern).
> - **Feral Spirits** *(sour track)* — grief-twisted spirits wandering in from deep regions, spooking the flock until soothed/tamed. Taming one first-time = permanent ward progress + parallel collection layer. Recolour + behaviour-swap on existing rigs = near-free content (research §5.4).
>
> **Service NPC roster — PROPOSED DRAFT (react/replace):** a general vendor (seeds, materials, food) · a builder (structures, supervisor equipment) · **the Ferryman** (sells land deeds — expansion is literally buying passage into new territory; also the competition circuit's master of ceremonies?) · a healer/medium (morale recovery services). Lean cast; each must have one clear mechanical job (research §4.4).

### 3.7 Collection Layers
> - Weave recipe discovery (journal silhouettes, 2.7)
> - Per-spirit competition records / fame
> - Ascension legacy collection (permanent rewards gallery)
> - ⏳ open: colour/variant layer on base species — cheap perceived-roster multiplier, decide in Pass 5 scope.

### 3.8 Endgame & Retention
> **Soft ending — locked: the shepherd's own Ascension is the goal.** The player's arc mirrors the spirits': tend, prize, fulfil — then take the ferry yourself. Post-credits: **New Game+** (a new shepherd) or **continue in sandbox**. The whole game retroactively reads as *your* fulfilment quest — the design's thesis statement.
> Retention beneath the ending: Ascension/attraction churn, weave recipe hunting, competition mastery tiers, feral taming wards, region collection goals.

---

## 4. Presentation
*Status: ✅ locked at direction level (specifics develop in production)*

### 4.1 Art Direction
> **Camera: 3/4 angled** (CotL-style) — locked. Every sprite sheet drawn to this from day one.
> **Technique: skeletal/puppet rigs** (Unity 2D rig / Spine; CotL's own method) — locked. Dramatically cheaper per creature at a 40–50 species roster, and it survives art-style evolution: re-skin the rigs, keep the animation.
> Style: cartoon drawings, dark-comic register, ghostly grammar unifying all silhouettes (§1.3). Look may evolve; rigs and functions stay stable.
> Barren→lush remains the progress readout (research §6.1): claimed land visibly warms/brightens as it's tended.

### 4.2 Audio Direction
> **Minecraft's model — locked:** silence is the default; low ambient music surfaces occasionally; rich ambient beds (per-region, per-time-of-day) carry the mood. No wall-to-wall score (matches research §6.2 — VP's "adaptive music" was mostly restraint).
> **Leitmotifs as systems:** villains get themes that play *while they act* — **the Repo-man's theme IS the alarm** (diegetic early warning, audible anywhere, satisfies the alert-latency contract). Ascension and Weaving get ceremonial themes.
> **Creature voices: human-performed gibberish** per species (research: cheap, funny, zero localisation cost).

### 4.3 UI & Information Design
> - **Ghost silhouettes in-world** for almost-unlockable species (VP's best idea, research §6.3) — doubles as the Journal's unknown-weave presentation (§2.7).
> - **Journal/encyclopedia** carries each species' full condition chain, progressively discovered; never reveals weave recipes.
> - Hot-key the top 3–4 tools; radial menu only for rare/contextual (research §6.3).
> - Interruption budget: modal pauses ONLY for stakes-bearing events. No nag popups.
> - Alerts must always fire early enough to act on foot (hard requirement, §2.3/2.4).

### 4.4 Onboarding
> **Commitments (locked):**
> - Short, skippable first hour; first successful lure within the first ten minutes.
> - Teach one system per real action, just-in-time, in-world; never steal control beyond seconds; no interruption cascades (VP's four stacked failures, documented in research §6.4, all inverted).
> - Trust the discovery systems (silhouettes + journal) to do the pulling.
> - Don't overcorrect into hyperactivity (TiP's documented failure mode).
> - **Labelled easy mode from day one:** no Repo-man, no ferals, gentle timers (VP "Just For Fun" pattern).
> **The Guide:** a disembodied shining light — "a god," deliberately unnamed and non-denominational; foreshadows the Ascension theme. Teaches by *pointing*, not lecturing. **Always honest** (no unreliable-narrator without a tell).

### 4.5 Tone & Writing
> Register: exaggerated dark humor, never actually evil (§1.5). Comic bureaucracy of death (Repo-man's clipboard), afterlife ordeals as sports (§2.8), waiver jokes at the Weave.
> **Species naming — two-register formula (proposed):**
> - **Base species:** `animal × death/horror pun`, spliced at a shared phoneme (VP rules, research §8.2 — rarest pun-words saved for latest species).
> - **Woven cryptids:** `mythological puns` — fitting, since the cryptids literally *are* the myth canon (§2.7).
> Actual names authored later; individual weaves auto-name as parent-name portmanteaus (§2.7).

---

## 5. Scope & Production
*Status: ✅ locked at strategy level*

### 5.1 Team & Constraints
> **Solo developer** (owner, building in Unity with AI assistance) + **artist friends** (skeletal-rig sprites — the most outsourceable piece) + **musician friends** (score, ambient beds, possibly gibberish VO). Passion project cadence — no deadline pressure, which makes **scope discipline the substitute for a producer.** Friend-contributor availability is a scheduling risk, not a cost risk (see 6.2).

### 5.2 Scope Targets
> - Full vision: **7 regions · ~20–30 base species · ~15–20 woven cryptids · several competition disciplines**.
> - Shipped in **chunks**: core loop first, then regions/species/competitions added in coherent packs (maps perfectly onto EA content-update rhythm).
> - Entity budget: VP's 32-creature cap proved sufficient on 2006 hardware; per-region soft caps around that order are a design choice, not a constraint (research §3.2).

### 5.3 Platform & Release Strategy
> **Passion project → public when it feels right.** Path: private demo/alpha (core loop validation) → **Steam Early Access** at a **lower entry price** than the genre's $19.99 norm, raising toward it at 1.0 (Mistria/Dinkum playbook, research §9.3) → Switch as post-1.0 roadmap item. Single-player, offline, no live-service — locked (§2.5, research §10.3).

### 5.4 Milestones
> 1. **Vertical slice** — one zone (Prairie), controls feel, one full lifecycle (lure → resident → fulfil → ascend), a few creatures. *Detailed slice plan is the next document after this GDD.*
> 2. **Core-loop alpha** — slice + Repo-man + one competition + weaving; validates the two-door decision loop with real players.
> 3. **Chunk cadence** — each subsequent chunk = a region pack (biome + native species + hazard + competition venue). "If the core works, every added chunk lands on solid ground" (owner's model — correct one).
> 4. EA launch when 3–4 regions feel complete; remaining regions ship as EA updates.

### 5.5 Price Point
> EA entry **below** $19.99 (exact number at launch-readiness), stepping up at 1.0 toward the validated $19.99–24.99 band. Never above it (Fae Farm lesson, research §9.2).

---

## 6. Risks & Open Questions
*Status: ✅ drafted*

### 6.1 Design Risks
> - **Competitions are the hook and minigames are hard.** Piloting-the-spirit minigames must feel good or the pitch's headline feature is hollow — this is why one competition is in the alpha, not deferred. Mitigation: prototype the boulder trial early and cheap; kill/replace any event that isn't fun in greybox.
> - **Two-door tension needs tuning.** If Ascension legacies or Weave outputs are mispriced against each other, one door becomes dead. Mitigation: both doors in alpha; watch which one players ignore.
> - **Embodiment tax.** Walking everywhere can become tedium as the map grows. Mitigation: fast-forward exists for time; consider movement aids (mount-spirit? ferry stops between regions) as regions multiply — decide when the map is big enough to hurt.
> - **Friction tuning.** "Middling" is a knife-edge (VP overshot for its audience). Mitigation: easy mode from day one; alarm-latency contract is non-negotiable; villain timers err generous.
> - **Tone drift.** Dark-funny can slide grim or twee. Mitigation: CotL as the calibration reference; every villain beat must pass "is this a joke?"

### 6.2 Production Risks
> - **Solo + friends = availability risk.** Art and music arrive on friendship schedules. Mitigation: skeletal rigs mean placeholder art is *swappable* without rework — build everything against rigs from day one; never block code on final art.
> - **7 regions × bespoke species/hazards/venues is the scope monster.** Mitigation: the chunk model — nothing beyond the current chunk is promised to anyone, including yourself.
> - **The requirement engine must be data-driven from the first line** (ScriptableObjects, conditions as data) or every species added later costs code (research §2.3). This is the one piece of architecture worth over-engineering.
> - **Minigame variety cost.** Each competition is effectively a small game. Budget them like features, not content.

### 6.3 Deliberately Deferred Decisions
> - Real title (codename "Animal Farm" must be replaced pre-announcement — Orwell collision, §1.8)
> - Essence as plain money vs. Weave ritual currency (§3.5)
> - Shepherd reputation as a soft secondary gate (§3.4)
> - Colour/variant layer on base species (§3.7)
> - Weather system detail (per-region, §3.3)
> - Species names (formula locked §4.5; authoring later)
> - Movement aids for a grown map (§6.1)
> - Weave chains beyond one generation (DLC headroom, §2.7)

---

## Next Document
> **Vertical Slice Plan** — to be created on request: Prairie zone scope, species shortlist, systems checklist, greybox milestones, and the definition of "controls feel good."
