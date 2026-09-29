# 01 — Foundations

**Question this slice answers:** *Is the shepherd fun to simply walk around as, for five minutes, in an empty field?*
**GDD:** §1.2 (embodied shepherd), §2.5 (time, quit-anytime), §2.6 (verbs), §4.1 (3/4 camera)

Everything in the game is experienced through this body and camera. If walking, looking, and reaching for things doesn't feel good here, no spirit or competition will save it.

---

## Builds (systems)

- **Project skeleton:** Unity 6 / 2D URP scene structure, input via Input System (keyboard+mouse AND gamepad from day one — genre is gamepad-heavy), folder/assembly conventions.
- **Shepherd controller:** 3/4-view movement (8-way or analog), acceleration/deceleration tuned for *pleasant*, not twitchy. Placeholder rigged puppet (Unity 2D rig — even a capsule with a bobbing head; the rig pipeline itself is what's being proven).
- **Camera:** follow-cam with soft leading, slight zoom control. 3/4 angle locked (GDD §4.1). No screen-edge hard snaps.
- **Interaction system:** proximity highlight + single contextual interact. This one system will later serve feed/soothe/herd/talk — build it generic (target + verb resolved from what's under focus).
- **Time system:** 20-minute day/night clock (GDD §2.5), fast-forward toggle, time-of-day readable in UI and world tint. Time only advances in play.
- **Save/pause:** save anywhere, pause anywhere, resume exact state. This is a *day-one* system, not a later one — every subsequent slice's data must ride it (design serialization conventions now).
- **Debug console/cheats:** time skip, teleport, spawn — you will need these in every slice after.

## Content

- One greybox Prairie test field (bigger than one screen, so the camera and walk distances get honestly tested).
- Placeholder shepherd rig with idle/walk animations.

## Out of scope

Spirits, plants, terraforming, any UI beyond clock + interact prompt, audio (a single footstep loop at most).

## Depends on

Nothing. This is the root.

## Definition of Done

- [ ] Walk around the field with gamepad and KB+M; both feel deliberate, not floaty
- [ ] Camera never jitters, never loses the shepherd, and the 3/4 perspective reads on slopes/edges
- [ ] Day/night visibly cycles in 20 min; fast-forward works; clock UI readable
- [ ] Save mid-walk, kill the app, resume — position/time restored
- [ ] Interact prompt appears/disappears correctly on a test object
- [ ] The rig pipeline is proven: swap the shepherd's placeholder skin for a second placeholder without touching animation

## Risks

- **Feel is subjective and cheap to fix now, expensive later.** Iterate movement values until it's *pleasant to do nothing in particular* — that's the cosy-genre bar.
- Serialization laziness here (e.g., scene-state not data-state) compounds through every slice. Save = data model from the start.
