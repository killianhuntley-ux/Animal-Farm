# 05 — The Boulder Trial (first competition)

**Question this slice answers:** *Is piloting your spirit in a competition actually fun?* Competitions are the pitch's headline (GDD §1.1) and minigames are the design's named #1 risk (GDD §6.1). This slice is the kill-or-cure.
**GDD:** §2.8 (competitions), §6.1 (kill criteria)

---

## Builds (systems)

- **Competition framework** (generic; the boulder is its first client):
  - venue scene/subscene, entry flow (pick spirit → pay fee → load event), results flow (placement → prize → morale effect → fame record)
  - **stat bridge:** the piloted spirit's stats + current Spirit modulate minigame parameters (stamina pool, push power, recovery rate) — *the creature matters, but the hands win it* (GDD §2.8)
  - AI rivals: 2–3 competitor spirits with rubber-band-free difficulty tiers
  - per-spirit **fame record** in the journal (feeds Fulfil formula from 04)
- **The Boulder Trial (Sisyphus as sport, GDD §2.8):** push a boulder up a slope; it rolls back when unpushed; pushing drains stamina; overexert = forced rest while it rolls. Timing/rhythm skill. First to the summit. 60–120 seconds per heat.
- **Losing:** costs the entry fee + a Spirit (morale) dip — stakes without permanence (GDD §1.7). Skipping events costs nothing.
- **Herding minigame (small, same input tech):** the runaway-recovery loop from 03 upgraded into a real mini-activity — steer/guide the fleeing spirit home (flock pressure, flute if built). One system, two jobs (GDD §2.8).

## Content

- Boulder venue greybox (Prairie fairground), 3 difficulty tiers, entry fees/prizes (placeholder numbers), 2 rival spirit skins.
- Competition UI: entry board, live stamina/progress readout, results card.

## Out of scope

Race event, triathlon meets, spectator economy, venue art.

## Depends on

04 (Spirit/fulfilment to feed into and out of), 01 (input feel foundation).

## Definition of Done

- [ ] The boulder trial is fun *bare* — testers replay it voluntarily ≥3 times without being asked
- [ ] A high-Spirit, well-fed spirit demonstrably outperforms a neglected one with equal player skill — and the player can *feel* why
- [ ] Losing stings but produces "one more go", not a quit
- [ ] Winning visibly advances fulfilment (04's formula now includes glory)
- [ ] Adding a hypothetical second event = new scene + data, no framework edits
- [ ] Herding recovery is a 30–60s activity with a skill component

## Kill criteria (honest, pre-committed — GDD §6.1)

If after two full iteration passes the boulder isn't fun in greybox: **kill the event, keep the framework**, and prototype the race (Mario-Kart pop-up boosts, GDD §2.8) as the flagship instead. If *no* competition prototype is fun after three attempts, escalate to a design review of the hook itself before building more content — do not sand a dead minigame for months.

## Risks

- Minigame feel eats time unpredictably — timebox iterations (e.g., 1-week passes).
- Stat bridge must not swamp skill (feels rigged) nor vanish (creature irrelevant). Target: stats set your *ceiling*, hands decide within it.
