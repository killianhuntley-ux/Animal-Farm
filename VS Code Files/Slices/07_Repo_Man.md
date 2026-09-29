# 07 — The Repo-man ✂ MILESTONE 2: CORE-LOOP ALPHA

**Question this slice answers:** *Does friction with a face make the farm feel worth defending — without tipping "middling" into cruel?*
**GDD:** §3.6 (Repo-man locked), §1.7 (loss model), §2.4 (alarm contract), §4.2 (leitmotif = alarm), §4.4 (easy mode)

The research verdict this slice implements: a legible threat with visible counters was load-bearing in VP (research §5.7) — but VP's Dastardos was most unfair during the new-player ignorance window. The Repo-man must be *scary the right amount, and never a mystery*.

---

## Builds (systems)

- **Neglect pipeline:** sustained low Spirit on a resident starts the repossession clock (never random — always earned, GDD §3.6). Clear staging: `troubled → flagged (paperwork appears!) → Repo-man dispatched → arrives → repossesses`.
- **The alarm contract (hard requirement, GDD §2.4):**
  1. **His leitmotif starts when he's dispatched** — audible anywhere (GDD §4.2)
  2. UI flag names *which* spirit and *where*
  3. Timer generous enough to run back from anywhere currently reachable — computed from map size, not a constant
- **Counters (learnable before he first matters, GDD §3.6):** soothe the troubled spirit above threshold (cancels dispatch) · pay the fine when he arrives (bribe — expensive, always available) · deterrent purchase later (09's shop). First-ever visit is a **warning visit** — he explains himself, takes nothing (kills the VP ignorance-window problem dead).
- **Repossession (the actual loss):** the spirit is taken *to a holding office* — recoverable via fee + a fetch trip, echoing the runaway model (GDD §1.7: never permanent). Its Spirit resets low; your time and money were the price.
- **Easy mode (labelled, day one — GDD §4.4):** "Gentle Passage" toggle: no Repo-man, gentler timers. Menu-labelled, switchable per save.
- **Character build:** rigged NPC, officious walk, clipboard idle, 8–10 barks in the comic-bureaucrat register (GDD §4.5 — every beat passes "is this a joke?").

## Content

- Repo-man rig + skin (first real character-art collaboration piece), leitmotif placeholder (musician-friend brief: *officious, tuba-adjacent, funny-ominous*), holding-office greybox at the map edge, fine/fee placeholder numbers.

## Out of scope

Rival Shepherd and Feral Spirits (villains 2–3 — region-chunk content, slice 11), biome events.

## Depends on

03 (Spirit/neglect), 04 (stakes exist because investment exists), 06 preferred (so alpha contains the full decision space).

## Definition of Done — Core-Loop Alpha acceptance

- [ ] The full modern loop exists: lure → tend → compete → fulfil → **defend** → weave/ascend
- [ ] Leitmotif alone is enough for testers to react ("oh no, he's coming") before any UI is read
- [ ] A tester who ignores the alarm loses the spirit, recovers it, and rates the exchange "fair"
- [ ] First-visit warning teaches 100% of testers the counter without text walls
- [ ] Gentle Passage verified: no Repo-man pressure, rest of game intact
- [ ] Tone check: he's funny. If testers describe him as stressful-but-not-funny, the barks/staging need another pass
- [ ] ✂ Alpha build packaged for outside friends; feedback template ready (what they tried, where they stalled, what they ignored — watch the two doors, GDD §6.1)

## Risks

- Timer tuning IS the "middling" dial (GDD §1.7) — err generous; tighten only with playtest evidence.
- One villain must carry all friction until slice 11 — if the alpha feels flat *between* his visits, note it for biome events rather than making him more frequent (frequency is how VP overshot).
