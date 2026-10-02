using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.UI
{
    /// <summary>One QA check: a concrete thing to try and the result it must produce.</summary>
    public sealed class FeedbackCheck
    {
        /// <summary>Stable id, never reused or renamed once shipped ("TOOL.weight") - saved drafts key on it.</summary>
        public string Id;
        public string Section;
        public string Title;
        /// <summary>Short concrete steps, with debug-console shortcuts where useful.</summary>
        public string Steps;
        /// <summary>Specific, testable expected behavior (or the dev-judgment question for feel checks).</summary>
        public string Expected;
        /// <summary>Feature not built yet: the row starts as N/A and is skipped in summaries.</summary>
        public bool NotBuilt;
        /// <summary>Feature lands in the current build wave: if it is missing in your build, mark N/A.</summary>
        public bool Wave2;
    }

    /// <summary>
    /// The development QA checklist (static data, read by FeedbackUI). To add a
    /// check, append one C(...) / W(...) / N(...) line to the right section
    /// method (or add a new Sec(...) block + a call in Ensure). Rules:
    ///  - ids are permanent; add new ones, never recycle an old id;
    ///  - W(...) = wave-2 feature that may not be in your build yet (mark N/A);
    ///    (all wave-2 rows were promoted to C(...) on 2026-10-01; the helper stays for the next wave);
    ///  - N(...) = known NOT built; starts as N/A - flip to C(...) when it lands;
    ///  - ASCII only (legacy font), and no double quotes inside strings.
    /// Console shortcuts referenced here are the real console commands (type 'help'):
    /// time, day, season, rain, ff, tp, save, load, spawn, spirits, inv, coins, grow,
    /// weed, weeds, villain, blessing, taskdone, resident, ready, weave, gentle, repo,
    /// parcel, guide, skipguide, bleep, audio, tooltier, sit, naming, mood, silhouette,
    /// flood, compost, leaving, seeds, quality, stats, traits, train, trainnow, gym, bait,
    /// reroll, weaveforce, weavenow, recipes, discover, forget, rumor, banner, banners,
    /// blend, inherit, road, dark, toll, ambush, gear, mount, affinity, spawntable, biome,
    /// ground, garden. Ctrl+M is the audio panic key.
    /// Handy places: Hearth Field centre is (0,0); the west gate is (-15,0); Reedmire
    /// is around (-37,0); the town plaza is east of x=15 (try 'tp 24 3').
    /// Parcel indices: 0 Hearth, 1 West, 2 North, 3 East, 4 South, 5 NW, 6 NE, 7 SW,
    /// 8 SE, 9 West Road Rights, 10-13 Reedmire Hollow/Bank/Shallows/Deep.
    /// </summary>
    public static class FeedbackChecklist
    {
        private static List<FeedbackCheck> _all;
        private static List<string> _sections;
        private static HashSet<string> _ids;
        private static string _sec;

        /// <summary>Every check, in authored order.</summary>
        public static IReadOnlyList<FeedbackCheck> All
        {
            get { Ensure(); return _all; }
        }

        /// <summary>Section names in authored order.</summary>
        public static IReadOnlyList<string> Sections
        {
            get { Ensure(); return _sections; }
        }

        private static void Ensure()
        {
            if (_all != null) return;
            _all = new List<FeedbackCheck>(320);
            _sections = new List<string>(24);
            _ids = new HashSet<string>();

            Movement();
            Tools();
            Land();
            Calendar();
            Biomes();
            Lure();
            Charm();
            Naming();
            Styx();
            Weaving();
            Friction();
            Frontier();
            Economy();
            Onboarding();
            Saving();
            Interface();
            Audio();
            Performance();
            NotBuilt();
        }

        // ------------------------------------------------------------ helpers

        private static void Sec(string name)
        {
            _sec = name;
            if (!_sections.Contains(name)) _sections.Add(name);
        }

        private static void Add(string id, string title, string steps, string expected, bool notBuilt, bool wave2)
        {
            if (!_ids.Add(id))
            {
                Debug.LogWarning("[FeedbackChecklist] duplicate check id skipped: " + id);
                return;
            }
            _all.Add(new FeedbackCheck
            {
                Id = id, Section = _sec, Title = title, Steps = steps, Expected = expected,
                NotBuilt = notBuilt, Wave2 = wave2
            });
        }

        /// <summary>A normal check.</summary>
        private static void C(string id, string title, string steps, string expected) =>
            Add(id, title, steps, expected, false, false);

        /// <summary>A check for a wave-2 feature: N/A it if your build does not have it.</summary>
        private static void W(string id, string title, string steps, string expected) =>
            Add(id, title, steps, expected, false, true);

        /// <summary>A known-unbuilt feature: starts as N/A.</summary>
        private static void N(string id, string title, string steps, string expected) =>
            Add(id, title, steps, expected, true, false);

        // ----------------------------------------------------------- sections

        private static void Movement()
        {
            Sec("Movement & camera");

            C("MOVE.walk", "Walking, diagonals, stopping",
                "Walk with WASD, then with the arrow keys. Walk a full circle including diagonals. Release keys abruptly.",
                "8-way movement responds identically on WASD and arrows. Diagonal speed equals straight speed (no faster diagonals). The shepherd stops on key release with no drift or sliding.");

            C("MOVE.sprint", "Sprint",
                "Hold Left Shift while walking, then release. Watch the feet. Sprint past an idle spirit.",
                "Speed visibly rises (walk cap about 4.5, sprint about 7.2 units/sec), dust puffs appear at the feet while sprinting, speed returns to walk on release.");

            C("MOVE.collision", "Collision and fences",
                "Walk into: the farm fence, a deep-water tile (the shallow rim is wadeable), a home, a headstone, the closed west gate bar (before road rights). Slide along corners.",
                "Everything blocks the shepherd with no jitter or tunnelling. The fenced cluster can only be left through the east town gate (or the west gate once road rights are owned). Corners do not snag.");

            C("MOVE.facing", "Facing and target cell",
                "Face left, right, up and down with a tool selected. Move diagonally and stop.",
                "The target reticle sits on the tile in front on the dominant axis. Sprite flips for left/right; up and down use their own poses (see NB.4dir-art) with no flicker when you stop on a diagonal.");

            C("NB.4dir-art", "Distinct four-direction sprites",
                "Face all four directions with WASD and compare body and head. Swing a tool facing up, down, left and right. Stop on a diagonal.",
                "Up, down and side are separate authored placeholder sprites (shepherd_body and shepherd_head in _up, _down and side variants; the alt skin has its own set); left is the side sprite flipped. The pose follows the dominant axis of the facing direction, holds the previous pose when you stop on a diagonal with no flicker, and tool swings read in all four directions.");

            C("MOVE.bob-feel", "Walk bob (feel)",
                "Walk and sprint for 30 seconds each, watching the body bob.",
                "DEV JUDGMENT: the bob reads as a calm scamper, not frantic (frequency was reduced to 0.55) and the sprint is distinguishable from walking. Use the note box for what to change.");

            C("MOVE.camera-follow", "Camera follow and zoom",
                "Walk and stop repeatedly. Scroll the wheel in and out to both limits while standing and while walking.",
                "Camera eases toward the shepherd with a small lead in the walking direction, no snapping or jitter. Zoom is smooth and stops at the closest (about 3.5) and furthest (about 9) limits.");

            C("MOVE.camera-clamp", "Camera clamp (soft)",
                "On a fresh save push to each edge of the Hearth Field, then walk east through the town gate. Then 'parcel 1' and 'parcel 9' and walk to the new edges.",
                "Camera stops about 4 units past the owned land, easing in (no hard wall stop). The town plaza stays viewable. Opening a parcel grows the clamp immediately with no pop; road rights reveal the Reedmire enclosure.");

            C("MOVE.focus-interact", "Interaction focus",
                "Stand between two interactables (a plant and a spirit, or two stalls), face one then the other, press E.",
                "Focus highlight and the bottom prompt follow the facing direction (not only the nearest). E opens the highlighted target's menu (or acts directly on plain interactables like plants). The prompt disappears when nothing is in range.");

            C("MOVE.facing-weighted", "Facing-weighted focus (numbers)",
                "Stand between two interactables (a plant and a spirit, or a weed and a headstone), one about 1.3 units ahead and one about 1.0 unit behind. Then turn around. Then stand still right at the point where focus would swap.",
                "Interact range is 1.4 units. The target you face wins even when it is a bit farther (distance squared is scaled from x1.8 directly behind to x0.5 directly ahead), turning around swaps the highlight, and the current focus keeps a x0.8 stickiness so the highlight does not flicker at the seam. Riding the mount: E dismounts instead of interacting.");

            C("NB.interact-menu-feel", "E opens the context menu (and plain interactables stay direct)",
                "Walk up to a spirit and press E. Close it (Esc). Press E at a stall (Vendor, Blacksmith, Registrar), a headstone, a weed, a ripe plant and the Competition Board. Also press E beside a placed spirit home, a training building and a Watchlight (they have no walk-up action of their own, but E now focuses them too). Face an empty tilled cell with nothing else in range and press E.",
                "On a spirit E opens its menu next to it (title = its name; rows such as Soothe / Come along / Inspect, plus Feed or Offer when they apply) and the bottom prompt read '[E] Options' beforehand. A target with exactly one real choice runs it directly with no menu (weed '[E] Pull', stall '[E] Trade', Registrar '[E] Land deeds', Holding Office '[E] Enter', Loom '[E] Weave', Board '[E] Read the notices' when no spirit is following). Homes, training buildings and Watchlights get the gold outline and '[E] Options' like everything else and E always opens their menu, even when Destroy is their only real row (it never destroys on a double-tap: 'Destroy' then 'Really destroy?' inside the menu). Rows in parentheses are info rows and are skipped by navigation. A ripe plant harvests directly (plain interactable, prompt unchanged). Empty tilled soil still opens the seed picker. While riding, E dismounts and opens nothing. The same E press that opens a menu never also confirms its first row.");

            C("NB.interact-menu-nav", "Menu keyboard / gamepad navigation and modal lock",
                "Open a spirit's menu with E. Press S / Down then W / Up (also d-pad and left stick). Confirm with E, then reopen and confirm with Enter, then reopen and confirm with gamepad South. Reopen and close with Esc, then Backspace, then right click, then gamepad East. Reopen and use the mouse (hover, click a row, click empty ground). While it is open try WASD, Z, V, the toolbelt and a left click on the ground.",
                "The highlighted row (gold) moves one row per press, wraps at the ends and never lands on a (parenthesised) info row; hovering with the mouse moves it too. Each confirm runs exactly one action and closes the menu (two-step rows like 'Destroy' re-label and stay open). Esc closes the menu and does NOT pause the game; Esc with no menu still pauses. While the menu is open the shepherd cannot walk, swing a tool, sit or ride and E does not re-fire; closing it gives control back immediately. An action that opens a panel (Inspect, a shop) leaves that panel in control. Clicking empty ground closes it without swinging a tool. Opening a menu mid-competition never happens (E acts directly).");

            C("NB.focus-outline", "Focus outline highlight",
                "Walk between a spirit, a weed, a plant, a headstone and a stall; watch the focused target. Walk away so nothing is focused. Sit and watch a focused spirit bob, flip and fade.",
                "The focused target gets a bright gold outline hugging its silhouette (about 1 pixel thick at default zoom) on top of the existing small scale-up; it follows sprite changes, flips, bobbing and fades, moves when the target moves, and disappears the moment focus leaves. Only one target is outlined at a time and nothing stays outlined after walking away.");
        }

        private static void Tools()
        {
            Sec("Tools, tiers, XP");

            C("TOOL.belt", "Tool cycling and toolbelt",
                "Press Tab repeatedly. Press T and pick a tool with the number keys and with the mouse. Select Hands and the Hammer and press Space.",
                "Only unlocked tools appear (fresh game: Hands + Hoe). The tool HUD matches the selection. Cycling wraps. Hands and Hammer act instantly (no wind-up); the Hammer and B open the build menu. Toolbelt clicks do not leak into the world.");

            C("TOOL.weight", "Tool weight and rhythm (feel)",
                "Hoe 10 tiles, then use the Water Pail and the Shovel. Sprint between actions.",
                "Each cell verb is a committed action of about 0.5s: wind-up, effect lands about 60% through with a puff and bleep, the swing sprite arcs. DEV JUDGMENT: weighty but not tedious. Note which tool feels worst.");

            C("TOOL.move-lock", "Movement lock during swings",
                "Start a swing and hold WASD. Press Esc mid-swing and resume. Open J mid-swing.",
                "Movement is locked only for the length of the action and resumes right after. No permanent lock after a pause or modal interrupted a swing.");

            C("TOOL.hold-chain", "Held-button chaining",
                "Hold Space (or left mouse) while walking along a row of scrub, then release mid-swing.",
                "Actions chain with a short breath (about 0.12s), each landing on the cell in front. Releasing stops after the current action. No cell is double-applied and no action is skipped.");

            C("TOOL.tier-speed", "Tier speed (Hoe, Pail, Shovel)",
                "Time 10 tiles at tier 1. Then 'tooltier Hoe 2', time again, 'tooltier Hoe 3', time again. Repeat for 'tooltier Water Pail 2/3' and 'tooltier Shovel 2/3'.",
                "Tier 2 takes about 0.65x the action time, tier 3 about 0.5x. Each step is clearly faster by eye. HUD or Blacksmith shows the new tier.");

            C("TOOL.tier-aoe", "Tier 3 area of effect",
                "'tooltier Hoe 3', face a block of scrub and till. 'tooltier Water Pail 3' and water. 'tooltier Shovel 3' and dig. Try it next to a locked field edge.",
                "Hoe and Pail tier 3 affect a 3-cell row across the facing axis. The Shovel stays single-cell (speed only). Locked or invalid cells in the row are skipped without errors.");

            C("TOOL.xp-drip", "XP per verb",
                "Note the Lv bar. Till 1 tile, water 1, dig 1, sow grass 1, plant 1, harvest 1, build a home, feed a spirit, soothe a spirit.",
                "Bar grows by: till 2, water 1, dig 2, sow 1, plant 2, harvest 3, build 8, feed 3, soothe 3 (herd 2). At level 1 the bar needs 100 XP, so one till is about 2% of the bar.");

            C("TOOL.level-curve", "Level curve and fanfare",
                "Earn XP to level 2 and 3 (repeat tilling, or harvest after 'grow').",
                "At 100 total XP: gold floating 'Level 2!' plus a chord and the bar resets. Level N needs 100*N XP more. 'Lv N' HUD text updates. Blacksmith tier gates (level 3 and 6) follow the level.");

            C("TOOL.level-persist", "Tiers and XP survive reload",
                "Set tiers with the console, earn some XP, 'save', stop Play, run again.",
                "Tiers, total XP and level are exact after reload and no level-up fanfare fires on restore.");
        }

        private static void Land()
        {
            Sec("Terrain, planting, weeds");

            C("LAND.till", "Tilling",
                "Select the Hoe, face scrub or grass, use it. Check the Lv bar.",
                "The faced cell becomes Dirt at the contact beat with a dirt puff and bleep, XP +2. Edges between grass and dirt blend (no hard seams).");

            C("LAND.locked-refuse", "Locked parcels refuse tools",
                "On a fresh save 'tp -10 0' (West Field, locked). Try the Hoe, a seed, the Shovel and placing a home.",
                "Every tool and placement is refused with clear feedback (denied bleep or text). No terrain change, no XP, no console error.");

            C("LAND.water-dig", "Watering and digging ponds",
                "Pail on tilled Dirt and on grass. Shovel a 3x3 pond. Walk into the pond.",
                "The Pail wets only tilled soil (darker tint). The Shovel turns cells to Water; deep cells are impassable and the ring next to land gets a lighter shallow rim you can wade (see NB.shallow-wading). Pond edges blend with no seams or holes.");

            C("LAND.seed-picker", "Seed picker and grass sowing",
                "Stand on Dirt holding grass seeds and press Use. Try with 0 seeds. Select with number keys and mouse.",
                "A contextual picker lists only seeds you own with counts. Sowing grass consumes 1 seed_grass and turns the cell to Grass. With 0 seeds no picker (or a clear message), never an empty broken picker.");

            C("LAND.plant-grow", "Planting and growth stages",
                "Plant palewheat, gravebloom and murkberry on Dirt. Use 'ff' (x8) and watch the stages.",
                "Three stages (sprout, mid, ripe). Effective hours per stage: palewheat 6, gravebloom 8, murkberry 10. Only ripe plants can be harvested. Planting on non-Dirt is refused with feedback.");

            C("LAND.water-boost", "Watered vs dry growth and wilt",
                "Plant two palewheat; water one only. 'ff' for about 45 real seconds (6 game hours).",
                "Watered soil grows at full speed, dry soil at half. An unwatered, unfinished crop visibly droops and desaturates (wilt) but never dies. Watering restores it.");

            C("LAND.harvest", "Harvest, regrow, star badges",
                "'grow', then press E on ripe plants of each species.",
                "Produce added (palewheat 2 wheat, gravebloom 1 bloom, murkberry 3 berry), XP +3. One-shot crops are removed; murkberry and reed drop back to the mid stage and regrow. Fine or Gleaming crops show star text.");

            C("LAND.quality", "Crop quality tiers",
                "'seeds 5', plant three palewheat: keep one always watered, water one about half the time, never water one. 'grow', harvest each, then 'inv'. (To skip the wait: 'quality fine' or 'quality gleaming' pins every crop's tier; 'quality normal' unpins.) Open the vendor with the harvest.",
                "Quality score = watered share of grow time, plus 0.15 with compost: Fine at 0.55 or more, Gleaming at 0.90 or more, else Normal. Harvest floats a star badge (* Fine, ** Gleaming). 'inv' lists wheat, wheat_fine, wheat_gleaming separately. The vendor pays x1.5 for Fine and x2.5 for Gleaming (berry 4 -> 6 / 10, wheat 3 -> 4 / 8). Spirits fed a Fine or Gleaming crop gain 1.5x / 2x contentment. The console prints 'Pinned N crop(s) to <tier> (water plants carry no quality)'.");

            C("LAND.regrow", "Regrow cycles reset quality",
                "'seeds 5', plant murkberry and reed (reed on a pond rim). 'grow', harvest each, keep the soil watered, 'ff' about 20 real seconds, harvest again.",
                "Harvest drops a regrowing plant back to the mid stage instead of removing it (murkberry gives 3 berries per harvest, reed 2) and starts a fresh grow cycle: watered share and any compost are reset, so the second harvest is graded on that cycle only. Palewheat, gravebloom and glowcap lily are one-shot and vanish when harvested.");

            C("LAND.water-plants", "Reed and glowcap lily",
                "'seeds 3' (or buy Reed 3 obols and Glowcap Lily 5). Dig a pond. Plant reed on the pond's shallow rim and a lily on deep water. Try planting both on land and on bare dirt.",
                "Water seeds only offer a planting prompt while you hold them. Reed plants only on shallow-rim cells, the lily on any water cell, land and dirt are refused with feedback. Water crops never wilt (always wet) and carry no quality stars. Reed regrows (5 hours per stage, 2 reed per harvest); the lily is one-shot (9 hours per stage, 1 glowcap). They sell for reed 3 and glowcap 7 obols.");

            C("LAND.compost", "Compost loop and fiber",
                "'compost 5' and 'leaving'. Walk over the leaving. Open the seed picker on empty tilled dirt, pick 'Compost soil', then plant there. Also press E on a growing (not ripe) crop. Then 'inv', pull weeds until you hold 3 fiber, use up your compost, and compost again.",
                "'leaving' drops a leaving beside you; walking over it gives +1 compost (max 6 uncollected leavings in the world). Naturally every ~40 scaled seconds each happy resident (Spirit 70 or more) has a 25% chance to drop one. The picker row reads 'Compost soil (x N)' with 'faster growth, finer crops'; an enriched cell says so and the next crop sown there carries it. A crop prompts 'Compost <name>' once per grow cycle and only before it is ripe. A composted crop grows 25% faster and gains +0.15 quality score; compost is spent with the harvest. With no compost held, 3 fiber stand in for 1 compost (the picker count includes fiber / 3). Enriched cells persist across save and load; uncollected leavings do not.");

            C("LAND.grass-spread", "Bounded grass spread",
                "Sow one grass cell inside scrub (not adjacent to other grass). 'ff' for about 2 real minutes. Also sow near a tilled cell and a planted cell.",
                "Roughly every 3 to 5 game hours one wild scrub cell that touches grass is converted to grass, only within 2 cells of a cell YOU sowed by hand. Spread cells are never anchors themselves, so the patch rounds itself out (at most a 5x5 area around each sown cell) and never marches across the map. Dirt, watered soil, planted cells and non-scrub ground are never taken.");

            C("LAND.sand-desert", "Sand loads and the Desert biome",
                "'coins 100'. Buy Sand at the vendor (8 obols for 4 loads). Face open scrub or grass and open the seed picker (Interact). Spread sand until about 40% of the base is sand. Try it on a tilled cell, a sand cell and a home. 'biome' to read the census.",
                "The picker row reads 'Sand load (x N)' with 'turns this cell to arid sand'; each use costs one load and floats 'Spread sand'. Sand is only offered on open scrub/grass (never on a cell with a home or plant); tilled dirt offers seeds, and a sand cell offers nothing (till it first). Biome check order is Desert (sand 40% or more of the base's usable cells, score = sand %), then Swamp, then Grassland, else Barren. 'biome' lists each base like 'Base 0: Desert score N'.");

            C("NB.shallow-wading", "Shallow-water wading",
                "Dig a 3x3 pond with the Shovel (centre cell is deep, the ring is shallow rim). Walk from land onto the pale rim and on toward the centre. 'wade' reads the feet cell. Sprint through the rim. Then dig a cell beside you while standing on a one-cell-wide rim spit.",
                "The pale rim is walkable and the shepherd visibly slows to about 60% speed (eased in and out, no snap), sprint included; riding the mount slows to about 80%. Each step plays the water slosh and kicks up a small blue splash puff. The deep centre still blocks walking with no jitter. 'wade' reports 'shallow rim; wading=True' on the rim and 'DEEP water' is never reachable. If the cell under you turns deep (its last land neighbour was dug) you are nudged to the nearest walkable cell with a splash, never stuck. Filling the pond back in with the Hoe turns rim cells to land and speed returns to normal. Reeds can still be planted on the rim from the bank.");

            C("NB.plant-sway", "Plant sway and bend",
                "Plant a few palewheat and a reed or two in a row and 'grow'. Stand back and watch for 20 seconds. 'gust 1' then 'gust 0' then 'gust auto'. Walk slowly through the row and stop in the middle, then walk away. Park a spirit beside a plant. 'rain on' and watch again.",
                "Every plant sways gently around its base, each out of step with its neighbours; ripe plants sway more than sprouts and lilies barely move. 'gust 1' sways wider and leans the tops downwind, 'gust 0' is a faint idle. Walking through plants leans each one AWAY from you (left of you leans left, and so on) with a slight squash, then springs back with a small overshoot once you leave; standing still keeps them leaned. Spirits brush plants more lightly. Rain blows harder. Wilting crops keep their sag with the sway added on top. Plants far off screen are still (no cost) and there is no frame-rate hit with 100+ plants (PERF.fps). Grass tiles do not bend (the leaf-puff trail is the grass feedback).");

            C("NB.rich-mud", "Rich mud: buy, spread, plant, swamp",
                "'coins 100', go to the Mire Peddler at the swamp gate (or 'mudload 4'). Buy Rich Mud (4 loads, 10 obols). Face open scrub, grass or tilled dirt and press Interact: pick 'Mud load'. Plant palewheat on the mud cell without watering. Compost the empty mud ('compost 5'). Walk across mud. Hoe a mud cell. Spread mud until water plus mud is 12% or more of the base with 2 plants rooted in mud or beside water, then 'biome'. 'save', reload.",
                "The Peddler lists 'Rich Mud (4 loads) - 10 obols' and the pouch shows mud_load. The picker row reads 'Mud load (x N)' with 'turns this cell to rich swamp mud: always wet'; each use costs one load, floats 'Spread rich mud' and paints a dark speckled mud tile that blends into its neighbours (never on a cell with a home or plant, never on sand or water). Dirt crops can be sown on mud, grow at FULL speed with no watering and never wilt, and harvest as Gleaming (always-wet score). Compost works on mud like dirt. Walking on mud squelches and kicks brown puffs. The Hoe on mud turns it back to Dirt (loads are not refunded). Mud counts toward Swamp: the census adds mud % to water %, and a plant rooted in mud counts as a wet plant, so 'biome' can read Swamp with little water. Mud survives save and load, and old saves load unchanged.");

            C("LAND.weeds", "Weed lifecycle and pulling",
                "'weed' (spawns near you) and 'weeds' (list). Stay 6+ units away for a minute. Pull one with E or the Pull action.",
                "Weeds sprout only more than 5 units from the shepherd and outside Watchlight wards, max 4 alive. Pulling takes 3 quick tugs, gives +1 fiber with 'ripped it out! +1 fiber' floating text. Weeds mature after 48 game hours and sour nearby residents every 10 seconds (young -2 Spirit within 3 units, mature -3 within 4) but never destroy terrain, plants, homes or items. Fiber sells for 1 obol or stands in for compost (3 per 1).");

            C("LAND.watchlight", "Watchlight wards",
                "Buy a Watchlight (30 obols), place it, 'weed' within and outside its range, then Destroy it (click it, or stand beside it and press E: '[E] Options', then Destroy).",
                "No weed sprouts within 6 units of a ward; the select menu reads '(wards 6 paces)'. Destroy needs a second 'Really destroy?' click. Placing on water, a plant, a home or a locked cell is refused and refunds 30 obols.");
        }

        private static void Calendar()
        {
            Sec("Calendar, seasons, rain");

            C("CAL.clock", "Clock, day/night light, fast-forward",
                "'time 6', 'time 12', 'time 20', 'time 23'. Leave running one minute. Press F (or 'ff') for fast-forward and again to stop.",
                "HUD time matches the set time. One game day is 20 real minutes. Dawn and dusk blend smoothly, night is dark but readable, no flicker at 6:00 or 18:00. The day counter advances exactly once at midnight. Fast-forward runs x8 and toggles off.");

            C("CAL.date-hud", "Date HUD and season cycle",
                "Read the date button under the clock. 'season' to list. 'day 16', 'day 31', 'day 46', 'day 61'.",
                "Shows 'Day N - Season'. Days 1-15 The Hush, 16-30 The Weep, 31-45 The Smolder, 46-60 The Long Dim, wrapping at 61. 'season' lists four seasons with rain percentages (10, 55, 5, 30).");

            C("CAL.calendar-ui", "Calendar page (C)",
                "Press C, then click the date button. Change the day with the console while it is open. Close it.",
                "Page shows season name and flavor, a 15-day grid with today highlighted, and the upcoming events list. It updates in place without flashing. Gameplay is blocked while open and restored on close. C typed in a text box never opens it.");

            C("CAL.season-tint", "Season palette and ambience",
                "'season 0' to 'season 3' at 'time 12' and 'time 22'.",
                "Each season shifts the light tint and the music keying noticeably. The scene stays readable in all four at day and night.");

            C("CAL.rain", "Rain visuals, sound, auto-watering",
                "'rain on', watch one minute with unwatered crops. 'rain off'.",
                "Rain streaks ride the camera and a soft hiss loops. Tilled soil is auto-watered about every 5s. 'rain off' stops visuals and sound within about 2s with no stuck particles.");

            C("CAL.rain-roll", "Rain frequency by season",
                "Step 'day N' through one full Weep (16-30) and one Smolder (31-45) noting rain days. Then cross dawn with 'time 23.9'.",
                "Rain is rolled at each dawn and lasts all day. The Weep rains on roughly half the days, the Smolder almost never (Hush 10%, Long Dim 30%). A rain day never toggles on and off mid-day.");

            C("CAL.pond-flood", "Pond flood in rain",
                "Dig a pond with tilled soil touching it. 'rain on', wait about 1 game hour (about 50 real seconds, or 'ff'), then 'rain off' and wait about 3 game hours. Also try 'flood on', 'flood off' and plain 'flood'. 'save' and 'load' mid-flood.",
                "About 1 game hour into rain the ponds swell: scrub, grass and sand within 1 cell of water draw as shallows, and tilled soil touching a pond stays soaked (swept every 5 seconds). About 3 game hours after the rain stops the water recedes. Nothing is destroyed or blocked, the biome census ignores the flood. 'flood on' prints 'Flood ON (recedes ~3 game-hours after rain stops)'. The flooded state persists across reload.");

            C("CAL.merchant", "Traveling merchant days",
                "'day 5', 'day 6', 'day 10', 'day 15'; 'tp 18 -2' on the merchant days. 'save' then 'load' on day 10.",
                "A stall appears inside the town gate near (17.5, -2.5) only on day-of-season 5, 10 and 15 with an announcement, and packs up at the next dawn. Presence is still correct after save and load.");
        }

        private static void Biomes()
        {
            Sec("Biomes, parcels, Reedmire");

            C("BIOME.score", "Biome classification and F1 overlay",
                "Press F1. Sow grass to 40% of the Hearth Field. Dig water to 12% and plant 2+ reeds/crops beside it. Open 'parcel 9' and 'parcel 10' and change Reedmire only.",
                "Overlay lists each base's biome and 0-100 score refreshing about 4x/sec without flicker. Checked in order: Desert at 40% sand, Swamp at 12% water with 2+ plants beside it, Grassland at 40% grass, otherwise Barren. Home and Reedmire score independently, locked cells never count. F1 is ignored while typing or in a modal.");

            C("NB.biome-affinity-materials", "Biome affinity table (5 steps)",
                "'affinity' for every species, then 'affinity bansheep' and 'affinity bogwick'. Read the last line too.",
                "Each line reads like 'bansheep: Grassland=love Swamp=dislike Desert=hard no Barren=neutral | native Grassland'. Authored (Grass / Swamp / Desert): Mausoleum like / dislike / dislike; Bansheep love / dislike / HARD NO; Wrabbit like / like / dislike; Phantomoth neutral / like / dislike; Wailpertinger neutral / love / dislike; Mothmaus neutral / like / dislike; Bogwick neutral / love / HARD NO; Reedhen like / love / dislike; Sloughling dislike / love / dislike; Scorpse neutral / dislike / LOVE. The last line lists road territories 'Hearth Verge=Grassland, Dust Reach=Desert, Mire Fringe=Swamp'. Only Bansheep and Bogwick have a Hard No (Desert).");

            C("BIOME.spawn-weights", "Affinity-weighted silhouette spawning",
                "'parcel 1', 'parcel 9', 'parcel 10' so both bases have land. 'spawntable' and read it. Then 'biome 0 desert' and 'spawntable 0'. Then 'biome clear'.",
                "Per base it prints 'Base N: <Biome>' then one line per species like '  bansheep: love w6.0 -> N% of its silhouettes'. Weights are Love 6 / Like 3 / Neutral 1 / Dislike 0.2 / Hard No 0, and a species' chance at a base is its weight there over its weight across all owned bases. A Hard No base reads '-> never appears here'; a base with no owned land reads 'no owned land, nothing spawns here'; a closed gate adds '[Appear gate closed]'. While a base is still Barren the old homeland rule applies (swamp-native species favour Reedmire, others the home base, 4x less likely at the wrong region). With the pin, Bansheep and Bogwick vanish from the desert base's table.");

            C("BIOME.hard-no-refuse", "Hard No: visitors and silhouettes drift away",
                "'biome 0 desert' (pins the home base to Desert). 'spawn bansheep' to get a visitor, then watch it. Also 'silhouette bansheep'. Then 'biome 0 clear'.",
                "A Bansheep visitor or silhouette on Hard No ground never visits or stays: within about half a second it drifts toward the border while fading (about 2.6s), floats '(it drifts off - wrong ground)' (visitors, you within 14 units) and is gone. No feeding is involved (visitors are never fed to convert) and the manager may later respawn a silhouette on kinder ground (never on a Hard No base). A visitor's wander points avoid Hard No ground. A homeless resident skips free homes built on its Hard No ground (placing the home is still allowed). Unpinning restores normal behaviour.");

            C("BIOME.discomfort", "Wrong ground is discomfort, not eviction",
                "'resident bansheep Moss', 'mood 90 Moss', stay within 14 units, 'biome 0 desert', 'ff' about 2 real minutes, then 'ground' and 'spirits'. Then 'biome 0 clear' and feed it.",
                "Within a few seconds Moss shows '(this ground feels wrong)' and a want bubble, repeating every 25 to 50 seconds while you are near. Spirit drifts down 3 per game hour and STOPS at 35: it never reaches the runaway threshold (15) from this alone and is never evicted. Gains (feed, soothe, rest) are halved on Hard No ground (x0.50). 'ground' prints a line like 'Resident Moss: ground base 0 Desert = hard no, mood x0.50, rain ...'.");

            C("BIOME.ground-bonus", "Affinity scales mood gains",
                "'resident bansheep A' on a Grassland base, then 'ground'. Feed or soothe it and watch the gain. Compare with 'resident mausoleum B' (Like) and a Dislike species on that ground ('biome 0 swamp' pin).",
                "'ground' shows the multiplier on mood GAINS: Love x1.5, Like x1.25, Neutral x1, Dislike x0.5, Hard No x0.5. A Love base adds a slow trickle (+1.2 Spirit per game hour, never pushing past 85). The inspector shows 'Ground: Grassland - at home here' (Love), 'comfortable here' (Like), 'fine here', 'uneasy here' (Dislike).");

            C("BIOME.rain-enjoy", "Rain: swamp-lovers enjoy it",
                "'resident bogwick A' and 'resident mausoleum B'. 'rain on', watch both for about 40 seconds, then 'ground'.",
                "Species that Like or Love Swamp and do not like Desert (Bogwick, Reedhen, Sloughling, Wrabbit, Phantomoth, Wailpertinger, Mothmaus) hop and chirp every 9 to 18 seconds for a hair of Spirit (+0.25, capped at 80); 'ground' shows 'rain Enjoys (raining)'. Mausoleum and Bansheep show 'Indifferent'. No species shelters yet (no desert-native species exists), which is the N row at the bottom.");

            C("BIOME.journal-line", "Journal Biomes line",
                "'resident bogwick A', 'resident bansheep B', 'resident wailpertinger C' (a species must be Visited or better). Press J, open each species page on the Species tab, then a resident's page on the Residents tab.",
                "A species only Seen (silhouette) shows no Biomes line yet. Each Visited-or-better species page has a line 'Biomes: Loves Swamp, Hates Desert' (Bogwick), 'Biomes: Loves Grassland, Dislikes Swamp, Hates Desert' (Bansheep), 'Biomes: Loves Swamp, Dislikes Desert' (Wailpertinger); neutral rows are skipped and a species with no table reads 'Easygoing about every biome'. A resident's page shows its current 'Ground: <biome> - <feeling>' line.");

            C("PARC.start", "Fresh-game land layout",
                "Fresh save: look at the 3x3 cluster and walk through the locked fields. Read a field's sign.",
                "Only Hearth Field (centre) is usable; 8 other fields are dimmed and walkable inside one perimeter fence. Locked fields show an info sign pointing at the Land Office ('About this land' opens it).");

            C("PARC.office", "Registrar land office",
                "Walk to the Registrar (Land Office stall) in town and open 'Land deeds'.",
                "Lists 14 deeds with name, blurb, size and price: West 120, North 140, East 150, South 160, NW 220, NE 240, SW 260, SE 280, West Road Rights 200, Reedmire Hollow 240, Bank 260, Shallows 280, Deep 300. Owned parcels show an OWNED stamp. Money is called obols.");

            C("PARC.charon", "Charon only at the Styx",
                "Walk the town, gates, roads, vendors, merchant and Land Office; read labels, prompts, guide lines and journal text.",
                "Charon / the Ferryman appears NOWHERE except the Styx crossing ceremony (owner rule). Land is sold by the Registrar. Any other mention is a bug.");

            C("PARC.registrar-flow", "The Registrar, end to end",
                "Walk up to the Land Office stall; read its label. Click it for the context menu, then press E at it (its only row, 'Land deeds', runs directly: prompt '[E] Land deeds'). Also open a locked field's sign and pick 'About this land'. Read the West Road Rights deed text.",
                "The stall label, the menu title and the modal title all read 'The Registrar' (a land clerk, never the Ferryman). The menu offers 'Land deeds'; the sign's 'About this land' opens the same modal. The road rights blurb says 'The Registrar keeps a file on every road.' Closing the modal returns movement.");

            C("PARC.buy", "Buying a parcel",
                "'coins 200', buy West Field. Then try a deed you cannot afford.",
                "Coins drop by exactly the price, the field is tillable immediately, the dimming goes, 'West Field opened!' floats, the camera clamp grows. Unaffordable shows '(needs N obols)' with no state change.");

            C("PARC.road", "Road rights and the west gate",
                "Before buying, walk to the west gate (-15, 0). Buy West Road Rights (200). Walk the corridor to Reedmire and try tilling it.",
                "The gate bar physically blocks you before purchase and disappears after. The corridor is walkable but cannot be tilled. The camera clamp includes the swamp enclosure.");

            C("PARC.swamp-prereq", "Reedmire needs road rights",
                "Before road rights try to buy Reedmire Hollow. Then buy road rights and buy all four Reedmire fields.",
                "Rows read '[needs West Road Rights]' and purchase is refused with no coin loss. Afterwards fields cost 240/260/280/300, become tillable, and have pooling water with shallow rims.");

            C("PARC.persist", "Parcels persist",
                "Buy two fields and the road, 'save', stop and run again.",
                "Owned set is identical, no gate signs remain for owned parcels, terrain is usable, camera clamp is correct on the first frame.");
        }

        private static void Lure()
        {
            Sec("Spirits: lure & residency");

            C("LURE.silhouette", "Silhouettes appear from the land",
                "Fresh game: sow grass to about 8% of the Hearth Field. Plant 2 palewheat. Plant gravebloom and wait for night.",
                "A translucent '???' Mausoleum silhouette appears at the border (grass 8%), Bansheep after 2 palewheat are planted, Phantomoth only at night (20:00-04:00) with a gravebloom, Wrabbit only once a Mausoleum is resident.");

            C("LURE.silhouette-shy", "Silhouette shyness",
                "'silhouette mausoleum' (pins one 5 units away). Walk toward it, then try to interact and click it for the menu. Wait out its absence, then approach again right after it returns.",
                "Closer than 3.2 units it drifts back (about 1.6s at 1.1 u/s) while fading out. It cannot be interacted with (menu shows '(it keeps its distance)'). It stays hidden 20 to 45 scaled seconds, then reappears on the border at least 8 units from you and fades in over about 2.4s; it will not shy again for 6s after returning. You can never touch it.");

            C("LURE.visit-feed", "Visitor decides to stay on its own",
                "Reach the Visit gate (Mausoleum: grass 15%). Do NOT feed it. Then meet its Stay gate (Mausoleum: grass 15% and 1 ripe palewheat; 'staygate mausoleum' shows [x]/[ ] per condition). Watch it for a few game-hours ('ff'). Also try 'stay' to force the decision.",
                "The silhouette becomes a visible Visitor when the Visit gate is met; feeding is never needed. With the Stay gate met it settles in for about 2 game-hours, then rolls once per game-hour (about 30%, 50% during the tutorial) and usually decides within a game-day. 'decides to stay!' floats up with a happy chirp, it looks left and right, hops, then walks to a free home of its species (else over to you), waits until you are within 15 units, and the naming ceremony begins. It becomes a Resident only through that decision. 'staygate <id>' and 'stay' work as described in their help lines.");

            C("LURE.stay-leaves", "A visitor that never gets what it wants wanders off",
                "'staygate' for a species whose visitor is on the land. Break its Stay gate (harvest the ripe crop it wants, or sand the lawn) and 'ff' about 6 game-hours with the Visit gate still met.",
                "While the Stay gate is unmet the visitor keeps wandering (no penalty, no mood change). After about 6 game-hours of it staying unmet it walks back to the border and becomes a silhouette again, and will not return as a visitor for about 3 game-hours. 'staygate <id>' shows 'unmet N/6h before it wanders off'. If the species is at its resident cap it never joins and eventually wanders off the same way.");

            C("LURE.stay-gift", "Gifts are welcome, never required",
                "'spawn bansheep' (or a real visitor) and hold its favourite food (wheat). Offer it from the menu (press E: '[E] Options', then pick 'Offer a gift (wheat)', or click the spirit and pick it). Offer again until it declines.",
                "The menu row reads 'Offer a gift (wheat)' (no n/N count anywhere). A gift spends one food, floats '(it accepts the gift)', gives a small Spirit lift and a happy chirp, and does NOT bring it closer to joining. A very content visitor (Spirit 95+) or one already deciding declines with '(it is content already)' and spends nothing. Without food the row is absent and the menu shows only Soothe and Inspect (the old '<name> is looking the place over' prompt no longer appears; the bottom prompt is always '[E] Options').");

            C("LURE.stay-bubble", "A visitor's bubble shows what it would need to stay",
                "With a visitor on the land whose Stay gate is unmet, walk up to it and wait for the bubble ('staygate <id>' shows what is missing). Meet the conditions one by one.",
                "The first unmet Stay condition picks the icon: a fruit = a crop to grow, a droplet = more water, a sprout = the land itself (grass / sand / biome), and a little roofed hut once the gate is met (it is considering a home). Time-of-day-only gaps show nothing. The bubble re-shows when you walk past, like a resident's.");

            C("LURE.console", "Spawn commands and caps",
                "'spawn mausoleum', 'resident bansheep Bob', 'spirits'. Try 4 Mausoleum residents and 3 Bansheep, and an unknown id.",
                "Visitor/resident spawn near you. 'spirits' prints name - state - Spirit%. Caps are respected (Mausoleum 3, others 2). Unknown id prints 'Unknown species'.");

            C("LURE.home", "Building and using homes",
                "Press B (Hammer unlocked), pick a spirit home, place it with the ghost. Move it, then Destroy it (click it, or stand beside it and press E: '[E] Options').",
                "Ghost is green on valid cells, red on invalid; click places (XP +8), right-click cancels. A homeless resident claims it within about 2s. Destroy needs 'Really destroy?'.");

            C("LURE.hunger-mood", "Hunger and Spirit",
                "Leave a resident unfed and watch the inspector with 'ff'. Then feed it and soothe it.",
                "Hunger rises to full over about 12 game hours. While hungry Spirit drains about 8 per hour. Feed gives +15, soothe +8. Spirit stays within 0-100.");

            C("LURE.runaway", "Runaway and herding",
                "Let a resident's Spirit fall under 15. Chase it back; soothe it.",
                "Under 15 Spirit it becomes Runaway and flees with an alarm banner. Chasing herds it back (XP +2). Soothing recovers it. 'spirits' shows state Runaway.");

            C("LURE.journal-inspect", "Journal and inspector",
                "Press J (Species, Residents, Legacy tabs, select a resident). Press R on a spirit. Try at 1280x720.",
                "Tabs populate; the journal opens and closes only with J. The inspector shows portrait, renamable name, state, Spirit bar, hunger, Vigor/Grace/Gleam, fame and fulfilment checklist, scrolls fully at 720p, X is pinned top-right. Nothing flashes on refresh.");

            C("LURE.stat-bands", "Stat bands and rolls",
                "'resident bansheep' six times and 'resident phantomoth' six times. 'stats bansheep' and 'stats phantomoth', then inspect (R) and open the journal. 'save', 'load'. Then 'reroll <name>'.",
                "Each spirit shows 'Vigor 6 (5-9)' style rows: the roll plus its species band. Bands (Vigor / Grace / Gleam): Mausoleum 2-5 / 4-8 / 3-7; Bansheep 5-9 / 2-5 / 3-7; Wrabbit 3-6 / 6-10 / 2-6; Phantomoth 2-4 / 5-9 / 5-10; Wailpertinger 5-9 / 5-9 / 4-8; Mothmaus 2-5 / 4-8 / 6-10; Bogwick 2-4 / 4-8 / 5-10; Reedhen 3-6 / 5-9 / 3-7; Sloughling 4-8 / 2-5 / 2-6; Scorpse 3-7 / 3-6 / 2-6. No Bansheep rolls Vigor under 5 and no Phantomoth over 4; individuals differ. Rolls persist after reload; 'reroll' stays inside the band and also rerolls traits.");
        }

        private static void Charm()
        {
            Sec("Spirits: charm & behavior");

            C("CHARM.want-bubble", "Want bubbles",
                "Let a resident get hungry or homeless. Walk away and back.",
                "A thought bubble (food / water / lonely / home icon) pops once when the want first occurs, fades within seconds, and shows again when you walk by or interact. No permanent icon over every spirit.");

            C("CHARM.mood-body", "Mood reads as body language",
                "Compare a Spirit 100 resident ('blessing') with one near 30, and a starving one.",
                "Happy, neutral and low read through posture and pace (upright and bouncy vs drooped and dragging) with no UI. Starving reads as sick. Species differ from each other.");

            C("CHARM.greeting", "Daily greeting",
                "Approach a resident within 2.5 units, leave, return the same day, then 'day' +1 and approach again.",
                "A hop and chirp the first time each resident sees you per game day, not again the same day, again after dawn. Spirit% does not change.");

            C("CHARM.drift", "Mood drift toward / away",
                "Stand still 3-6 units from a happy resident (70+) and a low one (under 30) for 30 seconds.",
                "Happy ones drift toward you and tag along a few steps; low-mood ones edge away. Nothing reacts beyond about 6 units.");

            C("CHARM.sprint-startle", "Sprint startle is visual only",
                "Note a resident's Spirit. Sprint past within 1.5 units twice.",
                "It flinches (recoil and squeak) at most once per 4 seconds. Spirit% does NOT drop.");

            C("CHARM.quirks-habitat", "Idle quirks and habitat habits",
                "Watch 5 residents unattended for 3 minutes, including at night near a lit Watchlight.",
                "Naps, stretches, hops and leaf-chases occur with species-different frequency, never while following, resting or in a ceremony. Habitat species use their anchors (rocks, water edge, lights at night).");

            C("CHARM.pair-play", "Pair interactions",
                "'resident mausoleum A', 'resident mausoleum B' and one other species next to them.",
                "Same species always play (circle each other, happy chirps, small Spirit bump). Cross-species either play or squabble (back off, grumpy blip) with no mood loss, consistently for the same pair across runs.");

            C("CHARM.night-shift", "Night shift",
                "'time 23' with day-active and night-active species around.",
                "Day species sleep curled up with a slow breathing swell and zzz; night species are awake. The switch around dusk and dawn is staggered, not all at once.");

            C("CHARM.voices", "Species voices",
                "With headphones trigger greet, feed, soothe and startle for two species.",
                "Each species has a recognizably different chirp (pitch and contour) and intents differ. 'audio mute voice' (or Ctrl+M) silences them. No clipping or painful peaks.");

            C("CHARM.follow-treat", "Follow treat",
                "Buy a treat (5 obols), Give treat (follow) to a resident, walk around fences and gates for 3 minutes.",
                "The spirit trails you through gates and around obstacles without getting stuck, then stops after a while. Treat is consumed once.");

            C("CHARM.traits", "Individual traits",
                "'traits' (lists the pool). 'resident mausoleum A' six times and read the inspector (R) and the journal. 'traits A brave lazy', 'traits A roll', then 'stats A'. 'save', 'load'.",
                "The pool is nine traits: brave, curious, dreamy, gentle, greedy, lazy, rowdy, showoff, skittish ('Trait pool: ...'). Each spirit rolls 1 or 2; the inspector and journal show 'Traits: Brave, Lazy' with a flavor line each (Brave: 'Marches toward whatever just squeaked.'). Traits shift idle-quirk frequency and training appetite (Rowdy hops more and favours the heavy stones, Lazy naps more and trains less) and persist across reload. 'traits A brave lazy' sets them, 'roll' rerolls, unknown ids print 'No known trait in: ...'.");

            C("CHARM.training", "Training buildings",
                "'coins 100'. Build Heavy Stones (30 obols), Hurdle Run (30) or Gleam Mirror (40) from B, or 'gym stones' (free, 2.5 units east of you). 'resident bansheep A', 'blessing', 'trainnow A', 'ff' a few game hours. Watch 'stats A'.",
                "Spirits walk to the building on their own when they feel like it (mood not Low, hunger under 80%), do an 8 second session (capacity 2, within 1.9 units) with exercise hops, and gain +1 training tick per session: 6 ticks = +1 stat point (stones train Vigor, hurdles Grace, mirror Gleam), never past the species max. 'stats' shows 'train xp V1.0 G0.0 L0.0/6' and 'TRAINING' while in a session. There is no player drill. Buildings and progress persist after reload. The selection menu reads '(spirits train Vigor here)'.");

            C("CHARM.train-cap", "Training progress and cap",
                "'train A vigor' (one point), 'train A vigor 3' (half a point), then 'train A vigor 60' until the cap. 'stats A'.",
                "'train <name|all> <vigor|grace|gleam> [ticks]' defaults to 6 ticks = one point and prints 'A: Vigor 6 -> 7 (max 9)'. Partial ticks show in 'stats' as 'train xp V3.0 G0.0 L0.0/6'. At the species maximum it prints 'at cap' and the stat does not rise.");

            C("CHARM.bait", "Bait food slot",
                "Place a training building. Click it: 'Add bait: wheat (have N)'. Or 'bait berry' / 'bait none'. Compare which species visit with and without bait ('trainnow all').",
                "The slot holds up to 6 charges (2 per food item); the menu offers 'Add bait: <food>' for foods you hold and 'Clear bait'. Species whose favored food matches the bait are 3x as keen to visit and gain x1.5 per session. With no bait everyone trains at the base appetite. 'bait none' prints '<building>: bait cleared'. Bait persists after reload.");

            C("CHARM.rest", "Sit and rest (Z)",
                "Press Z (or 'sit'). Wait 60s, push WASD while seated, then press Z again. Try sitting mid-swing, with a modal open and while riding. 'sit status' while seated.",
                "The shepherd squats and tools are put away; the hint reads 'Resting. Move to look around. Z to stand.' Move input pans the camera freely (up to 16 units) instead of walking; standing returns it. Sitting is refused during a tool swing, any modal, a competition, ghost placement, and while riding (V while seated says '(stand up first)'). Walking away or teleporting stands you up. Nothing is saved. 'sit status' prints 'Rest: seated 60s, gathered 3, pan 0.0,0.0'.");

            C("CHARM.rest-gather", "Rest gathering and mood",
                "'resident mausoleum A', 'resident bansheep B', 'resident wrabbit C', 'mood happy all', and one more with 'mood sad D'. Stand 5 units away and press Z. Watch Spirit with 'spirits' over a minute.",
                "Happy spirits within 8 units drift in and settle in a loose ring around you (max 6; ring radius about 2, alternating +0.55) and an occasional '<3' floats up; the sad one stays out. Each settled spirit gains about 0.15 Spirit per second (about +9 per minute) but only up to 90, so rest alone never maxes a spirit. Standing up lets them resume wandering. Followers stay within 3.5 units.");

            C("CHARM.mood-anim", "Per-species mood animation states",
                "'resident mausoleum A', 'resident bansheep B', 'resident phantomoth C', 'resident wailpertinger D'. 'mood happy all', watch; 'mood neutral all', watch; 'mood sad all', watch. Then let one go hungry with 'ff'.",
                "Happy is 70 or more, sad is under 35 ('mood happy' sets 90, 'neutral' 50, 'sad' 20). Each species moves differently when happy: Mausoleum bounces, Bansheep sways, Phantomoth flutters, Wailpertinger waddles. Sad reads as drooped (lower, wider, washed out, about 0.6x bob rate, 0.75x walking speed) with no UI. A starving resident (hunger full) reads as sick even at high Spirit. Silhouettes and runaways keep their own motion.");
        }

        private static void Naming()
        {
            Sec("Naming");

            C("NAME.prompt", "Naming prompt",
                "Meet a visitor's Stay gate and wait for it to decide ('stay' forces it), or 'naming mausoleum'.",
                "Naming modal appears with a prefilled editable name while the spirit and guide-light stay visible; gameplay is blocked. Confirm names it, an echo floats up, the journal lists it.");

            C("NAME.light-descends", "Light-descends ceremony",
                "'naming mausoleum Bob' (spawns a fresh resident and plays the ceremony; close the console to watch). Watch the whole sequence, type a name, confirm.",
                "About 6 seconds of choreography plus typing time: the world eases to 30% speed (gameplay blocked), the guide-light drifts down from above (about 2s) to hover over the spirit with a warm glow and one gentle chord, the spirit bows (about 1.2s) as the name field appears pre-filled with 'Bob' (still editable). Confirming echoes the name as three floating texts, time eases back to full speed, the light drifts off, and the journal page flips open on the new resident. The world label, journal and later headstone all use the typed name.");

            C("NAME.skip", "Skipping the ceremony",
                "'naming mausoleum' and press Esc during the descent. Run it again and press Esc after the field is up. Type letters in the field (never skips).",
                "Esc during the flourish jumps straight to the name field (no pause menu opens, world speed returns, the light does not stay behind). After naming, the echo text and journal flip are dropped but the name always lands. Typing in the field never skips. Esc at any point never leaves the world dimmed or input blocked afterwards.");

            C("NAME.queue", "Queue and console refusal",
                "While a naming ceremony is playing open the console and type 'naming mausoleum'. Later, try to get two spirits settling close together (two visitors deciding at once: 'spawn mausoleum', 'spawn bansheep', then 'stay' twice).",
                "The console refuses with 'A naming ceremony is already playing.' Real requests that arrive mid-ceremony queue and play one after another: every spirit gets its own ceremony and name, none is dropped, none plays twice. If the scene is quit mid-ceremony, an unnamed spirit simply loads unnamed and can be named from the inspector.");

            C("NAME.typing-lock", "Typing never leaks",
                "In the name box type 'wasd ejcbtrzf', Tab, Space, arrow keys, Backspace.",
                "No gameplay or modal action fires (no walking, journal, calendar, build, tool swing, console). Text editing works. F8 does not open the QA panel.");

            C("NAME.edge-cases", "Empty, long and odd names",
                "Confirm an empty name. Try a 30+ character name and one with digits and punctuation. Rename later via the inspector.",
                "Empty falls back to the prefill/default (never a nameless resident). Long names fit labels, inspector and journal without overflow. Rename updates label, journal and headstone immediately and persists. Queued namings never drop or duplicate.");
        }

        private static void Styx()
        {
            Sec("Styx, pad, headstones");

            C("STYX.pad-unlock", "Ascension pad unlock and build",
                "Fresh save: open B. Then 'resident mausoleum A', 'ready', open B again, build the pad, reload.",
                "No pad row before any spirit has been fulfilled. After that it appears (25 obols), reads '(built)' once placed (only one pad), placement ghost works, pad persists after reload.");

            C("STYX.pad-hints", "Pad fails softly",
                "Interact with the pad with no spirit near, with an unfulfilled spirit, and with a fulfilled spirit that is not following (no treat).",
                "Each case fails with a hint naming what is missing. No ceremony starts, no error.");

            C("STYX.sequence", "Crossing sequence",
                "Fulfilled + following spirit at the pad ('ready', give treat). Confirm. Watch the whole thing, then repeat with other residents nearby.",
                "In order: gameplay blocks, the spirit goes to the pad and residents gather; screen darkens except pad and spirits; river and Charon slide in with a low chord ('Payment, little light.'); 9 gold orbs arc to Charon with coin bleeps; spirit boards and the skiff sails off; farewell chirp chorus; light returns; headstone drops. About 20-25 seconds.");

            C("STYX.feel", "Goodbye feel (judgment)",
                "Run it again and judge as a player.",
                "DEV JUDGMENT: sad but nice? Note pacing, darkness level, chord, orbs and text. Say what to change.");

            C("STYX.skip-lighting", "Skip and lighting restore",
                "Press Esc mid-ceremony. Run one at 'time 12' and one at 'time 23'.",
                "Esc jumps to the committed end state (headstone exists, spirit gone) without opening the Pause menu; typing in the console never skips it. Lighting returns to the correct tint, no sprites stay above the black overlay or hidden.");

            C("STYX.headstone", "Headstone placement and epitaph",
                "Carry the stone after a crossing: place on a bad cell, right-click, later use 'Move stone'. Press E at it ('[E] Options': Read, Move stone) and pick Read repeatedly.",
                "Ghost is green/red; right-click leaves the stone at the pad; it can be moved again any time. 'Read' cycles epitaph lines (name, species, times fed, days among us) and keeps the menu open for the next line.");

            C("STYX.quit-mid", "Quit mid-ceremony",
                "Start a crossing, then Save & Quit (or stop Play) before it ends. Reload.",
                "Restores as ceremony never started: spirit is a fulfilled resident near the pad, no headstone, nothing duplicated or lost, ceremony can be redone.");

            C("STYX.persist-legacy", "Legacy persists",
                "Complete two crossings, save, reload, open J Legacy tab.",
                "Headstones persist at placed positions with names; Legacy tab lists both; no leftover fixed altar or light column anywhere on the farm.");

            C("NB.memorial-garden", "Memorial garden grows in",
                "'garden stones 4' (lays 4 placed stones in a tight cluster beside you), then 'garden', 'garden age 4', 'garden', 'garden age 4', 'garden'. Look at the flowers each time.",
                "'garden' prints 'Stones: 4 placed, 0 waiting. Night factor 0.00.' and one line per stone like '  Pip: bloom 0%, 3 neighbour(s), 0 blooms, 0 wisps'. Bloom % is game-days since placing over 8: age 4 reads about 50% (first flowers creeping in), age 8 reads 100% (full bloom, up to 16 flowers per stone). Stones with placed neighbours inside 2.4 units (counted up to 5) bloom fuller. Purely cosmetic: no Spirit, growth, price or speed changes anywhere near the garden.");

            C("GARDEN.night", "Garden night glow and wisps",
                "'garden stones 4', 'garden age 4', then 'time 12', 'time 19', 'time 22', 'time 4', 'time 7' and 'garden' at each.",
                "Night factor is 0 by day, ramps up from 17:00 to full at 20:00, stays full until 05:00 and fades out by 07:00. A soft cool glow wakes under each stone with it. Once a stone is 35% grown (about 2.8 days) pale wisps drift among the stones in lazy figure-eights, blinking like fireflies: 1 plus its neighbours, at most 4 per stone ('garden' shows the wisp count). No pops when the night factor changes.");

            C("GARDEN.place-move", "Unplaced stones and moving stones",
                "'garden drop', then select the stone and pick 'Move stone', place it near the others. Also 'garden drop' again then 'garden place'. Move an old stone a few cells.",
                "'garden drop' prints 'Dropped an unplaced stone for <name>. Select it > Move stone to place it.' and the status shows 'waiting to be placed'. Placing starts its own garden clock at 0%. 'garden place' marks every waiting stone placed where it stands. Moving a placed stone carries its flowers, glow and wisps with it. The journal Legacy list shows '(N stones waiting)' until all are placed.");

            C("GARDEN.gallery", "Legacy gallery page",
                "Do a real crossing (or 'garden stones 3'), press J, open the Legacy tab, click names. Place a stone and reopen.",
                "Left column: 'The memorial garden' and 'N names remembered', one row per crossed spirit ('Pip the Mausoleum - Day 12'), paged with '< Earlier' and 'Later >' when there are many. The page shows name, species, days on the farm, times fed, 'Crossed over on Day N', 'Their last wish, granted: ...' and either 'Their stone rests in the garden - the first flowers are creeping in, keeping 2 neighbours company.' (wording follows bloom) or the gold 'has not been placed yet' hint. The page says only that they crossed, never how.");

            C("GARDEN.persist", "Garden is derived, not saved",
                "'garden stones 6', 'garden age 5', note the layout, 'save', stop and start, 'garden'.",
                "The identical garden reappears on the first frames after load: same flower positions (stable per stone), same bloom % (from the saved placed time), no stones lost or doubled. Unplaced stones come back unplaced.");
        }

        private static void Weaving()
        {
            Sec("Weaving");

            C("WEAVE.loom", "Build and open the Loom",
                "B menu: The Loom (60 obols), place it, then press E at it (single row 'Weave' runs directly: prompt '[E] Weave') or click it. 'save' and reload.",
                "Only one Loom can exist. WeaveUI opens and lists only residents at 100 Spirit. The Loom is still there after reload (record the outcome if not).");

            C("WEAVE.recipes", "Recipes, toll and rejects",
                "'resident wrabbit A', 'resident bansheep B', 'blessing', get 6 essence, weave via the UI. Try an invalid pair and too little essence.",
                "Wrabbit + Bansheep gives Wailpertinger; Mausoleum + Phantomoth gives Mothmaus. Toll of 6 essence is shown and charged once. Invalid pair or too little essence is refused with no partial consumption.");

            C("WEAVE.console", "Console weave",
                "'weave' with and without a matching pair.",
                "First recipe-matching resident pair is set to 100 Spirit and woven. With no pair it prints 'No resident pair matches any weave recipe.'");

            C("WEAVE.ceremony", "Weave result and bookkeeping",
                "Run one weave (see NB.weave-rite for the visuals) and check what exists afterwards: 'spirits', 'inv', 'banners'.",
                "Parents are removed exactly once, the cryptid exists as a Resident with its own species, the toll (6 essence) was charged once, naming follows with a blended suggestion, and one banner record exists.");

            C("NB.weave-rite", "Night loom rite",
                "'coins 100', build the Loom, 'resident wrabbit A', 'resident bansheep B', then 'weaveforce A B' (sets both to Spirit 100 and grants missing essence; no Loom in scene prints 'No Loom in the scene (build one, or use weavenow).'). Watch the whole rite, ideally once by day and once at night.",
                "About 16 seconds, in order: gameplay blocks and both parents walk to either side of the Loom (6s timeout); a deep blue-black overlay (alpha 0.84) darkens everything EXCEPT the parents, you and the Loom; each parent unravels (thins and stretches) while a thread in its species colour is pulled up toward the Loom (2.6s); the Loom weaves the two threads into a cloth with a glowing shuttle (3.6s); one soft chord; the new cryptid steps out of the cloth (2.2s); the cloth folds into a banner and drifts to the pouch (1.2s); light returns and the naming ceremony opens titled 'A legend is woven!'. It plays the night look at any hour (no time gate). Esc jumps to the committed end state without opening the pause menu.");

            C("WEAVE.rite-quit", "Quit mid-rite",
                "Start 'weaveforce A B', then Save & Quit (or stop Play) before the cloth completes, and reload. Repeat quitting during the naming ceremony.",
                "Restores as the rite never started: both parents are full-spirit residents near the Loom, no cryptid, no banner, essence not charged. Nothing is duplicated or lost. Quitting after the cloth completes keeps the committed result (cryptid and banner exist, parents gone).");

            C("NB.weave-inherit", "Inheritance: stats, traits, name",
                "'resident wrabbit A', 'resident bansheep B'. Push A and B to the top of their bands ('train A grace 60', 'train B vigor 60'), then 'inherit A B'. Repeat with freshly rerolled weak parents. 'traits A brave', 'traits B gentle', 'inherit A B'. Then 'weavenow A B' and 'stats'.",
                "'inherit' prints each parent with its traits, 'Stat bias (-1..1): vigor 0.40, grace 0.55, gleam 0.10' (each parent's roll as a position inside ITS OWN band, averaged: strong parents give a positive bias, weak ones negative) and five 'trait roll' lines. The child's roll inside ITS species band is pulled toward the bias. It inherits one trait from each parent when the child species allows it (never a conflicting pair, never more than 2) and the rest is rolled. 'weavenow' skips the Loom and rite but still names the result and creates the banner, printing 'Wove A + B into a Wailpertinger. Banner id N is in the pouch.'");

            C("WEAVE.name-blend", "Name echo",
                "'blend Pip Wisp', 'blend Pip -', 'blend - -'. Then run a weave and read the pre-filled name.",
                "'blend' prints 'Blend suggestions:' and six names (for example Pisp): the head of one parent plus tail of the other, 3 to 10 letters, capitalized ASCII, never just one parent's name. '-' stands for an unnamed parent (a random pool name stands in); with both '-' it prints '(null: ceremony picks a random name)' six times. The naming field after a weave is pre-filled with such a blend and is fully editable.");

            C("WEAVE.banner", "Tapestry banners",
                "'banner' (adds a test banner), 'banners' (lists them). B menu, pick 'Hang: Pip & Wisp', place it; try a built cell, a locked cell, a second banner on one cell. Click the hung banner or press E at it ('[E] Options'): Read, Move banner, Take down. 'save', 'load'.",
                "'banners' prints lines like '#1 Day 3: Pip the Wrabbit + Wisp the Bansheep -> Pipwisp the Wailpertinger | banner carried' or 'HUNG at x,y'. Hanging needs usable land with nothing built there and one banner per cell. The banner art is woven from the two parents' thread colours and labelled 'Pip & Wisp'. Menu: 'Read' cycles floating lines about the weave, 'Move banner', 'Take down' returns it to the pouch. Carried and hung state persist; garden stones stay exclusive to Styx-crossed spirits.");

            C("WEAVE.rumors", "Rumor hints",
                "'resident wrabbit A', 'resident bansheep B', 'forget all', 'recipes', then 'rumor' (vendor), 'rumor guide', 'rumor reset', 'rumor clear'. Walk past a vendor stall afterwards.",
                "'recipes' prints rows like 'wailpertinger: Wrabbit + Bansheep -> Wailpertinger | UNDISCOVERED | toll 6 | residents 1/1 | rumors heard 0'. 'rumor' prints '[vendor] <line>' and shows the bubble at your position; 'rumor guide' prints '[guide] <line>' and the guide-light says it (never during the onboarding tutorial). Lines hint at the pair without naming the recipe ('Wrabbit and Bansheep share a thread'). Naturally at most one rumor per game day (70% chance, 40% of them via the guide), only for undiscovered recipes whose parent species are both resident, vendors speak within 3.5 units of their stall, up to 3 per recipe are kept. 'rumor reset' refunds today's budget, 'rumor clear' forgets all. Charon is never a rumor source.");

            C("WEAVE.journal-pages", "Journal ??? pages and woven pages",
                "'forget all', open J, Species tab, find the cryptid rows. 'rumor' a few times, reopen. Then 'discover wailpertinger' (or really weave it) and reopen its page.",
                "An undiscovered cryptid's row reads '???' and its page shows a dark silhouette, '???', 'A thread nobody has pulled yet. It will take two.' and 'Rumors: none heard yet.' (or 'Rumors heard:' with each line and its teller). After discovery the page shows 'Woven at the Loom' and 'Woven from Pip the wrabbit and Wisp the bansheep' (no gates, no 'Appears when'). Resident pages of a cryptid record its parents.");

            C("WEAVE.woven-away", "Woven away in the Legacy tab",
                "Weave a pair, open J, Legacy tab.",
                "Under the crossed list a 'Woven away' section lists each weave: 'Pip the Wrabbit and Wisp the Bansheep - woven into Pipwisp, Day N'. It stays visible whatever stone is selected; with no crossings and no weaves the tab reads '(no one has moved on yet)'.");

            C("WEAVE.journal-quit", "Journal and quit mid-weave",
                "J after a weave. Then quit mid-weave and reload.",
                "Cryptid species is discovered and counted. After a mid-weave quit either both parents exist or the cryptid exists (never neither, never both) and essence is not double-charged.");
        }

        private static void Friction()
        {
            Sec("Villains & Repo-man");

            C("VILL.digger", "Digger",
                "'villain digger' on tilled land. Tamp a hole. Save and reload.",
                "A mole-like blob digs up to 4 holes over 1-2 game hours then leaves. Holes block planting and building (menu shows '(blocks planting/building)'); two quick Tamp interacts or one Hoe swing (see VILL.hoe-flatten) fix them. Holes persist after reload. ('villain' bypasses ward charms; a natural Digger visit with a Mud-Stone carried turns back at the fence instead.)");

            C("VILL.hoe-flatten", "Hoe flattens holes",
                "'villain digger', wait for holes, select the Hoe, face a hole and use it. Try again on a normal cell. Compare with pressing E twice on a hole.",
                "One Hoe swing on a hole flattens it ('flattened!' floats, XP +2 as a till) and leaves the ground as it was (no extra tilled cell); the Hoe on a clean cell still tills. A hole always counts as a valid Hoe target. E on a hole (prompt '[E] Tamp') needs two quick taps; its menu row 'Tamp' stays open for the second. Both fixes persist.");

            C("VILL.devourer", "Devourer",
                "Plant 4 crops (some immature), then 'villain devourer'.",
                "Approach is telegraphed. It eats at most 2 plants per visit, preferring immature ones. Loss is small and recoverable.");

            C("VILL.scarer", "Scarer",
                "With 3 residents (note Spirit), 'villain scarer'.",
                "Up to 4 frights at -6 Spirit each, never below 25. Residents flinch. It leaves after its visit.");

            C("VILL.drive-off", "Driving villains off",
                "Run at a villain. Place a Watchlight near target crops and repeat. Try to feed or name one.",
                "Chasing drives it off. A villain never steps inside ward radius 6, paces, grumbles and leaves early when everything it wants is warded. Villains cannot be fed, named or befriended.");

            C("VILL.schedule", "Natural villain schedule",
                "With under 2 residents or before day 4 step days. Then 2+ residents, day 4+, step 10 days. Run 'villain digger' twice in a row.",
                "No natural visits with fewer than 2 residents or before day 4. Afterwards at most one visit per day arriving 08:00-20:00. A second 'villain' while one is visiting is refused. A loaded game starts villain-free.");

            C("REPO.dispatch", "Repo-man walk-in and warning visit",
                "'gentle off', make a resident Runaway, wait 2 game hours or use 'repo'. Note the first visit, then a later one.",
                "First-ever visit is a lecture and takes nothing. He walks in visibly from the town gate (never teleports); later visits take the spirit to the Holding Office. Soothing the runaway first turns him around. 'gentle on' (pause menu) disables dispatch.");

            C("REPO.whistle-bribe", "Whistle, nervous glances and the bribe dialog",
                "'resident mausoleum A', 'coins 60', 'repo skipwarn', 'repo runaway', 'repo'. Listen from a distance and as he nears. Watch residents within 6 units. Interact with him mid-walk (prompt 'Bribe the Repo-man (8 obols)'), or wait with him standing over the spirit. 'audio log' while he walks.",
                "He walks in at about 1.6 u/s (never teleports) whistling an off-key tune every ~2.2s; the whistle fades to silence by 22 units, stops when he leaves, and is a normal Sfx-bus sound ('audio log' shows key repo_whistle, 'audio mute sfx' silences it). Residents within 6 units glance nervously ('(gulp)', '...', '(eyes down)'). When he arrives he says 'Ahem. Per section 12...' and, with you within 7 units, the modal opens: 'The Repo-man', 'Section 12 applies to <name>.', buttons 'Slip him N obols' and 'Let him proceed' (Esc stays Pause). Pay: he says 'A modest consideration. Never happened.' and leaves without the spirit, once per visit. Proceed: he takes it at once ('Per section 12: repossession.'). If you do nothing for 25s he takes it. Short of obols the dialog shows '(needs N obols)'. Warning visits cannot be bribed (nothing to buy off).");

            C("REPO.bribe-price", "Bribe price escalation",
                "'repo price', pay a bribe, 'repo price' again. Then 'repo bribes 3', 'repo bribes 6', 'repo bribes 20'. 'save', 'load', 'repo price'.",
                "'repo price' prints 'Bribes paid: 0. Next bribe: 8 obols.' Each payment adds 5 (8, 13, 18, 23, 28, 33, 38) and the price is capped at 40, so 'repo bribes 20' still reads 40. The count persists across save and load. 'repo skipwarn' marks the warning visit spent so the next dispatch is a real one.");

            C("REPO.holding-office", "Holding Office reclaim",
                "'resident mausoleum A', then let him take it ('repo runaway', 'repo skipwarn', 'repo', refuse the bribe). Reclaim at the office (press E at it: its only row, 'Enter', runs directly; 2x favored food). Watch it for 1 game hour with 'spirits'.",
                "Reclaim costs exactly 2x the favored food (any quality tier, lowest used first) and the spirit appears at the office door as a Resident (never Runaway) with Spirit 35 and half hungry, with the hunger clock reset to match, so it does not flee again within an hour. 'Released. Do keep better records.' floats. Too little food is refused with '(needs 2x <food>)' and nothing is consumed.");
        }

        private static void Frontier()
        {
            Sec("Road, swamp & mount");

            C("NB.road-dangers", "Road status and natural schedule",
                "'parcel 9' (road rights), 'tp -16 0', then 'road' at x = -16, -22 and -26 (use 'tp'). Also 'road' before owning road rights. Over several days with an escort, note the toll and ambush days.",
                "'road' prints 'road: <segment> (<territory>), escorts N, dark yes/no, lantern yes/no, tollDay yes/no, ambush none, lastAmbushDay N'. Segments west from the home gate: Hearth Verge (x -15 to -20.5, Grassland), Dust Reach (-20.5 to -24.5, Desert, the hostile middle), Mire Fringe (-24.5 to -27, Swamp); the two ends borrow the censused biome of the base they lead out of when it is not Barren. Off the corridor it reads 'road: off-road'. Without road rights it adds a line that toll imps wait for them. Naturally: a toll imp on about 40% of days (same answer for the same day after reload), at most one ambush per day with about 35% odds per crossing that has escorts ('ambush armed@x' shows while pending). The road alone never makes a spirit run away except an ambush bolt.");

            C("ROAD.dark-lantern", "Dark stretch and the lantern",
                "'parcel 9', 'resident wrabbit A', 'blessing A', menu A > 'Come along'. 'gear clear', walk west into the dark stretch (x -19 to -25.2) or 'dark on'. Then 'gear lantern' and repeat. 'dark off'. 'inv'.",
                "A dark overlay closes in on the shepherd (fades at about 2.5 per second, no pops). Without a lantern: it is nearly black (alpha 0.93, small lit radius), '(the dark swallows the road - a lantern would help)' floats once, and each escort in the dark crawls at 0.55x speed and loses 1.1 Spirit per second down to a floor of 35 with '(it hates the dark)' toasts (max one per 7s per spirit). With a lantern in the pouch (durable, never consumed, no equip key) the dark is lighter (alpha 0.5, wide lit radius) and escorts keep pace and keep their mood. 'dark on' prints 'Forced darkness ON (no lantern: escorts will slow and sulk).'; 'road' shows 'dark yes [forced]'. 'inv' still lists lantern x 1 afterwards.");

            C("ROAD.strain", "Biome mismatch strain",
                "'resident bansheep A' and 'resident mausoleum B', 'blessing', both 'Come along', walk the Dust Reach (x -20.5 to -24.5). Then bring a Wrabbit or a Bogwick. 'road' to confirm the segment.",
                "On ground a species dislikes, each escort loses Spirit per second: Dislike 0.9, Hard No 2.2 (Bansheep and Bogwick hate Desert); Neutral, Like and Love lose nothing. It stops at a floor of 30 (a spirit already under it is left alone) and shows '(<name> hates this ground)' (max one per 7s). It is never enough to cause a runaway on its own, and it recovers with soothing or feeding. Route planning matters: Mausoleum and Bansheep also dislike the Mire Fringe (Swamp).");

            C("ROAD.toll", "Toll imp",
                "'parcel 9', 'resident wrabbit A', 'blessing A', 'Come along', 'toll on', walk to the imp at the road's head (about -17.4, 0.9). Pay once, walk the road both ways. 'toll reset', then refuse by walking past. 'toll off'.",
                "'toll on' prints 'Toll imp forced ON (it appears within a second at the road's head)'. Within 4 units it quips ('Toll! Small. Tiny. Practically a hug. (3 obols)'). The toll is 2 obols plus 1 per escort, capped at 6. Press E ('[E] Options') and pick 'Pay toll (3 obols)': '-3 obols. <line>', and it waves you through for the rest of the day in both directions (menu then says '(paid up for today)'). Short of obols shows '(needs N obols)' and nothing is taken. Refusing (menu 'Refuse': 'Fine! *raspberry*') or just walking past it ('Hmph! Rude! *raspberry*') costs you: each escort loses 5 Spirit (floor 35, '(insulted)') and nothing else; it forgets at dawn. 'toll reset' forgets today's settlement. Naturally it only camps 06:00 to 20:00 once road rights are owned.");

            C("ROAD.ambush", "Road ambush",
                "'parcel 9', 'resident wrabbit A', 'resident bansheep B', 'blessing', both 'Come along', face west on the road, 'gear clear', then 'ambush scarer'. Wait it out or walk up to it. Repeat with 'ambush devourer'.",
                "'Scarer ambush started (a matching ward charm turns it away).' An ambusher bursts out about 3 units ahead with an 'AMBUSH ON THE ROAD' banner and a short 0.8s telegraph, then strikes once. Scarer: every escort within 4.5 units loses 6 Spirit (floor 25) and each bolts with 60%. Devourer: lunges at the nearest escort (it always bolts) and 35% a second one; no item or spirit is ever lost. At most 2 bolts per ambush. The ambusher then lurks 7s; walking within 2 units drives it off, otherwise it slinks away, and the banner hides. With no escort the console warns '(no escorts right now - Come along a resident first to see the bolt)'.");

            C("ROAD.bolt-herd", "Bolt and herd recovery",
                "After 'ambush scarer' with escorts, let one bolt, then run at it to herd it back (the normal tags). Ignore a second bolted one for a few minutes.",
                "A bolted escort drops following and becomes a Runaway already mid-flight, scattering 3 to 5.5 units away along the lane. Two herding tags bring it back (XP +2 per the herding rule), then soothing recovers it. If ignored, the usual runaway clock and Repo-man rules apply unchanged (see REPO.dispatch). Never more than 2 spirits bolt from one ambush.");

            C("NB.defense-pouty-mount", "Ward charms (Bell Charm, Bitter Bait, Mud-Stone)",
                "'gear bell 2', 'gear bait 2', 'gear mud 1', 'inv'. Then 'ambush scarer' with the Bell Charm, 'ambush devourer' with only a Bell Charm, and 'ambush scarer' beside a Watchlight. 'gear clear'.",
                "Charms are consumable one-shots carried in the pouch (bell_charm, bitter_bait, mud_stone). The matching charm is auto-spent the first time its villain would strike: Bell Charm vs Scarer, Bitter Bait vs Devourer, Mud-Stone vs Digger. The ambusher recoils at once with '(it recoils from your ward)', 'Bell Charm spent' floats and nobody is frightened. A mismatched charm is not spent. A Watchlight over the spot fends the ambush without spending a charm. 'gear' prints '+2 Bell Charm (Scarer ward).'; 'gear clear' prints 'Road gear cleared.' A natural farm Digger visit with a Mud-Stone carried turns back at the fence ('(something unpleasant turns back at the fence)'); the 'villain' console command bypasses wards.");

            C("ROAD.gear-shop", "Road gear for sale",
                "'coins 100'. Open the town Vendor and buy a Road Lantern twice, a Bell Charm, a Bitter Bait and Sand. Try with 5 obols too.",
                "Town stock: Road Lantern 25 obols ('keeps escorts brisk and cheerful in the dark stretch; never used up'), Bell Charm 8, Bitter Bait 8, Sand 8 (4 loads). A second lantern is refused with '(you already carry one)' and no charge; a shortage shows '(needs N obols)' with a denied bleep. A sale floats '+1 bell charm' with a coin bleep. Mud-Stone is NOT sold here.");

            C("SWAMP.peddler", "Mire Peddler (swamp vendor)",
                "'parcel 9', 'tp -29 5', walk up to the stall. Read the label, click it, Trade, buy both items. 'coins 50' first.",
                "A stall labelled 'Mire Peddler' stands just inside the swamp gate on the north side and is always there (no milestone). The shop is titled 'The Mire Peddler' ('Mud has uses. Come back from the dry end with questions.') and sells Peat Compost (3) for 12 obols (adds 3 compost, usable on the prairie farm) and Mud-Stone for 10. Mud-Stone is sold nowhere else, which is the biome-exclusive vendor hook.");

            C("SWAMP.mirewort", "Mirewort (swamp weed)",
                "'tp -35 0' inside the Reedmire enclosure, 'weed', then 'weeds'. Compare with a weed on the home farm. Pull it.",
                "A weed that sprouts inside Reedmire is labelled 'Mirewort' (prompt '[E] Pull'; menu title 'Mirewort', later 'Mirewort (grown wild)'), has a murky teal tint and a 1.2x harm radius. Harm numbers, 3 quick tugs, +1 fiber and the cap of 4 live weeds are the same as a normal weed. It never destroys anything.");

            C("SWAMP.species", "Swamp species: Bogwick, Reedhen, Sloughling",
                "'parcel 9', 'parcel 10', make Reedmire a Swamp ('biome 1 swamp' pins it for gate testing, or dig water to 12% and plant 2+ crops beside it). Plant glowcap lily and reed. Read their journal pages and try each lure at the right time of day.",
                "Bogwick appears in a swamp with a glowcap lily planted and only after dark, visits for a ripe lily at night; favored food glowcap; wish: give 1 glowcap. Reedhen appears with 2 reeds planted, visits for 3 ripe reeds in daytime; favored food reed; wish: dig water within 2 cells of its home. Sloughling appears in any swamp and visits once the swamp score is 30 or more; favored food berry; wish: give 2 berries. All three Love Swamp; Bogwick has a Hard No on Desert. Their silhouettes favour the Reedmire base ('spawntable').");

            C("MOUNT.join", "Pouty Mount joins (Grudge)",
                "'mount' (status). Buying 5 fields alone does nothing now. Buy the road rights ('parcel 9' opens them), then walk the road end to end (home gate to the swamp gate and back) 6 times: 'mount crossings' shows the count ('mount crossings 6' sets it; 'mount skipwait' skips the 1 game-day wait after buying the rights). Once all three are met, walk onto the road. Also 'mount revoke' then 'mount join'.",
                "'mount' prints 'mount not joined (road rights ..., crossings N/6, wait ...)'. Turning around half-way never adds a crossing; reaching the far end does. With road rights, 6 crossings and a day passed, the next time you are on the road: a guide line 'Something horse-shaped stands in the road ahead, pretending not to look at you.', it trots in, stops a pace away, '*sigh*', '(it has decided to allow this)', then 'Grudge, a pouty horse-spirit, has joined you. It will carry you, if it feels like it. Press V ...'. It then trails you about 2 units behind (label 'Grudge', menu 'Grudge, the Pouty Mount'). 'mount grant' joins with no scene, 'mount revoke' resets so the scene fires again.");

            C("MOUNT.ride", "Riding Grudge",
                "Join the mount, walk within 4 units, press V. Ride, sprint, try tools, B, E, then press V again. Try V from far away.",
                "V mounts within 4 units ('(too far to mount)' otherwise); the shepherd lifts onto it and the hint reads 'Riding. Tools are put away. V to dismount.' Speed is x1.5 (x1.75 while the mount is perked) and sprinting adds only half of that bonus. Tools, the build menu and Interact are blocked; pressing E dismounts. Standing beside the mount, E shows '[E] Options' and opens its menu (or click it): 'Ride' / 'Dismount', a mood line, 'Feed (berry, favourite)' and 'Pet'. Riding is never saved: loading puts you on foot.");

            C("MOUNT.pout-perk", "Grudge pouts and perks",
                "'mount', 'mount content 20' (pout), try to ride, pet it, feed it berry, then feed it wheat. 'ff' 8 game hours without tending it.",
                "Contentment starts at 65 and, after 4 game hours with no ride, pet or feed, drains 2 per hour. Under 30 it pouts: sulks 5.5 units back with its back turned, says 'hmph.', '(it turns its back on you)', '(pointedly not looking)' or '*dramatic sigh*', and refuses a ride ('(it sulks and won't be ridden - feed or pet it)' plus a denied bleep). Pet: +25 ('<3', or '(it thaws, slightly)' while pouting), 20s cooldown ('(it has had enough fuss for now)'). Feed berry: +40, 'it perks right up!', perked for 3 game hours; other produce (wheat, bloom, reed, glowcap): +15, '(it deigns to eat)', perked 1 hour. Perked: ears up, hops, and rides at x1.75. 'mount' shows 'mount joined, pouting (20/100), ...'.");

            C("MOUNT.exclusive", "Never ascends, never conflicts",
                "Join the mount. 'spirits', 'ready', build the pad and interact, try 'Come along' on it, open the journal Residents and Legacy tabs. Press Z while riding and V while seated.",
                "Grudge is not a spirit: it is absent from 'spirits', ascension and Styx lists, weave lists, Repo-man targets, competitions and the journal. It cannot be asked to 'Come along' and never wants to ascend. Riding and sitting are mutually exclusive both ways: Z while riding does nothing, V while seated says '(stand up first)'.");

            C("MOUNT.persist", "Mount persists",
                "Join, feed it a berry, 'save', stop and start Play, 'mount'.",
                "Joined state, contentment, remaining perk hours, the last-tended stamp and its position all return; you load dismounted. No join scene replays. 'mount revoke' still works after a reload.");
        }

        private static void Economy()
        {
            Sec("Vendors & economy");

            C("VEND.trade", "Town vendor",
                "Walk to the Vendor stall ('tp 24 3', look around) and Trade. Hold wheat, berry, bloom, reed, glowcap, essence and fiber, including Fine and Gleaming ones ('quality fine').",
                "SELL rows only for held items (wheat 3, berry 4, bloom 5, reed 3, glowcap 7, essence 8, fiber 1; Fine pays x1.5 and Gleaming x2.5, rounded). BUY rows: seeds 2/3/4/4/3/5 (grass, palewheat, gravebloom, murkberry, reed, glowcap lily), Follow Treat 5, Watchlight 30, Road Lantern 25, Bell Charm 8, Bitter Bait 8, Sand (4 loads) 8, Mystery Seed 12 disabled. Coins update instantly; shortage shows '(needs N obols)'.");

            C("VEND.currency-sell-only", "Obols everywhere, vendors only",
                "Open every shop, office and the pouch HUD. Look for any shipping crate.",
                "Money is called obols everywhere the player reads it (never 'coin'). There is no shipping crate; selling happens only face-to-face at vendors.");

            C("VEND.blacksmith-arrive", "Blacksmith move-in",
                "Fresh save: till tiles one by one. Watch the town west of the vendor at tile 39 and 40.",
                "No forge stall before 40 tiles tilled. At the 40th a forge stall appears with a guide-light line and stays after reload. Feel: the Hoe is starting to feel slow at about that point.");

            C("VEND.blacksmith-shop", "Blacksmith tiers",
                "'coins 500', open the Blacksmith at levels 1, 3 and 6.",
                "Rows: Hoe, Water Pail, Shovel. Tier 2 costs 40 obols and level 3; tier 3 costs 100 and level 6. Under-level rows grey with 'needs level N'; maxed shows '(mastered)'. Purchase deducts exactly, applies immediately, persists.");

            C("VEND.merchant-stock", "Merchant stock",
                "On day 5 open the Merchant, close, 'save', 'load', reopen.",
                "3 rows: a discounted seed, a treat deal (4, or 3 for 11), 35% chance of a curio (15 obols, grants 'curio'). Same stock for the same day after reload.");

            C("ECON.seed-profit", "Farming is profitable",
                "Plant, harvest and sell each crop once; compare with seed price.",
                "Every crop returns more than its seed: palewheat 2x3=6 vs 3, gravebloom 5 vs 4, murkberry 3x4=12 per harvest vs 4 and regrows. Flag any crop with profit of 0 or less, or one crop that dominates.");

            C("ECON.progression", "Early pacing and obol sinks",
                "Play normally 3 game days; record coins per day in the note. Compare with the price list.",
                "Starter kit is 12 obols. First field (120) reachable in about 5-8 game days of normal play (DEV JUDGMENT; record real days). Sinks: 8 fields 1570, road 200, Reedmire 1080, Loom 60, Pad 25, Watchlight 30, tiers 420. Note where income stalls or runs away.");

            C("ECON.holding-bribe-costs", "Food-priced costs are payable",
                "Check the favored foods needed for reclaim (2x) and the Repo bribe against what you normally hold.",
                "Reclaim and bribe costs are affordable with ordinary harvests; none requires a food the player has no way to grow.");
        }

        private static void Onboarding()
        {
            Sec("Onboarding & guide light");

            C("ONB.fresh-start", "Fresh start kit and tool drip",
                "Delete save0.json (see the draft folder path in the export log) and start.",
                "Starts with Hands + Hoe only; kit of 6 grass seeds, 3 palewheat seeds, 12 obols; guide light visible with a HUD line. Water Pail arrives after the plant step, Hammer after naming, Shovel after the home step, each with a grant line.");

            C("ONB.steps", "Nine steps advance correctly",
                "Follow the guide: till, plant, water, sow grass, wait for the stir, keep the crops growing until it decides to stay, name, build a home, finish.",
                "Steps advance only on the right action; each shows an intro and a done line; the light points at the target each step; the send-off line 'Hours served...' plays; no step stalls with its goal met.");

            C("ONB.nudge-skip", "Nudge, guide and skip",
                "Idle 90 seconds on a step. Run 'guide'. Then 'skipguide'.",
                "One nudge line floats up once after 90s. 'guide' prints the objective. 'skipguide' completes silently and unlocks every tool.");

            C("ONB.persist", "Resume and legacy saves",
                "Quit at step 4 and reload. Also load a save that already has residents.",
                "Resumes at the same step and the kit is not granted twice. A save with residents completes silently and grants every tool.");

            C("ONB.scope", "Guide stays in its lane",
                "After the tutorial play 10 minutes including the Blacksmith arrival.",
                "Guide light only speaks for tutorial-type beats (vendor move-in, new system); no idle quips or nagging at any other time.");
        }

        private static void Saving()
        {
            Sec("Save / load");

            C("SAVE.pause-save", "Save, Save & Quit, relaunch",
                "Esc -> Save (check save0.json time). Esc -> Save & Quit, relaunch. Also quit while paused.",
                "Save logs '[SaveSystem] Saved N entries'. Relaunch loads automatically ('Loaded N entries') and starts unpaused with working input.");

            C("SAVE.roundtrip", "Full state round-trip",
                "Build rich state (coins, seeds, tiers, XP, parcels, plants, residents with homes, a headstone). Note the context block, save, restart.",
                "Day/time/season, coins and every inventory count, tools and tiers, Lv/XP, parcels, plant growth, residents (names, Spirit, hunger, home, stats), headstones, weeds and villain holes all match.");

            C("SAVE.load-dupes", "Console load does not duplicate",
                "Count spirits, plants, weeds, homes, headstones. Run 'load'. Recount.",
                "'load' mid-session restores without duplicating or leaving ghosts of anything. Counts are identical.");

            C("SAVE.mid-action", "Quit mid-action",
                "Quit while mid tool swing, holding a tool, carrying a headstone ghost, and placing a home ghost.",
                "Reload is clean: no stuck movement lock, no stuck placement ghost, no phantom item.");

            C("SAVE.mid-modal", "Quit with a modal open",
                "Quit with a shop, the journal, or a naming prompt open. Reload.",
                "Gameplay input is unblocked after reload. A resident awaiting a name exists and is nameable or has a default name (never nameless limbo).");

            C("SAVE.corrupt", "Corrupt save",
                "Truncate save0.json by half and start.",
                "Starts fresh with a warning ('Failed to read save'), no exception loop, no crash.");

            C("SAVE.known-gaps", "Known non-persistent things",
                "Place Watchlights, let essence motes drop, spawn weeds. Save and reload.",
                "Weeds persist. Uncollected essence motes may vanish (accepted). Watchlights should persist - the code comment says they do not yet; record what actually happens.");

            C("SAVE.settings", "Settings persist",
                "Change music and SFX volume, screen shake, Gentle Passage, rebind a key. Restart.",
                "Volumes, shake, text speed and rebinds persist (device prefs); Gentle Passage persists in the save.");

            C("SAVE.draft", "This QA tool's draft survives",
                "Set a few statuses, type a note, log a bug. Stop Play, start again, press F8.",
                "Everything (statuses, notes, bugs, filter, expanded sections) is restored from feedback_draft.json.");
        }

        private static void Interface()
        {
            Sec("UI, HUD, input");

            C("UI.hud-fit", "HUD and panels fit all sizes",
                "Game view at 1920x1080, 1280x720, then 21:9 and 4:3. Check every HUD element and open each modal.",
                "Clock, date button, Lv bar, tool HUD, pouch and objective line are visible, unoverlapped and legible. No panel is clipped. No missing-glyph boxes anywhere (legacy font).");

            C("UI.text-eats-keys", "Text boxes eat keys",
                "Open the console (backquote), the name box and a QA note box. Type W A S D E F J C B T R Z Tab Space F1.",
                "No gameplay or modal action fires while typing. Esc still pauses (record anything odd). F8 does not close the QA panel while typing.");

            C("UI.modal-stack", "Modal stacking",
                "Open J, then press C, T, B, R, F8. Open a shop then J. Open the pause menu over a modal.",
                "Only one modal at a time or cleanly layered; no key leaks into the world; each closes with its own control.");

            C("UI.modal-unblock", "Input returns after every modal",
                "Open and close in turn: journal, calendar, toolbelt, build, seed picker, vendor, blacksmith, merchant, land office, holding office, weave, inspector, options, controls, QA.",
                "After each close the shepherd moves and uses tools. No stuck block. If paused first, closing a modal leaves the pause menu in control.");

            C("UI.pause", "Pause behavior",
                "Esc during play, mid-swing, and with a modal open. Hold a key through resume.",
                "Pause freezes clock, camera, spirits and rain. Held keys do not leak after Resume. Options and Controls are reachable. Resume restores input exactly once.");

            C("UI.context-menu", "Click context menu",
                "Left-click a spirit, home, stall, plant, weed, headstone near the screen edges.",
                "Menu opens at the cursor, stays on screen, actions match the target (Soothe, Give treat, Inspect, Pull, Tamp, Move, Destroy two-step, Trade, Land deeds). Clicking away closes it. Far targets show '(too far to tend)'.");

            C("UI.hud-feedback", "Floating text, prompt, labels, banners",
                "Watch the interact prompt, world labels, floating text, alarm banner and pouch during a busy minute.",
                "Floating text is readable and capped at 12. The prompt matches the focused target and hides when none. World labels fade with distance without clutter. The pouch updates instantly. Nothing flashes on refresh.");

            C("UI.keys-in-ceremony", "J and T during ceremonies",
                "Start 'naming mausoleum' and, while the light descends and while the name field is up, press J, T, C, B, Tab, Z, V, F1 and WASD. Repeat during a Styx crossing and during the weave rite. After each ends, press J, T and walk.",
                "No journal, toolbelt, calendar, build menu, seat or ride opens or fires during any ceremony, and the world does not move you. Closing the journal or toolbelt never hands input back while a ceremony still owns it. When the ceremony ends J and T open normally and the shepherd walks.");

            C("UI.console-over-naming", "Console over the name field",
                "During the naming field open the console (backquote), run 'time 12', close it, and keep typing WASD in the name field. Confirm the name, then walk.",
                "The console never leaks keys into the world. Closing it hands the text field back its own typing lock (it remembers what the name prompt held), so WASD still only types letters and the shepherd stays blocked until the name is confirmed. After confirming, gameplay input is restored exactly once.");

            C("UI.pause-over-modal", "Pause and resume with a modal or ceremony open",
                "Open J, press Esc (pause menu), Resume. Repeat with the toolbelt, then with the console open. Then press Esc during a naming ceremony and during a Styx crossing.",
                "Resuming never unblocks input while another modal, a text field or a ceremony still owns it: after Resume the journal is still open and the shepherd cannot walk until you close J. During naming and the Styx crossing Esc is intercepted (it skips the flourish) and the pause menu does not stack on top of it. Every close leaves input working exactly once.");

            C("UI.controls", "Rebinding",
                "Pause -> Options -> Controls: rebind Interact, Sprint, Rest and Ride; bind a conflicting key; press Esc while listening.",
                "Rebinding works and persists after restart. Conflicts are handled or clearly flagged. Esc cancels listening. Pause and Console remain usable.");

            C("UI.qa-panel", "QA panel self-test",
                "Open F8 at 1280x720: scroll, collapse sections, filters, take a screenshot, log a bug, export.",
                "Panel fits the screen, scrolls to every row, buttons are reachable, the screenshot excludes the panel, export logs a full path and a new .md file appears.");
        }

        private static void Audio()
        {
            Sec("Audio");

            C("AUDIO.screech", "The screech bug",
                "Play normally with headphones at moderate volume; spam rain on/off, spirits, ceremonies. If a screech occurs press Ctrl+M, then run 'audio' and 'audio log'.",
                "No sustained squeal or painful peak anywhere. If it happens record the time, what you were doing and the AudioGuard diag file path (printed by 'audio' as 'diag file: ...', named audio_diag_*.log in the persistent data folder); Ctrl+M kills all audio and restores it.");

            C("AUDIO.guard-status", "Audio guard status and diag log",
                "Play a minute, then run 'audio', then 'audio log'. Open the diag file path it prints.",
                "'audio' prints 'buses: music=on ambience=on sfx=on voice=on', 'last 5 s: N accepted, M rejected', 'limiter: last loud peak 0.62 at t=12.3, session max 0.71' (or '(none yet)') and 'diag file: <path>'. 'audio log' prints the last 30 requests (bus, key, volume, accepted or the rejection reason). The diag file holds every request for the first 45 seconds, then only flood and limiter events for the rest of the session.");

            C("AUDIO.guard-limiter", "Rate limiter and output limiter",
                "Spam 'bleep click' as fast as you can type for 5 seconds, then 'resident mausoleum' about 12 times and 'blessing' to make them all chirp at once, then 'audio log'.",
                "Retriggers of one sound inside 0.06s are rejected, accepted one-shots are capped at 4 per frame, 8 per sound per second, 24 per second on the Sfx bus and 6 per second (0.12s apart) on the Voice bus, so 25 simultaneous chirps become a handful. Rejections show in 'audio log' with a reason and a flood (more than 8 requests of one sound in 1 second) logs one LogWarning with the call stack at most every 10 seconds. The master output limiter keeps the mix from clipping (a pre-limit peak at or above 0.85 is written to the diag file; over 1.0 also warns). Nothing is audibly harsh.");

            C("AUDIO.panic-key", "Ctrl+M panic",
                "Make noise (rain on, a few chirps), press Ctrl+M, run 'audio', press Ctrl+M again. Try Ctrl+M with the journal open and with a text field focused.",
                "Ctrl+M silences everything (guard kill plus listener volume 0): the log says 'AUDIO KILLED (Ctrl+M to restore)', 'audio' prints 'PANIC KILL is ON (Ctrl+M to restore)', and a second press restores sound ('Audio restored'). 'audio unmute' also clears it. The key is a direct read, so it is ignored while typing in a text field or with a modal open.");

            C("AUDIO.mute-solo", "Console bus mute and solo",
                "'audio mute voice', trigger chirps and a bleep. 'audio solo music'. 'audio mute all'. 'audio unmute'. Try 'audio mute banana'.",
                "'Voice muted.' silences spirit chirps only (SFX still play). 'audio solo music' prints 'Music soloed (audio unmute clears).' and everything else is silent; 'audio' shows 'music=on(SOLO)'. 'audio mute all' prints 'All buses muted.' 'audio unmute' prints 'All buses unmuted, solo and panic kill cleared.' A bad bus prints 'Unknown bus: banana (music|ambience|sfx|voice|all)'. Rejected plays show in 'audio log'.");

            C("AUDIO.bleeps", "Bleep kit",
                "Run 'bleep click', plant, harvest, feed, soothe, build, coin, alarm, ascend, weave, denied. Spam one quickly.",
                "Each is a distinct, short, soft sound at a sane level with no pops. Rapid repeats do not stack into noise (the guard rate-limits them).");

            C("AUDIO.music", "Ambient music bed",
                "Listen through a full day. Use 'time' and 'season'. Pause.",
                "Quiet generative pads and plucks; day brighter, night darker and sparser; seasons transpose the bed; crossfades have no gaps or pops; pausing makes it softer (not silent); it sits under the SFX.");

            C("AUDIO.rain", "Rain loop",
                "'rain on' and 'rain off' repeatedly, pause during rain, cross midnight in rain, save and reload in rain.",
                "Soft hiss loops seamlessly and stops with the rain. No doubled loops after toggling or loading. Pause softens or stops it.");

            C("AUDIO.rain-seam", "Rain loop seam click",
                "'rain on', turn the Ambient slider up, put on headphones and listen to at least three full loops (the loop is 3 seconds) with the shepherd standing still.",
                "No click, tick, pop or level jump once per loop: the 3 second hiss is cross-faded into itself, so the seam is inaudible at any volume.");

            C("AUDIO.volume-buses", "Volume sliders and buses",
                "Options: Music, SFX, Voice and Ambient rows (steps of 10%, 0 to 100). Set each to 0, then back; restart. Ambient moves the rain, Voice moves the spirit chirps.",
                "Levels change immediately and persist (Voice and Ambient are saved in device prefs; 0 is silent). Each slider moves only its own bus: Music = the ambient bed and sit theme, SFX = bleeps and footsteps, Voice = species chirps, Ambient = rain. The panel fits with all four rows.");

            C("AUDIO.footsteps", "Footsteps and leaf trail",
                "Walk, then sprint, over grass, dirt, scrub and sand (water is not walkable). Stand still, then walk 10 steps. 'audio mute sfx' mid-walk. 'audio log' for the key.",
                "One step per about 0.95 units of travel (so sprinting quickens the rhythm), alternating feet with slightly different pitch, 3 variants per surface: grass swish, dirt crunch, scrub rustle, sand hiss. Sprint is about 25% louder. Standing still is silent; the first step lands soon after setting off. Walking on grass leaves 2 small green leaf puffs per step. 'audio log' shows key footstep on the Sfx bus; muting Sfx silences them. No volume spike when many steps stack.");

            C("AUDIO.world-sfx", "Whistle and rest theme",
                "Watch the Repo-man approach ('repo runaway', 'repo skipwarn', 'repo'). Sit with Z for 30 seconds, then stand.",
                "The off-key whistle plays while the Repo-man walks (louder as he nears, silent beyond 22 units). The sit theme (a 24 second pentatonic loop) fades in over about 3s with the ambient bed ducking underneath, fades out over about 2.5s on standing, softens while paused and respects the music volume and 'audio mute music'.");

            C("AUDIO.ceremony-stings", "Ceremony audio",
                "Run a Styx crossing and a weave.",
                "Charon's chord, orb coin bleeps, farewell chorus and weave sound each fire once at a sane level and never overlap into noise.");
        }

        private static void Performance()
        {
            Sec("Performance");

            C("PERF.fps", "Frame rate in normal play",
                "Play 10 minutes with about 8 residents, rain on, a few plants. Read FPS avg/min in the context block.",
                "Average at least 60 FPS (note editor vs build), worst one-second window at least 30. No sustained drop in rain, crossing or with many spirits.");

            C("PERF.hitches", "Hitches",
                "Watch for freezes: first open of each modal, placing homes, weeds spawning, Save, scene start.",
                "No hitch above 100 ms in normal play (the hitch counter in the report stays near zero). First modal open and Save each under about 100 ms.");

            C("PERF.soak", "Long fast-forward soak",
                "'ff' for 10 real minutes, then read the error log in the report.",
                "No FPS decay or memory growth, spirit count stable, days advance without double-firing. Zero errors/exceptions; warnings limited and not repeating every frame.");
        }

        private static void NotBuilt()
        {
            Sec("Not built yet (wave 2+)");

            N("NB.flute-lantern-festivals", "Flute, festivals and races",
                "Look in vendors and the calendar board.",
                "A flute is purchasable and turns or calms spirits; scripted calendar events and festivals fire; competitions and races return hung off the calendar; season-gated silhouettes appear. (The road lantern is built, see ROAD.dark-lantern.)");

            C("NB.desert-species", "Desert-native species and rain shelter",
                "Run the menus AnimalFarm > Generate Placeholder Art, Generate Content Assets and Build Prairie Scene. 'affinity scorpse', 'staygate scorpse'. Buy Sand (vendor) and spread it over the home base until 'biome' reads Desert (40% sand), grow gravebloom, then 'resident scorpse Dune' and 'rain on'.",
                "Scorpse (bone-white scorpion) Loves Desert and dislikes Swamp ('affinity scorpse'). Its chain: Appear = sand 15% and a gravebloom growing; Visit = the home base is Desert and a gravebloom is ripe; Stay = Desert score 50+, 2 ripe gravebloom and water 3% of the field. Desert bases attract it (spawn weight Love 6) and it decides to stay on its own like the others. In rain it hurries to its home or the nearest building and waits ('ground' shows 'rain Shelters (under cover)'). Final wish: dig water near its home. Bansheep and Bogwick still Hard-No the Desert base.");

            C("NB.villain-season", "Villain visits weighted by season",
                "Type 'villain' (no argument) in each season: 'season 0', 'villain', 'season 1', 'villain', and so on. Also 'season' lists the current mix. To sample, jump to a season and run through several days with 'ff'/'day' and watch the alarm banners.",
                "'villain' prints this season's mix: The Hush Digger 20% / Devourer 20% / Scarer 60%; The Weep Digger 60% / Devourer 20% / Scarer 20%; The Smolder Digger 20% / Devourer 60% / Scarer 20%; The Long Dim Digger 20% / Devourer 20% / Scarer 60%. Over many visits each season mostly brings its favored kind (Weep = burrowing, Smolder = hungry prowler, Hush and Long Dim = dread). The overall visit rate, ward charms, Watchlight wards and damage caps are unchanged. Only the mix varies by season.");

            C("NB.competition-mothball", "Competition board mothball text",
                "On a non-event day ('festival clear'), walk up to the Competition Board and press E (or select it). Read the page, then Close. Jump seasons with 'season 2' and reopen it. Optionally 'compete' and 'compete race' in the console.",
                "The board opens a read-only 'Competition Notices' page: 'Entries open with the festival season.', today's date, 'Coming up' with the next 3 festival events (name, format Boulder Trial or Sprint, season and day, 'today' / 'tomorrow' / 'in N days') each with 3 rival shepherds, their spirit and a flavor line, and 'Your residents who could enter' listing your residents (up to 4, then '...and N more'). There is no entry button on this page; the only button is Close. The race is never called The Crossing. The events sort by what is soonest and the list moves when you change season. The prompt reads 'Read the notices'. The console 'compete' still runs a Boulder Trial and 'compete race' the sprint.");

            C("COMP.festival-entry", "Festival day: bring a spirit and enter",
                "Have a resident with Spirit 60 or more and use 'Come along' on it. Type 'festival now' (forces today to be an event day at any hour). Walk to the Competition Board. Open the menu with E. First try it with no follower (stand the spirit down), then with one, and with two following. Pick 'Enter <name>', choose a difficulty, press the Enter button. Afterwards press E at the board again, reopen the notices, then 'save' and 'load' and try again. 'festival reset' forgets the entry; 'festival clear' ends the forced day.",
                "The board prompt reads '[E] Options' when a spirit is following (an Enter row exists); with no follower the only real row is 'Read the notices', so the prompt reads '[E] Read the notices' and E opens the notices directly. The E menu shows the info row '(<event name> - entries open!)', then '(bring a spirit along to enter)' with no follower, or one 'Enter <name>' row per following resident, plus 'Read the notices'. 'Enter <name>' opens a page titled with the event, showing its format (Boulder Trial or Sprint), the spirit, your obols, three difficulty buttons and the fee (5 / 10 / 20 obols); too few obols shows '(needs N obols)' and stays open; paying closes the page and starts that format with that spirit. When it ends the spirit gets the usual morale swing and prize, and the board menu shows the result as an info row for the rest of the day ('<name> won the <event>!' or '<name> placed N/M in the <event>; <rival> took it.'); the notices page leads with 'Today's result: ...'. No second entry is possible that day, even after save and load. Without 'festival now', the same happens only on the scheduled season day between 08:00 and 18:00 (for example The Hush day 5, 'Hushstep Sprint'); on that day outside those hours the notices page says entries are open 8:00 to 18:00. The next day the board goes back to read-only notices. Closing the page restores movement and never unblocks input while pause or a ceremony still holds it.");
        }
    }
}
