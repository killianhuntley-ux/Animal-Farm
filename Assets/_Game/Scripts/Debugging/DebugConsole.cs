using System.Collections.Generic;
using System.Globalization;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Onboarding;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Minimal IMGUI developer console. Toggled via GameInput.ConsoleToggled.
    /// While open it blocks gameplay input; on close it unblocks only if the
    /// game isn't paused (the pause menu owns the block then).
    /// </summary>
    public class DebugConsole : MonoBehaviour
    {
        private const int MaxStoredLines = 100;
        private const int VisibleLines = 12;
        private const string InputControlName = "DebugConsoleInput";

        private bool _open;
        private string _input = "";
        private readonly List<string> _lines = new List<string>();
        private Vector2 _scroll;
        private bool _wantsFocus;
        private bool _subscribed;

        private void Start()
        {
            if (GameInput.Instance != null)
            {
                GameInput.Instance.ConsoleToggled += OnConsoleToggled;
                _subscribed = true;
            }

            Print("Debug console. Type 'help' for commands.");
        }

        private void OnDestroy()
        {
            if (_subscribed && GameInput.Instance != null)
                GameInput.Instance.ConsoleToggled -= OnConsoleToggled;
        }

        private void OnConsoleToggled()
        {
            _open = !_open;
            UIInputLock.TextInputActive = _open; // console keystrokes must never reach gameplay

            if (GameInput.Instance != null)
            {
                if (_open)
                {
                    GameInput.Instance.SetGameplayBlocked(true);
                }
                else
                {
                    // Don't hand input back if the pause menu still needs it blocked.
                    bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                    if (!paused) GameInput.Instance.SetGameplayBlocked(false);
                }
            }

            if (_open)
            {
                _input = "";
                _wantsFocus = true;
                _scroll.y = float.MaxValue;
            }
        }

        private void OnGUI()
        {
            if (!_open) return;

            float width = Screen.width * 0.5f;
            float lineHeight = 20f;
            float logHeight = VisibleLines * lineHeight;
            float inputHeight = 24f;
            float pad = 8f;
            float boxHeight = logHeight + inputHeight + pad * 3f;

            var boxRect = new Rect(0f, 0f, width, boxHeight);
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(boxRect, Texture2D.whiteTexture);
            GUI.color = prevColor;
            GUI.Box(boxRect, GUIContent.none);

            // Catch Enter before the TextField consumes the event.
            var e = Event.current;
            bool submit = e.type == EventType.KeyDown
                          && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                          && GUI.GetNameOfFocusedControl() == InputControlName;
            if (submit) e.Use();

            // Output log (scrollable, pinned to the tail).
            var logOuter = new Rect(pad, pad, width - pad * 2f, logHeight);
            float contentHeight = Mathf.Max(logHeight, _lines.Count * lineHeight);
            var logInner = new Rect(0f, 0f, width - pad * 4f, contentHeight);

            _scroll = GUI.BeginScrollView(logOuter, _scroll, logInner);
            for (int i = 0; i < _lines.Count; i++)
            {
                GUI.Label(new Rect(4f, i * lineHeight, logInner.width - 8f, lineHeight), _lines[i]);
            }
            GUI.EndScrollView();

            // Input field.
            GUI.SetNextControlName(InputControlName);
            _input = GUI.TextField(
                new Rect(pad, pad * 2f + logHeight, width - pad * 2f, inputHeight),
                _input);

            if (_wantsFocus)
            {
                GUI.FocusControl(InputControlName);
                _wantsFocus = false;
            }

            if (submit)
            {
                Submit(_input);
                _input = "";
                _wantsFocus = true;
            }
        }

        // ---------------------------------------------------------- Commands

        private void Submit(string raw)
        {
            string line = raw == null ? "" : raw.Trim();
            if (line.Length == 0) return;

            Print("> " + line);

            string[] args = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            string cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help":
                    Print("help            - list commands");
                    Print("time <hours>    - set time of day (0-24)");
                    Print("day [n]         - show the date, or jump to absolute day n");
                    Print("season [n]      - list seasons, or jump to season n (keeps day-of-season)");
                    Print("rain [on|off]   - force/toggle rain for today");
                    Print("ff              - toggle fast-forward");
                    Print("tp <x> <y>      - teleport player");
                    Print("save            - save the game");
                    Print("load            - load the save");
                    Print("spawn <id>      - force-spawn a spirit as visitor");
                    Print("spirits         - list all spirit agents");
                    Print("inv             - list inventory items");
                    Print("coins [n]       - add coins (default 50)");
                    Print("grow            - force-mature all plants");
                    Print("weed            - force-spawn a weed near the player");
                    Print("weeds           - list live weeds (age and cell)");
                    Print("villain <kind>  - force a villain visit (digger|devourer|scarer)");
                    Print("blessing [name] - fill a resident's spirit (name or 'all'; default all)");
                    Print("taskdone [name] - complete a resident's final wish (name or 'all')");
                    Print("resident <id> [name] - instantly spawn a named RESIDENT of a species");
                    Print("ready [name]    - make resident(s) ascension-ready (spirit+task+home)");
                    Print("compete [0|1|2] - enter the first resident in a Boulder Trial");
                    Print("weave           - weave the first recipe-matching resident pair");
                    Print("gentle [on|off] - set/toggle Gentle Passage (disables the Repo-man)");
                    Print("repo            - dispatch the Repo-man at a runaway now");
                    Print("parcel <0|1>    - force-open a land parcel (no cost; bypasses the Ferryman)");
                    Print("guide           - show the current guide-light objective");
                    Print("skipguide       - skip the guide-light onboarding");
                    Print("bleep [kind]    - audio self-test; play a bleep (default Click)");
                    Print("tooltier <tool> <1-3> - set a tool's upgrade tier (e.g. tooltier Hoe 3)");
                    break;

                case "time":
                    CmdTime(args);
                    break;

                case "day":
                    CmdDay(args);
                    break;

                case "season":
                    CmdSeason(args);
                    break;

                case "rain":
                    CmdRain(args);
                    break;

                case "tooltier":
                    CmdToolTier(args);
                    break;

                case "ff":
                    CmdFastForward();
                    break;

                case "tp":
                    CmdTeleport(args);
                    break;

                case "save":
                    if (SaveSystem.Instance == null) { Print("SaveSystem not available."); break; }
                    SaveSystem.Instance.Save();
                    Print("Saved.");
                    break;

                case "load":
                    if (SaveSystem.Instance == null) { Print("SaveSystem not available."); break; }
                    Print(SaveSystem.Instance.TryLoad() ? "Loaded." : "No save to load.");
                    break;

                case "spawn":
                    CmdSpawn(args);
                    break;

                case "spirits":
                    CmdSpirits();
                    break;

                case "inv":
                    CmdInventory();
                    break;

                case "coins":
                    CmdCoins(args);
                    break;

                case "grow":
                    CmdGrow();
                    break;

                case "weed":
                    CmdWeed();
                    break;

                case "weeds":
                    CmdWeeds();
                    break;

                case "villain":
                    CmdVillain(args);
                    break;

                case "blessing":
                    CmdBlessing(args);
                    break;

                case "taskdone":
                    CmdTaskDone(args);
                    break;

                case "resident":
                    CmdResident(args);
                    break;

                case "ready":
                    CmdReady(args);
                    break;

                case "compete":
                    CmdCompete(args);
                    break;

                case "weave":
                    CmdWeave();
                    break;

                case "gentle":
                    CmdGentle(args);
                    break;

                case "repo":
                    CmdRepo();
                    break;

                case "parcel":
                    CmdParcel(args);
                    break;

                case "guide":
                    CmdGuide();
                    break;

                case "skipguide":
                    CmdSkipGuide();
                    break;

                case "bleep":
                    CmdBleep(args);
                    break;

                default:
                    Print("Unknown: " + cmd + " (try 'help')");
                    break;
            }
        }

        private void CmdTime(string[] args)
        {
            if (GameClock.Instance == null) { Print("GameClock not available."); return; }
            if (args.Length < 2
                || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hours))
            {
                Print("Usage: time <hours 0-24>");
                return;
            }

            hours = Mathf.Clamp(hours, 0f, 24f);
            GameClock.Instance.SetTimeHours(hours);
            Print("Time set to " + hours.ToString("0.##", CultureInfo.InvariantCulture) + "h.");
        }

        private void CmdDay(string[] args)
        {
            if (GameClock.Instance == null) { Print("GameClock not available."); return; }

            if (args.Length >= 2)
            {
                if (!int.TryParse(args[1], out int day) || day < 1)
                {
                    Print("Usage: day [n >= 1]");
                    return;
                }
                GameClock.Instance.SetDay(day);
            }

            var calendar = GameCalendar.Instance;
            Print("Day " + GameClock.Instance.Day
                  + (calendar != null ? " (" + calendar.DateLine + ")" : " (no calendar)"));
        }

        private void CmdSeason(string[] args)
        {
            var calendar = GameCalendar.GetOrCreate();
            if (calendar == null) { Print("GameCalendar not available."); return; }

            if (args.Length < 2)
            {
                for (int i = 0; i < calendar.SeasonCount; i++)
                {
                    var def = calendar.GetSeason(i);
                    Print(i + ": " + def.name
                          + " (rain " + Mathf.RoundToInt(def.rainWeight * 100f) + "%)"
                          + (i == calendar.SeasonIndex ? "  <- now" : ""));
                }
                return;
            }

            if (!int.TryParse(args[1], out int index)
                || index < 0 || index >= calendar.SeasonCount)
            {
                Print("Usage: season [0-" + (calendar.SeasonCount - 1) + "]");
                return;
            }

            calendar.Debug_SetSeason(index);
            Print("Now " + calendar.DateLine + ".");
        }

        private void CmdRain(string[] args)
        {
            var weather = WeatherManager.GetOrCreate();
            if (weather == null) { Print("WeatherManager not available."); return; }

            bool value;
            if (args.Length >= 2)
            {
                string arg = args[1].ToLowerInvariant();
                if (arg == "on") value = true;
                else if (arg == "off") value = false;
                else { Print("Usage: rain [on|off]"); return; }
            }
            else
            {
                value = !weather.IsRainDay;
            }

            weather.Debug_ForceRain(value);
            Print("Rain " + (value ? "ON" : "OFF") + " for today (re-rolls at dawn).");
        }

        private void CmdToolTier(string[] args)
        {
            // "Water Pail" is two words: the LAST arg is the tier, everything
            // between the command and it is the tool name.
            if (args.Length < 3
                || !int.TryParse(args[args.Length - 1], out int tier))
            {
                Print("Usage: tooltier <tool> <1-3> (e.g. tooltier Water Pail 2)");
                return;
            }

            var tools = FindFirstObjectByType<AnimalFarm.Player.ToolController>();
            if (tools == null) { Print("ToolController not available."); return; }

            string name = string.Join(" ", args, 1, args.Length - 2);
            if (!tools.SetToolTier(name, tier))
            {
                Print("Unknown tool '" + name + "'. Tools: Hands, Hoe, Water Pail, Shovel, Hammer.");
                return;
            }

            Print(name + " is now tier " + tools.GetToolTier(name) + ".");
        }

        private void CmdFastForward()
        {
            if (GameClock.Instance == null) { Print("GameClock not available."); return; }
            GameClock.Instance.FastForward = !GameClock.Instance.FastForward;
            Print("Fast-forward " + (GameClock.Instance.FastForward ? "ON" : "OFF") + ".");
        }

        private void CmdTeleport(string[] args)
        {
            if (args.Length < 3
                || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Print("Usage: tp <x> <y>");
                return;
            }

            var player = GameObject.FindWithTag("Player");
            if (player == null) { Print("No GameObject tagged 'Player'."); return; }

            var pos = new Vector3(x, y, 0f);
            player.transform.position = pos;

            var body = player.GetComponent<Rigidbody2D>();
            if (body != null) body.position = new Vector2(x, y);

            Print("Teleported player to (" + x + ", " + y + ").");
        }

        private void CmdSpawn(string[] args)
        {
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }

            if (args.Length < 2)
            {
                Print("Usage: spawn <speciesId>");

                var known = SpiritManager.Instance.KnownSpecies;
                if (known == null || known.Count == 0) { Print("(no species defined)"); return; }

                var ids = new List<string>();
                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i] != null && !string.IsNullOrEmpty(known[i].id))
                        ids.Add(known[i].id);
                }
                Print("Known: " + string.Join(", ", ids));
                return;
            }

            string id = args[1];
            var agent = SpiritManager.Instance.ForceSpawn(id);
            Print(agent != null ? "Spawned " + id + " as visitor." : "Unknown species: " + id);
        }

        private void CmdSpirits()
        {
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }

            var spirits = SpiritManager.Instance.AllSpirits;
            if (spirits == null || spirits.Count == 0) { Print("(no spirits)"); return; }

            for (int i = 0; i < spirits.Count; i++)
            {
                var agent = spirits[i];
                if (agent == null) continue;

                string name = !string.IsNullOrEmpty(agent.GivenName)
                    ? agent.GivenName
                    : (agent.Species != null ? agent.Species.displayName : "?");
                Print(name + " - " + agent.State + " - Spirit " + Mathf.RoundToInt(agent.Spirit) + "%");
            }
        }

        private void CmdInventory()
        {
            if (Inventory.Instance == null) { Print("Inventory not available."); return; }

            var items = Inventory.Instance.All;
            if (items.Count == 0) { Print("(empty)"); return; }

            foreach (var pair in items)
                Print(pair.Key + " x " + pair.Value);
        }

        private void CmdCoins(string[] args)
        {
            if (Inventory.Instance == null) { Print("Inventory not available."); return; }

            int amount = 50;
            if (args.Length >= 2 && (!int.TryParse(args[1], out amount) || amount <= 0))
            {
                Print("Usage: coins [n]");
                return;
            }

            Inventory.Instance.Add("coin", amount);
            Print("Added " + amount + " coin(s). Total: " + Inventory.Instance.Count("coin") + ".");
        }

        private void CmdGrow()
        {
            if (PlantManager.Instance == null) { Print("PlantManager not available."); return; }

            var plants = PlantManager.Instance.AllPlants;
            int grown = 0;
            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i] == null || plants[i].IsMature) continue;
                plants[i].ForceMature();
                grown++;
            }
            Print("Force-matured " + grown + " plant(s).");
        }

        private void CmdWeed()
        {
            if (WeedManager.Instance == null) { Print("WeedManager not available."); return; }

            var weed = WeedManager.Instance.ForceSpawnNearPlayer();
            Print(weed != null
                ? "Weed spawned at cell (" + weed.Cell.x + ", " + weed.Cell.y + ")."
                : "No sproutable cell near the player (water/plants/homes everywhere?).");
        }

        private void CmdWeeds()
        {
            if (WeedManager.Instance == null) { Print("WeedManager not available."); return; }

            var weeds = WeedManager.Instance.AllWeeds;
            int live = 0;
            if (weeds != null)
            {
                for (int i = 0; i < weeds.Count; i++)
                {
                    var w = weeds[i];
                    if (w == null) continue;
                    live++;

                    float age = GameClock.Instance != null
                        ? GameClock.Instance.TotalHours - w.SpawnedAtTotalHours
                        : 0f;
                    Print("weed at (" + w.Cell.x + ", " + w.Cell.y + ") - "
                          + (w.IsMature ? "MATURE" : "young") + " - "
                          + age.ToString("0.#", CultureInfo.InvariantCulture) + "h old");
                }
            }
            Print(live == 0 ? "(no weeds)" : live + " weed(s) live.");
        }

        private void CmdVillain(string[] args)
        {
            var manager = VillainManager.GetOrCreate();
            if (manager == null) { Print("VillainManager not available."); return; }

            if (args.Length < 2)
            {
                Print("Usage: villain <digger|devourer|scarer>");
                return;
            }

            VillainKind kind;
            switch (args[1].ToLowerInvariant())
            {
                case "digger":   kind = VillainKind.Digger; break;
                case "devourer": kind = VillainKind.Devourer; break;
                case "scarer":   kind = VillainKind.Scarer; break;
                default:
                    Print("Unknown villain: " + args[1] + " (digger|devourer|scarer)");
                    return;
            }

            Print(manager.Debug_ForceVisit(kind)
                ? "A " + kind + " slips onto the farm."
                : "Spawn refused (a villain is already visiting, or no terrain).");
        }

        private void CmdBlessing(string[] args)
        {
            int count = ForEachResident(args, agent => agent.Debug_SetSpirit(100f));
            if (count >= 0) Print("Blessed " + count + " resident(s).");
        }

        private void CmdTaskDone(string[] args)
        {
            int count = ForEachResident(args, agent => agent.Debug_CompleteTask());
            if (count >= 0) Print("Completed the wish of " + count + " resident(s).");
        }

        /// <summary>
        /// Applies an action to residents matched by args[1]: a given name
        /// (case-insensitive), or "all"/omitted for every resident. Returns the
        /// number affected, or -1 if the SpiritManager is missing.
        /// </summary>
        private int ForEachResident(string[] args, System.Action<SpiritAgent> apply)
        {
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return -1; }

            string filter = args.Length >= 2 ? args[1] : "all";
            bool all = filter.Equals("all", System.StringComparison.OrdinalIgnoreCase);

            var spirits = SpiritManager.Instance.AllSpirits;
            int count = 0;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent == null || agent.State != SpiritState.Resident) continue;
                    if (!all && !filter.Equals(agent.GivenName, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    apply(agent);
                    count++;
                }
            }
            return count;
        }

        private void CmdResident(string[] args)
        {
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }
            if (args.Length < 2)
            {
                Print("Usage: resident <speciesId> [name]");
                return;
            }

            string name = args.Length >= 3 ? args[2] : "";
            var agent = SpiritManager.Instance.ForceSpawnResident(args[1].ToLowerInvariant(), name);
            Print(agent != null
                ? "Resident " + agent.GivenName + " (" + args[1] + ") spawned."
                : "Unknown species: " + args[1]);
        }

        private void CmdReady(string[] args)
        {
            int count = ForEachResident(args, agent =>
            {
                agent.Debug_SetSpirit(100f);
                agent.Debug_CompleteTask();

                // Give the homeless a home next to them; the agent's slow tick
                // claims it within a couple of seconds.
                if (!agent.HasHome && HomeManager.Instance != null
                    && TerrainGrid.Instance != null && agent.Species != null)
                {
                    for (int dx = -2; dx <= 2 && !agent.HasHome; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (!TerrainGrid.Instance.TryWorldToCell(
                                agent.transform.position + new Vector3(dx, dy, 0), out var cell))
                            continue;
                        if (HomeManager.Instance.PlaceHome(agent.Species, cell) != null)
                            break;
                    }
                }
            });
            if (count >= 0)
                Print(count + " resident(s) made ascension-ready (home claims within ~2s).");
        }

        private void CmdCompete(string[] args)
        {
            var manager = AnimalFarm.Competitions.CompetitionManager.GetOrCreate();
            if (manager == null) { Print("CompetitionManager not available."); return; }
            if (manager.EventRunning) { Print("An event is already running."); return; }
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }

            int difficulty = 0;
            if (args.Length >= 2
                && (!int.TryParse(args[1], out difficulty) || difficulty < 0 || difficulty > 2))
            {
                Print("Usage: compete [0|1|2]");
                return;
            }

            SpiritAgent entrant = null;
            var spirits = SpiritManager.Instance.AllSpirits;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent != null && agent.State == SpiritState.Resident) { entrant = agent; break; }
                }
            }
            if (entrant == null) { Print("No resident spirits to enter."); return; }

            AnimalFarm.UI.CompetitionEntryUI.LastDifficulty = difficulty;
            manager.StartBoulderTrial(entrant, difficulty);

            string name = !string.IsNullOrEmpty(entrant.GivenName)
                ? entrant.GivenName
                : (entrant.Species != null ? entrant.Species.displayName : "Spirit");
            Print("Entered " + name + " in the Boulder Trial (difficulty " + difficulty + ").");
        }

        private void CmdWeave()
        {
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }

            var loom = FindFirstObjectByType<TheLoom>();
            if (loom == null) { Print("No Loom in the scene."); return; }

            var recipes = SpiritManager.Instance.Recipes;
            if (recipes == null || recipes.Count == 0) { Print("No weave recipes defined."); return; }

            // First two DISTINCT residents whose species pair matches any recipe.
            SpiritAgent first = null, second = null;
            var spirits = SpiritManager.Instance.AllSpirits;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count && second == null; i++)
                {
                    var a = spirits[i];
                    if (a == null || a.State != SpiritState.Resident) continue;
                    for (int j = i + 1; j < spirits.Count; j++)
                    {
                        var b = spirits[j];
                        if (b == null || b.State != SpiritState.Resident) continue;
                        if (SpiritManager.Instance.FindRecipe(a.Species, b.Species) == null) continue;
                        first = a;
                        second = b;
                        break;
                    }
                }
            }

            if (first == null || second == null)
            {
                Print("No resident pair matches any weave recipe.");
                return;
            }

            first.Debug_SetSpirit(100f);
            second.Debug_SetSpirit(100f);

            string nameA = !string.IsNullOrEmpty(first.GivenName)
                ? first.GivenName
                : (first.Species != null ? first.Species.displayName : "?");
            string nameB = !string.IsNullOrEmpty(second.GivenName)
                ? second.GivenName
                : (second.Species != null ? second.Species.displayName : "?");

            loom.RunWeave(first, second);
            Print("Weaving " + nameA + " + " + nameB + " (both spirits set to 100).");
        }

        private void CmdGentle(string[] args)
        {
            if (GameSettings.Instance == null) { Print("GameSettings not available."); return; }

            bool value;
            if (args.Length >= 2)
            {
                string arg = args[1].ToLowerInvariant();
                if (arg == "on") value = true;
                else if (arg == "off") value = false;
                else { Print("Usage: gentle [on|off]"); return; }
            }
            else
            {
                value = !GameSettings.Instance.GentlePassage;
            }

            GameSettings.Instance.GentlePassage = value;
            Print("Gentle Passage " + (value ? "ON (the Repo-man stays home)." : "OFF."));
        }

        private void CmdRepo()
        {
            if (RepoManManager.Instance == null) { Print("RepoManManager not available."); return; }
            if (SpiritManager.Instance == null) { Print("SpiritManager not available."); return; }

            SpiritAgent runaway = null;
            var spirits = SpiritManager.Instance.AllSpirits;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent != null && agent.State == SpiritState.Runaway) { runaway = agent; break; }
                }
            }

            if (runaway == null)
            {
                Print("No runaways. (Neglect someone first, or lower spirit with the info panel.)");
                return;
            }

            Print(RepoManManager.Instance.Debug_DispatchNow()
                ? "The Repo-man has been dispatched."
                : "Dispatch refused (already active, or Gentle Passage is on).");
        }

        private void CmdParcel(string[] args)
        {
            if (ParcelManager.Instance == null) { Print("ParcelManager not available."); return; }

            int count = ParcelManager.Instance.ParcelCount;
            if (args.Length < 2 || !int.TryParse(args[1], out int index)
                || index < 0 || index >= count)
            {
                Print("Usage: parcel <0-" + (count - 1) + ">");
                return;
            }

            if (ParcelManager.Instance.IsUnlocked(index))
            {
                Print("Parcel " + index + " is already open.");
                return;
            }

            Print(ParcelManager.Instance.Debug_Unlock(index)
                ? "Parcel " + index + " force-opened (no cost)."
                : "Could not open parcel " + index + ".");
        }

        private void CmdGuide()
        {
            if (OnboardingManager.Instance == null) { Print("OnboardingManager not available."); return; }
            Print(OnboardingManager.Instance.IsComplete
                ? "complete"
                : OnboardingManager.Instance.CurrentObjective);
        }

        /// <summary>
        /// Audio self-test: plays a bleep straight through Bleeps, isolating
        /// clip synthesis and the audio host from any event wiring. No arg
        /// plays Click at full volume; an arg matches a BleepKind name
        /// case-insensitively.
        /// </summary>
        private void CmdBleep(string[] args)
        {
            if (Bleeps.Muted) Print("(note: Bleeps.Muted is ON -- nothing will be audible)");

            if (args.Length < 2)
            {
                Bleeps.Play(BleepKind.Click);
                Print("Played Click at full volume.");
                return;
            }

            if (System.Enum.TryParse(args[1], true, out BleepKind kind))
            {
                Bleeps.Play(kind);
                Print("Played " + kind + " at full volume.");
            }
            else
            {
                Print("Unknown kind: " + args[1]);
                Print("Kinds: " + string.Join(", ", System.Enum.GetNames(typeof(BleepKind))));
            }
        }

        private void CmdSkipGuide()
        {
            if (OnboardingManager.Instance == null) { Print("OnboardingManager not available."); return; }
            if (OnboardingManager.Instance.IsComplete) { Print("Onboarding already complete."); return; }
            OnboardingManager.Instance.Skip();
            Print("Guide-light onboarding skipped.");
        }

        private void Print(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxStoredLines)
                _lines.RemoveRange(0, _lines.Count - MaxStoredLines);
            _scroll.y = float.MaxValue; // keep pinned to the newest line
        }
    }
}
