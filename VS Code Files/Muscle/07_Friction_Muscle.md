# Muscle 07 — Friction (Repo-man, Weeds, Villain Spirits)

**Skeleton status:** complete (Repo-man reclaim loop + Holding Office, weeds + Watchlights, runaway/herding).
All verdicts are owner decisions, 2026-09-30 (question-round format).

## Build status (2026-10-01, wave 2)

Compiled clean, awaiting owner playtest.

**BUILT (by build-list item)**
1. Repo-man arrival - walks in from the far end of the plaza road through the gates (~1.6 u/s, never teleports), off-key whistle that fades by 22 units, nervous glances from residents within 6 units.
2. Bribe option - modal "Slip him N obols" / "Let him proceed", once per visit, escalating saved price; warning (lecture) visits cannot be bribed.
3. Weed yield - pulling gives +1 fiber; 3 fiber stand in for 1 compost; fiber sells for 1 obol; swamp variant "Mirewort".
4. Villain spirit framework - PARTIAL: Digger / Devourer / Scarer with damage caps, chase and Watchlight drive-off, and the Hoe now flattens holes in one swing. Scheduling is day-count based (2+ residents, day 4+, at most one visit per day), NOT season aware.
5. Reclaim mood fix - reclaimed spirits exit as Residents at Spirit 35, half hungry.

**NOT BUILT**
- Villain season weighting: should visit chance and archetype mix vary by underworld season, and how (owner question 2).
- Throwable deterrents (carried forward). The consumable ward charms from Muscle 08 are the only new defenses.
- Fiber trade hooks beyond vendor sale and compost (owner question 1).

**ASSUMPTIONs made in build**
- Bribe price: 8 obols first, +5 each time it is paid, capped at 40; saved; 'repo bribes N' sets it for testing.
- Confrontation: modal opens automatically when you are within 7 units; he takes the spirit after 25 s of dithering or when you refuse.
- Reclaim exit state: Spirit 35, hunger 50%, hunger clock reset to match.
- Weeds: max 4 live, mature after 48 game hours; fiber 1 obol.
- Villains: daily visit chance 25% + 3% per resident beyond 2 (cap 40%), arriving 08:00-20:00; Digger max 4 holes per visit; a second console 'villain' is refused while one visits.
- Ward charms are consumable and spent automatically on a matching villain (see Muscle 08); the 'villain' console command bypasses them.

---

## Verdicts (locked)

### 1. Repo-man: visible arrival + bribeable
- **Visible arrival**: he walks in along the road (never teleports), whistling off-key; spirits watch him nervously. You see trouble coming.
- **Bribeable**: slip him obols to look the other way once; the price rises each time. Underworld bureaucracy humor.
- No warning-letter system, no evolving dialogue arc (lecture stays as-is).

### 2. Weeds become useful
Still a nuisance (mood-sour, Watchlight wards), but pulling them yields a scrappy resource (fiber/nettle) used in compost or vendor trade. Even weeding pays — work-never-wasted applied to the enemy.

### 3. VILLAIN SPIRITS: untamable chaos agents (owner direction, verbatim intent)
NOT redeemable VP-Sours. Untamable spirits that invade and cause specific kinds of chaos:
- **Digger** — an evil mole-type digs holes in your land that you must flatten/redo.
- **Devourer** — obsessed with plants, eats them ("fine with a little work lost").
- **Scarer** — comes in just to frighten residents and drop their mood.
**Calibration law:** not too much work lost — but if none is ever lost, there is no challenge. Villain damage must be small, visible, and recoverable. They are driven off (chase, Watchlights, maybe thrown things later), never befriended.

### 4. Holding Office reclaim: stays transactional
Pay 2x favored food, spirit released, done. The office is a bureaucracy — coldness IS the joke.
- **Bug to fix regardless:** reclaimed spirits currently exit in runaway mood (they can immediately flee again). Exit mood = subdued-but-stable.

---

## Build list (ordered)

1. **Repo-man arrival** — road-walk entrance path (ties to the Muscle 02 world restructure roads), off-key whistle synth, nervous-glance reaction on residents in his radius.
2. **Bribe option** — dialogue choice at confrontation; escalating price (saved); success = he leaves without the spirit, one-time per visit.
3. **Weed yield** — pull action drops fiber scrap; compost recipe + vendor trade hook.
4. **Villain spirit framework** — invader spawn scheduling (calendar/season aware), three archetypes (Digger: hole tiles needing re-flatten via hoe; Devourer: eats a plant occasionally, telegraphed; Scarer: fear aura mood drain), drive-off responses (player chase proximity, Watchlight wards work on them too), damage caps per visit.
5. **Reclaim mood fix** — exit state correction.

## Carried forward
- Throwable deterrents / defenses beyond Watchlights → later pass once villains are in and tested.
- Road danger events (travel ambush flavor) → slice 08 muscle.
