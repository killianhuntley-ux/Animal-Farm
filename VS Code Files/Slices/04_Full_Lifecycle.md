# 04 — The Full Lifecycle ✂ MILESTONE 1: VERTICAL SLICE

**Question this slice answers:** *Does saying goodbye feel good?* Ascension is the piñata-breaking equivalent (GDD §1.3) — if the player hoards spirits instead of ascending them, the game's thesis fails here, cheaply, where it can still be fixed.
**GDD:** §2.2 (two-door lifecycle back half), §3.8 (ending logic in miniature), §1.7 (goodbye is compensated)

---

## Builds (systems)

- **Fulfilment state:** a resident whose Fulfil gate passes (fed well + high Spirit; competition glory joins the formula in 05) enters a visible *radiant* state. Fulfilled spirits glow — the farm's trophy shelf is ambient.
- **The final personal task:** on fulfilment, the spirit reveals one authored request ("bury my old bones under a night sky") — the tone delivery vehicle (GDD §2.2). Small, completable with existing verbs, one gentle joke each.
- **Ascension ceremony:** task done → player-initiated send-off at a chosen spot. Ceremony beat: music sting (placeholder), light column, the flock gathers to watch. 20–30 seconds, skippable never needed because it's *good*.
- **Legacy system:** each ascension grants a **permanent** reward — a legacy token/shrine in the world + a journal Legacy Gallery entry carrying the spirit's *player-given name*. Legacies stack visibly; the farm remembers everyone who left (GDD §3.7).
- **Essence v0:** happy residents shed essence pickups (GDD §3.5) — collected, counted, *not yet spendable* (economy is slice 09; essence exists now so fulfilment has a passive payoff and tuning data accrues).

## Content

- One final task per existing species (4 tasks) — first real writing in the tone register (GDD §4.5): dark-funny, never mean.
- Ceremony VFX/SFX placeholder, Legacy Gallery journal tab, 4 legacy reward definitions (make them *want-able*: e.g. a shrine that slightly widens a lure radius — mechanical, not just cosmetic).

## Out of scope

Weaving (the second door — deliberately absent so Ascension is evaluated pure), competitions, villains, money.

## Depends on

03 (residents, Spirit stat, journal), 02 (Fulfil gates as data).

## Definition of Done — Vertical Slice acceptance

- [ ] **The full loop plays in 20–30 minutes:** empty field → shape land → silhouette → visitor → named resident → fulfilled → final task → ascension → legacy on the shelf
- [ ] A tester describes the ascension as some flavour of "sad but nice" — not "why would I ever do that"
- [ ] The legacy reward is compelling enough that testers ascend a *second* spirit unprompted
- [ ] Fulfilled-glow reads at a glance across the field
- [ ] Final tasks raise a smile; tone check against GDD §4.5 (is it a joke? is it kind underneath?)
- [ ] Whole slice survives save/quit/resume at any point, mid-ceremony included
- [ ] ✂ **Hand-off ready:** a stranger can play this build with zero verbal instructions (a one-screen "controls" card allowed)

## Risks

- **The hoarding failure mode.** If goodbye isn't compensated enough, players keep pets and the churn engine (GDD §3.8) never turns. Tune legacy value up until ascending is *tempting*, then stop.
- Ceremony scope creep — it's 20 seconds of theatre, not a cutscene pipeline.
- This milestone is the go/no-go for the whole design: schedule honest outside playtests (friends who'll be rude).
