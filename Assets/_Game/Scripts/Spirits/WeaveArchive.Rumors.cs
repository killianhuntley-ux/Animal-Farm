using System.Collections.Generic;
using System.Text;
using AnimalFarm.Core;
using AnimalFarm.Onboarding;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Rumor hints (muscle 06, verdict 2): the vendors and the guide-light
    /// occasionally drop a cryptic line pointing at an UNDISCOVERED recipe
    /// whose two ingredient species are both resident right now. At most one
    /// rumor per game-day (shared budget, persisted). Vendors speak when the
    /// player walks past their stall; the guide speaks through
    /// GuideMoments.Announce (never during the onboarding tutorial). Every
    /// rumor heard is kept for the journal's hint slot.
    /// </summary>
    public partial class WeaveArchive
    {
        private const float TickSeconds = 2f;            // real seconds between checks
        private const float RumorChancePerDay = 0.7f;    // "occasionally"
        private const float GuideShare = 0.4f;           // share of rumors the guide-light tells
        private const float VendorHearRadius = 3.5f;
        private const int MaxHeardPerRecipe = 3;

        private static readonly string[] VendorLines =
        {
            "Overheard: {a} and {b} share a thread. Don't ask which loom.",
            "Funny thing. {a} and {b} keep standing close. Something wants to be woven.",
            "A customer swore {a} plus {b} makes a third thing. Customers swear a lot.",
            "If I owned a loom, I'd put {a} next to {b}. I own a stall. It's fine.",
            "Between us? {a} and {b}. The loom does the rest. Allegedly.",
            "Pull the thread where {a} meets {b}. Gently. Everything bites a little."
        };

        private static readonly string[] GuideLines =
        {
            "{a} and {b} share a thread. Somebody should pull it.",
            "The loom hums when {a} and {b} stand near. Just saying.",
            "Two lights, {a} and {b}, cut from one cloth. Almost literally.",
            "Listen close: {a} and {b} together sound like a shuttle. A kind one.",
            "Some things only exist between two others. {a}. {b}. Think about it."
        };

        private static readonly Color VendorRumorColor = new Color(0.95f, 0.88f, 0.70f, 1f);

        private float _tick;
        private int _rolledDay = -1;
        private bool _armedToday;
        private bool _viaGuide;
        private float _guideHour;

        // ---- recipe state -------------------------------------------------------

        /// <summary>Recipe discovery = the cryptid species has been uncovered (weaving it makes it Resident).</summary>
        public static bool IsRecipeDiscovered(WeaveRecipe r)
        {
            var mgr = SpiritManager.Instance;
            return r == null || r.result == null || mgr == null
                || mgr.GetDiscovery(r.result.id) != SpiritManager.DiscoveryLevel.Unseen;
        }

        /// <summary>Undiscovered valid recipes whose ingredient species are both resident right now.</summary>
        public static List<WeaveRecipe> RumorCandidates()
        {
            var list = new List<WeaveRecipe>();
            var mgr = SpiritManager.Instance;
            if (mgr == null) return list;

            var recipes = mgr.Recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null || r.result == null || r.parentA == null || r.parentB == null) continue;
                if (IsRecipeDiscovered(r)) continue;

                int needA = r.parentA.id == r.parentB.id ? 2 : 1;
                if (mgr.CountResidents(r.parentA.id) < needA) continue;
                if (mgr.CountResidents(r.parentB.id) < 1) continue;
                list.Add(r);
            }
            return list;
        }

        public int HeardCount(string resultId)
        {
            int n = 0;
            for (int i = 0; i < _rumors.Count; i++)
                if (_rumors[i].resultId == resultId) n++;
            return n;
        }

        /// <summary>Rumors heard about a cryptid species, oldest first (journal hint slot).</summary>
        public List<RumorRecord> RumorsFor(string resultId)
        {
            var list = new List<RumorRecord>();
            for (int i = 0; i < _rumors.Count; i++)
                if (_rumors[i].resultId == resultId) list.Add(_rumors[i]);
            return list;
        }

        // ---- the daily check -------------------------------------------------------

        private void Update()
        {
            // Rumors tick on unscaled time: pausing must not let one fire (or the day roll) behind the menu.
            if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;

            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = TickSeconds;

            var clock = GameClock.Instance;
            if (clock == null || SpiritManager.Instance == null) return;

            // One roll per game-day: is there a rumor today, and who tells it?
            int day = clock.Day;
            if (day != _rolledDay)
            {
                _rolledDay = day;
                _armedToday = Random.value < RumorChancePerDay;
                _viaGuide = Random.value < GuideShare;
                _guideHour = Random.Range(9f, 18f);
            }
            if (!_armedToday || day <= _lastRumorDay) return;

            // Never talk over a modal, a ceremony, or the tutorial's single-line rule.
            if (UIInputLock.ModalOpen || NamingCeremony.Running || StyxCrossingCeremony.Running
                || WeaveRiteCeremony.Running) return;
            bool tutorial = OnboardingManager.Instance != null && !OnboardingManager.Instance.IsComplete;
            if (tutorial) _viaGuide = false;

            var recipe = PickRumorRecipe();
            if (recipe == null) return;

            if (_viaGuide)
            {
                if (clock.Hours < _guideHour) return;
                FireRumor(recipe, true, Vector3.zero, "The light");
            }
            else if (TryFindNearbyVendor(out string teller, out Vector3 pos))
            {
                FireRumor(recipe, false, pos, teller);
            }
        }

        /// <summary>Least-heard candidate first (random among ties); null when nothing is ready.</summary>
        private WeaveRecipe PickRumorRecipe()
        {
            var candidates = RumorCandidates();
            WeaveRecipe best = null;
            int bestHeard = int.MaxValue;
            int ties = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int heard = HeardCount(candidates[i].result.id);
                if (heard >= MaxHeardPerRecipe) continue;
                if (heard < bestHeard) { best = candidates[i]; bestHeard = heard; ties = 1; }
                else if (heard == bestHeard && Random.Range(0, ++ties) == 0) best = candidates[i];
            }
            return best;
        }

        // Stall lookups are cached and refreshed every StallRefreshSeconds (the scans
        // are scene-wide); stalls rarely appear or vanish, and the checks are 2 s apart.
        private const float StallRefreshSeconds = 10f;
        private readonly List<(Component stall, string who)> _stallCache = new List<(Component, string)>();
        private float _stallRefreshAt = -1f;

        private void RefreshStallCache()
        {
            // (the second test catches a clock restart: a refresh time far in the future is stale)
            if (Time.unscaledTime < _stallRefreshAt
                && Time.unscaledTime >= _stallRefreshAt - StallRefreshSeconds) return;
            _stallRefreshAt = Time.unscaledTime + StallRefreshSeconds;
            _stallCache.Clear();

            // (Charon/the Ferryman is deliberately NOT a rumor source: he appears only in the Styx crossing.)
            foreach (var s in FindObjectsByType<VendorStall>(FindObjectsSortMode.None))
                _stallCache.Add((s, "The vendor"));
            foreach (var s in FindObjectsByType<MerchantStall>(FindObjectsSortMode.None))
                _stallCache.Add((s, "The merchant"));
            foreach (var s in FindObjectsByType<BlacksmithStall>(FindObjectsSortMode.None))
                _stallCache.Add((s, "The blacksmith"));
        }

        private bool TryFindNearbyVendor(out string teller, out Vector3 pos)
        {
            teller = null;
            pos = Vector3.zero;

            var player = GameObject.FindWithTag("Player");
            if (player == null) return false;
            Vector3 p = player.transform.position;
            float best = VendorHearRadius * VendorHearRadius;
            string foundTeller = null;
            Vector3 foundPos = Vector3.zero;

            RefreshStallCache();
            for (int i = 0; i < _stallCache.Count; i++)
                ConsiderVendor(_stallCache[i].stall, _stallCache[i].who, p, ref best, ref foundTeller, ref foundPos);

            if (foundTeller == null) return false;
            teller = foundTeller;
            pos = foundPos;
            return true;
        }

        private static void ConsiderVendor(Component c, string who, Vector3 from, ref float best,
            ref string teller, ref Vector3 pos)
        {
            if (c == null) return;
            float d = (c.transform.position - from).sqrMagnitude;
            if (d >= best) return;
            best = d;
            teller = who;
            pos = c.transform.position;
        }

        /// <summary>
        /// Delivers one rumor (budget: marks today used) and records it for the
        /// journal. <paramref name="pos"/> is where a vendor's line appears.
        /// Also the debug entry point.
        /// </summary>
        public string FireRumor(WeaveRecipe recipe, bool viaGuide, Vector3 pos, string teller)
        {
            if (recipe == null || recipe.result == null) return null;

            string a = Lower(recipe.parentA), b = Lower(recipe.parentB);
            if (Random.value < 0.5f) { string t = a; a = b; b = t; }

            var pool = viaGuide ? GuideLines : VendorLines;
            string text = pool[Random.Range(0, pool.Length)].Replace("{a}", a).Replace("{b}", b);

            _lastRumorDay = GameClock.Instance != null ? GameClock.Instance.Day : _lastRumorDay;
            _rumors.Add(new RumorRecord { resultId = recipe.result.id, teller = teller, text = text });
            // Keep the journal slot bounded: oldest rumor for this recipe falls off.
            while (HeardCount(recipe.result.id) > MaxHeardPerRecipe)
            {
                int idx = _rumors.FindIndex(r => r.resultId == recipe.result.id);
                if (idx < 0) break;
                _rumors.RemoveAt(idx);
            }

            if (viaGuide) GuideMoments.Announce(text);
            else RumorBubble.Show(pos + Vector3.up * 1.7f, teller + ": \"" + text + "\"", VendorRumorColor);
            return teller + ": " + text;
        }

        private static string Lower(SpiritSpeciesDefinition s) =>
            s != null && !string.IsNullOrEmpty(s.displayName) ? s.displayName.ToLowerInvariant() : "something";

        /// <summary>Console hook: forget today's rumor so the daily check can fire again.</summary>
        public void Debug_ResetRumorBudget()
        {
            _lastRumorDay = -1;
            _rolledDay = -1;
        }

        public void Debug_ClearRumors() => _rumors.Clear();
    }

    /// <summary>
    /// A longer-lived, word-wrapped world line for vendor rumors (FloatingText
    /// lasts ~1s - too short for a sentence). Scaled time; fades over its last second.
    /// </summary>
    public static class RumorBubble
    {
        private const float Life = 6f;
        private const float FadeTail = 1.2f;
        private const int WrapAt = 34;

        public static void Show(Vector3 worldPos, string text, Color color)
        {
            var go = new GameObject("RumorBubble");
            go.transform.position = worldPos;

            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = Wrap(text, WrapAt);
            mesh.characterSize = 0.06f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = mesh.font.material;
                renderer.sortingOrder = 500;
            }

            go.AddComponent<Runner>().Init(mesh, color);
        }

        private static string Wrap(string text, int width)
        {
            var sb = new StringBuilder(text.Length + 8);
            int lineLen = 0;
            foreach (var word in text.Split(' '))
            {
                if (lineLen > 0 && lineLen + 1 + word.Length > width)
                {
                    sb.Append('\n');
                    lineLen = 0;
                }
                if (lineLen > 0) { sb.Append(' '); lineLen++; }
                sb.Append(word);
                lineLen += word.Length;
            }
            return sb.ToString();
        }

        private class Runner : MonoBehaviour
        {
            private TextMesh _mesh;
            private Color _color;
            private Vector3 _start;
            private float _elapsed;

            public void Init(TextMesh mesh, Color color)
            {
                _mesh = mesh;
                _color = color;
                _start = transform.position;
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;
                transform.position = _start + Vector3.up * (0.35f * Mathf.Clamp01(_elapsed / Life));
                if (_mesh != null)
                {
                    float fade = Mathf.Clamp01((Life - _elapsed) / FadeTail);
                    _mesh.color = new Color(_color.r, _color.g, _color.b, _color.a * fade);
                }
                if (_elapsed >= Life) Destroy(gameObject);
            }
        }
    }
}
