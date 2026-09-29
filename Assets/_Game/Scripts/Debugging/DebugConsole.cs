using System.Collections.Generic;
using System.Globalization;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
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
                    Print("ff              - toggle fast-forward");
                    Print("tp <x> <y>      - teleport player");
                    Print("save            - save the game");
                    Print("load            - load the save");
                    Print("spawn           - (not yet)");
                    Print("inv             - list inventory items");
                    Print("grow            - force-mature all plants");
                    break;

                case "time":
                    CmdTime(args);
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
                    Print("No spawnables yet (slice 03).");
                    break;

                case "inv":
                    CmdInventory();
                    break;

                case "grow":
                    CmdGrow();
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

        private void CmdInventory()
        {
            if (Inventory.Instance == null) { Print("Inventory not available."); return; }

            var items = Inventory.Instance.All;
            if (items.Count == 0) { Print("(empty)"); return; }

            foreach (var pair in items)
                Print(pair.Key + " x " + pair.Value);
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

        private void Print(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxStoredLines)
                _lines.RemoveRange(0, _lines.Count - MaxStoredLines);
            _scroll.y = float.MaxValue; // keep pinned to the newest line
        }
    }
}
