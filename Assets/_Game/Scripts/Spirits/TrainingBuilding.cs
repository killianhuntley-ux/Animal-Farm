using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>Static definition of one buildable training structure (muscle 05).</summary>
    public sealed class TrainingSpec
    {
        public string id, displayName, sub;
        public SpiritStat stat;
        public int cost;            // obols
        public float scale;         // world scale of the generated sprite
        public float ticksPerSession;
    }

    /// <summary>
    /// A passive-use training structure (muscle 05, verdict 3): spirits wander
    /// here on their own (SpiritAgent.TryTrainingTarget, driven by trait /
    /// species / bait appetite), do a short session and gain slow progress in
    /// the structure's stat, capped at the species max. No player drill.
    /// A food-bait slot (click the building) pulls in species that favor that
    /// food and sweetens their gains. Sprites are GENERATED in code (Ascension
    /// Pad pattern), so no bootstrapper wiring is needed.
    /// </summary>
    public class TrainingBuilding : MonoBehaviour, ISelectable
    {
        public const float SessionSeconds = 8f;
        public const int Capacity = 2;
        public const float UseRadius = 1.9f;
        public const int MaxBaitCharges = 6;
        public const int ChargesPerFood = 2;
        public const float BaitAttraction = 3f;   // appetite multiplier for a matching-bait species
        public const float BaitTickMul = 1.5f;    // session gain multiplier for a matching-bait species
        private const float BaseAppetite = 0.3f;  // chance per "feel like training?" check, before multipliers

        public static readonly TrainingSpec[] Specs =
        {
            new TrainingSpec { id = "stones", displayName = "Heavy Stones", stat = SpiritStat.Vigor,
                cost = 30, scale = 1.2f, ticksPerSession = 1f,
                sub = "lug, heave, repeat (trains Vigor)" },
            new TrainingSpec { id = "hurdles", displayName = "Hurdle Run", stat = SpiritStat.Grace,
                cost = 30, scale = 1.2f, ticksPerSession = 1f,
                sub = "hop-hop-hop (trains Grace)" },
            new TrainingSpec { id = "mirror", displayName = "Gleam Mirror", stat = SpiritStat.Gleam,
                cost = 40, scale = 1.2f, ticksPerSession = 1f,
                sub = "a pool of borrowed reflection (trains Gleam)" }
        };

        public static TrainingSpec FindSpec(string id)
        {
            for (int i = 0; i < Specs.Length; i++)
                if (Specs[i].id == id) return Specs[i];
            return null;
        }

        private static readonly List<TrainingBuilding> _all = new List<TrainingBuilding>();

        /// <summary>Every live training building.</summary>
        public static IReadOnlyList<TrainingBuilding> All => _all;

        public static bool AnyAtCell(Vector2Int cell)
        {
            var grid = AnimalFarm.World.TerrainGrid.Instance;
            if (grid == null) return false;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i] == null) continue;
                if (grid.TryWorldToCell(_all[i].transform.position, out var c) && c == cell) return true;
            }
            return false;
        }

        private sealed class Session
        {
            public SpiritAgent agent;
            public float elapsed;
            public float pulseIn;
        }

        public TrainingSpec Spec { get; private set; }
        /// <summary>Food id currently in the bait slot ("" = empty).</summary>
        public string BaitId { get; private set; } = "";
        /// <summary>Sessions of bait left.</summary>
        public int BaitCharges { get; private set; }

        private readonly List<Session> _sessions = new List<Session>();
        private Transform _body;
        private float _scanTimer;
        private bool _confirmingDestroy;

        // ---- factory ---------------------------------------------------------------

        public static TrainingBuilding Create(TrainingSpec spec, Vector3 pos, string baitId = "", int baitCharges = 0)
        {
            if (spec == null) return null;
            var go = new GameObject("Training_" + spec.id);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var b = go.AddComponent<TrainingBuilding>();
            b.Init(spec, baitId, baitCharges);
            return b;
        }

        private void Init(TrainingSpec spec, string baitId, int baitCharges)
        {
            Spec = spec;
            BaitId = baitCharges > 0 && baitId != null ? baitId : "";
            BaitCharges = Mathf.Clamp(baitCharges, 0, MaxBaitCharges);

            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            body.transform.localScale = Vector3.one * spec.scale;
            _body = body.transform;
            var sr = body.AddComponent<SpriteRenderer>();
            sr.sprite = GetSprite(spec.id);
            sr.sortingOrder = -2;
            var mat = SharedSpriteMaterial();
            if (mat != null) sr.sharedMaterial = mat;

            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.9f, 1.5f);

            RefreshLabel();
        }

        /// <summary>Same lit sprite material the Home/Loom visuals use, when one can be found.</summary>
        private static Material SharedSpriteMaterial()
        {
            var mgr = SpiritManager.Instance;
            if (mgr != null)
            {
                var agents = mgr.AllSpirits;
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] != null && agents[i].Renderer != null && agents[i].Renderer.sharedMaterial != null)
                        return agents[i].Renderer.sharedMaterial;
            }
            return null;
        }

        private void OnEnable() => _all.Add(this);

        private void OnDisable()
        {
            _all.Remove(this);
            for (int i = 0; i < _sessions.Count; i++)
                if (_sessions[i].agent != null) _sessions[i].agent.EndTraining();
            _sessions.Clear();
        }

        // ---- appetite --------------------------------------------------------------

        /// <summary>True when the bait slot holds this spirit's favorite food.</summary>
        public bool IsBaitedFor(SpiritAgent a) =>
            BaitCharges > 0 && a != null && a.Species != null
            && !string.IsNullOrEmpty(BaitId) && a.Species.favoredFoodId == BaitId;

        /// <summary>Free places, counting spirits already walking here.</summary>
        public bool HasRoomFor(SpiritAgent self)
        {
            int taken = _sessions.Count;
            var mgr = SpiritManager.Instance;
            if (mgr != null)
            {
                var agents = mgr.AllSpirits;
                for (int i = 0; i < agents.Count; i++)
                {
                    var a = agents[i];
                    if (a != null && a != self && !a.IsTraining && a.TrainTarget == this) taken++;
                }
            }
            return taken < Capacity;
        }

        /// <summary>
        /// Chance (0..1) that this spirit fancies a session here on one check:
        /// base x species keenness (higher stat max = keener) x trait appetite,
        /// boosted when its favorite food is in the bait slot. Zero when it is
        /// already at its species max for the stat, or the building is full.
        /// </summary>
        public float Appetite(SpiritAgent a)
        {
            if (a == null || Spec == null || a.Species == null) return 0f;

            a.GetStatBand(Spec.stat, out _, out int max);
            if (a.GetStat(Spec.stat) >= max) return 0f;
            if (!HasRoomFor(a)) return 0f;

            float p = BaseAppetite * (0.5f + max / 8f) * SpiritTraits.Appetite(a.Traits, Spec.stat);
            if (IsBaitedFor(a)) p *= BaitAttraction * SpiritTraits.BaitAppetite(a.Traits);
            return Mathf.Clamp(p, 0f, 0.95f);
        }

        // ---- sessions ----------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            TickSessions(dt);

            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 0.25f;
                ScanArrivals();
            }

            if (_body != null)
            {
                float k = _sessions.Count > 0 ? 1f + 0.025f * Mathf.Sin(Time.time * 9f) : 1f;
                _body.localScale = Vector3.one * (Spec != null ? Spec.scale : 1f) * k;
            }
        }

        private void ScanArrivals()
        {
            if (_sessions.Count >= Capacity) return;
            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            var agents = mgr.AllSpirits;
            for (int i = 0; i < agents.Count && _sessions.Count < Capacity; i++)
            {
                var a = agents[i];
                if (a == null || a.IsTraining || a.TrainTarget != this) continue;

                Vector3 d = a.transform.position - transform.position;
                d.z = 0f;
                if (d.sqrMagnitude > UseRadius * UseRadius) continue;

                if (a.BeginTraining(SessionSeconds))
                    _sessions.Add(new Session { agent = a, elapsed = 0f, pulseIn = 0.3f });
            }
        }

        private void TickSessions(float dt)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var s = _sessions[i];
                // Interrupted (slept, followed the shepherd, ran off, despawned): no reward.
                if (s.agent == null || !s.agent.IsTraining)
                {
                    if (s.agent != null) s.agent.EndTraining();
                    _sessions.RemoveAt(i);
                    continue;
                }

                s.elapsed += dt;
                s.pulseIn -= dt;
                if (s.pulseIn <= 0f)
                {
                    s.pulseIn = 1.1f;
                    s.agent.TrainingPulse();
                }

                if (s.elapsed >= SessionSeconds)
                {
                    Complete(s.agent);
                    _sessions.RemoveAt(i);
                }
            }
        }

        private void Complete(SpiritAgent a)
        {
            bool baited = IsBaitedFor(a);
            float ticks = Spec.ticksPerSession * (baited ? BaitTickMul : 1f);
            if (baited)
            {
                BaitCharges--;
                if (BaitCharges <= 0) BaitId = "";
                RefreshLabel();
            }

            int before = a.GetStat(Spec.stat);
            int gained = a.AddTrainingTicks(Spec.stat, ticks);
            a.EndTraining();

            Vector3 at = a.transform.position + Vector3.up * 0.9f;
            if (gained > 0)
            {
                FloatingText.Show(at, SpiritStats.Label(Spec.stat) + " " + before + " -> " + (before + gained),
                    UIStyle.Gold);
                Bleeps.Play(BleepKind.Soothe, 0.6f);
            }
            else
            {
                a.GetStatBand(Spec.stat, out _, out int max);
                FloatingText.Show(at, a.GetStat(Spec.stat) >= max ? "(at its limit)" : "(a good workout)",
                    UIStyle.Grey);
            }
        }

        // ---- bait ----------------------------------------------------------------------

        public bool AddBait(string foodId)
        {
            var inv = Inventory.Instance;
            if (inv == null || string.IsNullOrEmpty(foodId)) return false;
            if (BaitCharges > 0 && BaitId != foodId) { Say("(clear the bait first)"); return false; }
            if (BaitCharges + ChargesPerFood > MaxBaitCharges) { Say("(the bait slot is full)"); return false; }
            if (!CropQuality.ConsumeWorst(inv, foodId, 1)) return false;

            BaitId = foodId;
            BaitCharges += ChargesPerFood;
            RefreshLabel();
            Bleeps.Play(BleepKind.Feed, 0.7f);
            Say("bait laid: " + foodId);
            return true;
        }

        /// <summary>Empties the slot and returns whole unused foods to the inventory.</summary>
        public void ClearBait()
        {
            if (BaitCharges <= 0) return;
            int back = BaitCharges / ChargesPerFood;
            if (back > 0 && Inventory.Instance != null && !string.IsNullOrEmpty(BaitId))
                Inventory.Instance.Add(BaitId, back);
            BaitId = "";
            BaitCharges = 0;
            RefreshLabel();
        }

        /// <summary>Console hook: fills the bait slot directly (no inventory cost).</summary>
        public void Debug_SetBait(string foodId, int charges)
        {
            BaitCharges = string.IsNullOrEmpty(foodId) ? 0 : Mathf.Clamp(charges, 0, MaxBaitCharges);
            BaitId = BaitCharges > 0 ? foodId : "";
            RefreshLabel();
        }

        private void RefreshLabel()
        {
            string text = Spec != null ? Spec.displayName : "Training";
            if (BaitCharges > 0 && !string.IsNullOrEmpty(BaitId))
                text += "\n(bait: " + BaitId + " x" + BaitCharges + ")";
            WorldLabel.Attach(gameObject, text, -0.95f);
        }

        private void Say(string text) =>
            FloatingText.Show(transform.position + Vector3.up * 1.2f, text, UIStyle.Grey);

        // ---- ISelectable -------------------------------------------------------------------

        public string SelectableTitle => Spec != null ? Spec.displayName : "Training";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null || Spec == null) return;

            into.Add(new SelectAction("(spirits train " + SpiritStats.Label(Spec.stat) + " here)", () => { }, false));

            string slot = BaitCharges > 0
                ? "Bait: " + BaitId + " x" + BaitCharges
                : "Bait: none";
            into.Add(new SelectAction(slot, () => { }, false));

            // One row per food some species favors that the player is holding.
            var inv = Inventory.Instance;
            var mgr = SpiritManager.Instance;
            if (inv != null && mgr != null)
            {
                var seen = new HashSet<string>();
                var species = mgr.KnownSpecies;
                for (int i = 0; i < species.Count; i++)
                {
                    var s = species[i];
                    if (s == null || string.IsNullOrEmpty(s.favoredFoodId) || !seen.Add(s.favoredFoodId)) continue;
                    int have = CropQuality.CountAny(inv, s.favoredFoodId);
                    if (have <= 0) continue;
                    string food = s.favoredFoodId;
                    into.Add(new SelectAction("Add bait: " + food + " (have " + have + ")",
                        () => AddBait(food)));
                }
            }
            if (BaitCharges > 0)
                into.Add(new SelectAction("Clear bait", ClearBait));

            if (!_confirmingDestroy)
            {
                into.Add(new SelectAction("Destroy", () => { _confirmingDestroy = true; }, false));
            }
            else
            {
                into.Add(new SelectAction("Really destroy?", () =>
                {
                    _confirmingDestroy = false;
                    ClearBait(); // refund the unused bait
                    Destroy(gameObject);
                }));
            }
        }

        // ---- generated placeholder sprites ------------------------------------------------------

        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        /// <summary>Generated placeholder sprite for a spec id (also the build ghost).</summary>
        public static Sprite GetSprite(string specId)
        {
            if (_sprites.TryGetValue(specId, out var cached) && cached != null) return cached;

            Sprite s;
            switch (specId)
            {
                case "stones": s = MakeStones(); break;
                case "hurdles": s = MakeHurdles(); break;
                default: s = MakeMirror(); break;
            }
            _sprites[specId] = s;
            return s;
        }

        private static Sprite Finish(Color[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void Blend(Color[] px, int w, int h, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || c.a <= 0f) return;
            var dst = px[y * w + x];
            float a = c.a + dst.a * (1f - c.a);
            if (a <= 0f) return;
            var rgb = (new Color(c.r, c.g, c.b) * c.a + new Color(dst.r, dst.g, dst.b) * dst.a * (1f - c.a)) / a;
            px[y * w + x] = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        private static void Ellipse(Color[] px, int w, int h, float cx, float cy, float rx, float ry, Color col)
        {
            for (int y = Mathf.Max(0, (int)(cy - ry - 2)); y <= Mathf.Min(h - 1, (int)(cy + ry + 2)); y++)
                for (int x = Mathf.Max(0, (int)(cx - rx - 2)); x <= Mathf.Min(w - 1, (int)(cx + rx + 2)); x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cover = Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry)); // ~1px soft edge
                    if (cover <= 0f) continue;
                    var c = col; c.a *= cover;
                    Blend(px, w, h, x, y, c);
                }
        }

        private static void Box(Color[] px, int w, int h, int x0, int y0, int x1, int y1, Color col)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Blend(px, w, h, x, y, col);
        }

        /// <summary>Outlined, highlighted rounded blob (stone / shell look).</summary>
        private static void Blob(Color[] px, int w, int h, float cx, float cy, float rx, float ry, Color baseCol)
        {
            var dark = new Color(baseCol.r * 0.55f, baseCol.g * 0.55f, baseCol.b * 0.6f, 1f);
            var light = new Color(Mathf.Min(1f, baseCol.r * 1.18f), Mathf.Min(1f, baseCol.g * 1.18f),
                Mathf.Min(1f, baseCol.b * 1.18f), 1f);
            Ellipse(px, w, h, cx, cy, rx, ry, dark);
            Ellipse(px, w, h, cx, cy + 0.5f, rx - 1.5f, ry - 1.5f, baseCol);
            Ellipse(px, w, h, cx - rx * 0.25f, cy + ry * 0.3f, rx * 0.45f, ry * 0.35f, light);
        }

        private static Sprite MakeStones()
        {
            const int w = 56, h = 40;
            var px = new Color[w * h];
            Ellipse(px, w, h, 28f, 7f, 24f, 5f, new Color(0f, 0f, 0f, 0.25f)); // ground shadow
            Blob(px, w, h, 17f, 14f, 13f, 9f, new Color(0.56f, 0.57f, 0.63f, 1f));
            Blob(px, w, h, 38f, 13f, 12f, 8.5f, new Color(0.52f, 0.54f, 0.60f, 1f));
            Blob(px, w, h, 28f, 25f, 12f, 9f, new Color(0.62f, 0.63f, 0.69f, 1f));
            return Finish(px, w, h);
        }

        private static Sprite MakeHurdles()
        {
            const int w = 64, h = 36;
            var px = new Color[w * h];
            var wood = new Color(0.48f, 0.34f, 0.22f, 1f);
            var woodDark = new Color(0.32f, 0.22f, 0.14f, 1f);
            var bar = new Color(0.90f, 0.86f, 0.74f, 1f);
            Ellipse(px, w, h, 32f, 5f, 29f, 4f, new Color(0f, 0f, 0f, 0.22f));
            for (int i = 0; i < 3; i++)
            {
                int x = 6 + i * 20;
                Box(px, w, h, x, 6, x + 1, 22, woodDark);
                Box(px, w, h, x + 12, 6, x + 13, 22, woodDark);
                Box(px, w, h, x + 1, 6, x + 1, 22, wood);
                Box(px, w, h, x + 12, 6, x + 12, 22, wood);
                Box(px, w, h, x, 19, x + 13, 21, bar);
                Box(px, w, h, x, 12, x + 13, 14, bar);
                Box(px, w, h, x, 19, x + 13, 19, new Color(0.7f, 0.66f, 0.55f, 1f));
                Box(px, w, h, x, 12, x + 13, 12, new Color(0.7f, 0.66f, 0.55f, 1f));
            }
            return Finish(px, w, h);
        }

        private static Sprite MakeMirror()
        {
            const int w = 44, h = 56;
            var px = new Color[w * h];
            var frame = new Color(0.86f, 0.70f, 0.36f, 1f);
            var frameDark = new Color(0.55f, 0.42f, 0.20f, 1f);
            var glass = new Color(0.70f, 0.84f, 0.95f, 1f);
            Ellipse(px, w, h, 22f, 4f, 15f, 3.5f, new Color(0f, 0f, 0f, 0.22f));
            Box(px, w, h, 14, 4, 16, 18, frameDark); // stand legs
            Box(px, w, h, 28, 4, 30, 18, frameDark);
            Ellipse(px, w, h, 22f, 34f, 17f, 21f, frameDark);
            Ellipse(px, w, h, 22f, 34.5f, 15.5f, 19.5f, frame);
            Ellipse(px, w, h, 22f, 34.5f, 12.5f, 16.5f, glass);
            // diagonal glint across the glass
            for (int y = 24; y <= 46; y++)
                for (int x = 14; x <= 30; x++)
                {
                    float dx = (x - 22f) / 12.5f, dy = (y - 34.5f) / 16.5f;
                    if (dx * dx + dy * dy > 0.92f) continue;
                    int off = Mathf.Abs((x - 22) - (y - 34) / 2 + 3);
                    if (off <= 1) Blend(px, w, h, x, y, new Color(1f, 1f, 1f, 0.5f));
                }
            return Finish(px, w, h);
        }
    }
}
