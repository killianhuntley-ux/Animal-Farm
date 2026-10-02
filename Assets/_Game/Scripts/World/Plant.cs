using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// One growing plant instance. Never placed by hand — always created via
    /// <see cref="PlantManager.PlantSeed"/>. Growth is ACCUMULATED game-hours
    /// (not derived from the planting timestamp): each frame adds the elapsed
    /// game-hours scaled by soil moisture — watered soil grows at full speed,
    /// dry soil at half. Watering is a boost, never a death sentence — owner
    /// law: work is never wasted. Progress persists via GrowthHours.
    ///
    /// Muscle 02 crop depth: every Dirt crop also tracks how much of its grow
    /// time was spent watered; at harvest that share (plus a compost nudge)
    /// decides the 3-tier quality (Normal / Fine / Gleaming). An unwatered,
    /// unfinished crop WILTS (droop + desaturate) -- visual only, nothing ever
    /// dies. Regrowing species drop back to a mid stage on harvest instead of
    /// being removed. Water species (requiredSurface = Water) are always wet
    /// and carry no quality.
    /// </summary>
    public class Plant : MonoBehaviour, AnimalFarm.Interaction.IInteractable
    {
        private const float DrySpeed = 0.5f; // dry dirt still grows, just slower
        private const float CompostGrowthMult = 1.25f; // composted soil grows a quarter faster

        private const float WiltRate = 0.6f;    // wilt amount (0..1) change per real second
        private const float WiltDroop = 0.14f;  // vertical squash at full wilt
        private const float WiltTilt = 9f;      // degrees of sag at full wilt
        private const float WiltDesaturate = 0.65f;

        // Wind sway + bend (transform wobble, no shader). Every plant sways a
        // little around its base; a mover (shepherd, spirits) brushing past leans
        // it AWAY and a damped spring settles it back. All of it folds into the
        // one ApplyScaleAndTilt write, so the change guard still holds.
        private const float SwayCullSqr = 30f * 30f; // beyond this from the shepherd: no sway work at all
        private const float AmbientSprout = 1.2f;    // sway amplitude (degrees) by growth stage
        private const float AmbientMid = 2.2f;
        private const float AmbientRipe = 3.2f;
        private const float BendRadius = 0.95f;      // shepherd brush radius (world units, at the feet)
        private const float SpiritBendRadius = 0.8f;
        private const float SpiritBendStrength = 0.6f;
        private const float BendMaxDeg = 24f;
        private const float BendSpring = 90f;        // damped spring: slight overshoot on the way back
        private const float BendDamp = 9f;
        private const float BendSquash = 0.08f;      // vertical squash at full bend
        private const float TiltQuantum = 0.25f;     // degrees; writes only when the quantized tilt changes

        // Shared per-frame wind/mover state, refreshed by the first Plant that updates each frame.
        private static int s_frame = -1;
        private static float s_dt;                   // unscaled, clamped; 0 while paused
        private static float s_windClock;
        private static float s_gust;                 // slow global gust, 0 (calm) .. 1 (gusty)
        private static Transform s_shepherd;
        private static int s_nextFindFrame;
        private static bool s_hasShepherd;
        private static Vector2 s_shepherdFeet;
        private static readonly Vector2[] s_spiritPos = new Vector2[64];
        private static int s_spiritCount;

        /// <summary>Console ('gust'): pins the global gust 0..1; negative = natural wind.</summary>
        public static float DebugGust = -1f;

        /// <summary>Console ('gust'): the current global gust, 0..1.</summary>
        public static float CurrentGust => s_gust;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedState()
        {
            DebugGust = -1f;
            s_frame = -1; s_dt = 0f; s_windClock = 0f; s_gust = 0f;
            s_shepherd = null; s_nextFindFrame = 0; s_hasShepherd = false; s_spiritCount = 0;
        }

        private PlantSpecies _species;
        private Vector2Int _cell;
        private float _plantedAtTotalHours;
        private float _growthHours;
        private float _lastClockHours = float.NaN; // NaN = no clock sample yet

        // Quality tracking (this grow cycle; reset on regrow).
        private float _elapsedHours;   // game-hours since the cycle began (until mature)
        private float _wateredHours;   // of which the soil was wet
        private bool _composted;
        private float _debugScore = -1f; // >= 0 overrides the computed score (console)

        private SpriteRenderer _renderer;
        private int _appliedStage = -1;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;
        private float _wilt; // 0 = fresh, 1 = fully wilted
        private float _appliedTilt = float.NaN; // last z-tilt written (NaN = never written)

        private Vector3 _basePos;     // resting world position (the tilt pivots about the plant's base)
        private float _halfHeight;    // world half-height of the current sprite (pivot offset)
        private float _phase;         // per-plant sway phase (position hash)
        private float _freq = 1f;     // per-plant sway speed variation
        private float _swayDeg;       // ambient sway + wind lean + bend, degrees (added to the wilt tilt)
        private float _bend;          // spring state: current bend angle
        private float _bendVel;
        private float _squash;        // 0..1 vertical squash from a brush

        public PlantSpecies Species => _species;
        public Vector2Int Cell => _cell;

        /// <summary>Game-hours timestamp of planting (for save/migration).</summary>
        public float PlantedAtTotalHours => _plantedAtTotalHours;

        /// <summary>Accumulated effective growth, in game-hours (for save).</summary>
        public float GrowthHours => _growthHours;

        /// <summary>Game-hours this grow cycle has run (for save; quality driver).</summary>
        public float ElapsedHours => _elapsedHours;

        /// <summary>Of ElapsedHours, how many the soil was wet (for save; quality driver).</summary>
        public float WateredHours => _wateredHours;

        /// <summary>Compost has been worked into this cycle (for save).</summary>
        public bool Composted => _composted;

        private int LastStageIndex =>
            _species != null && _species.stageSprites != null && _species.stageSprites.Length > 0
                ? _species.stageSprites.Length - 1
                : 0;

        /// <summary>Current growth stage, derived from accumulated growth-hours.</summary>
        public int Stage
        {
            get
            {
                if (_species == null) return 0;
                float perStage = Mathf.Max(0.01f, _species.hoursPerStage);
                int stage = Mathf.FloorToInt(_growthHours / perStage);
                return Mathf.Clamp(stage, 0, LastStageIndex);
            }
        }

        public bool IsMature => _species != null && Stage >= LastStageIndex;

        /// <summary>Tilled-soil crops carry quality + wilt; water species are always wet and plain.</summary>
        public bool HasQuality => _species != null && _species.requiredSurface == Surface.Dirt;

        /// <summary>Share (0..1) of this cycle's grow time the soil was watered.</summary>
        public float WateredFraction => _elapsedHours > 0.01f ? Mathf.Clamp01(_wateredHours / _elapsedHours) : 0f;

        /// <summary>0..1 quality score: watered share, nudged up by compost.</summary>
        public float QualityScore
        {
            get
            {
                if (!HasQuality) return 0f;
                if (_debugScore >= 0f) return _debugScore;
                float score = Mathf.Clamp01(WateredFraction + (_composted ? CropQuality.CompostBonus : 0f));
                // Rich Mud is always wet (full growth speed) but never gleams: capped
                // at Fine, compost or not, so a mud farm cannot print x2.5 crops.
                if (OnMud) score = Mathf.Min(score, CropQuality.MudScoreCap);
                return score;
            }
        }

        private bool OnMud => TerrainGrid.Instance != null && TerrainGrid.Instance.GetSurface(_cell) == Surface.Mud;

        /// <summary>Tier this plant would harvest at right now.</summary>
        public CropTier Tier => CropQuality.FromScore(QualityScore);

        /// <summary>Called by PlantManager right after AddComponent.
        /// growthHours seeds accumulated progress (0 for a fresh seed; the
        /// save's value — or the migrated elapsed time — on restore). The
        /// quality fields restore a saved cycle (zeros for fresh/old saves).</summary>
        public void Init(PlantSpecies s, Vector2Int cell, float plantedAtTotalHours, float growthHours = 0f,
            float elapsedHours = 0f, float wateredHours = 0f, bool composted = false)
        {
            _species = s;
            _cell = cell;
            _plantedAtTotalHours = plantedAtTotalHours;
            _growthHours = Mathf.Max(0f, growthHours);
            _elapsedHours = Mathf.Max(0f, elapsedHours);
            _wateredHours = Mathf.Clamp(wateredHours, 0f, _elapsedHours);
            _composted = composted;
            _lastClockHours = float.NaN;

            name = "Plant_" + (s != null ? s.id : "unknown");

            if (TerrainGrid.Instance != null)
                transform.position = TerrainGrid.Instance.CellCenterWorld(cell);

            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<SpriteRenderer>();

            _baseScale = transform.localScale;
            _basePos = transform.position;

            // Phase + speed from a cheap position hash: neighbours never sway in lockstep.
            uint h = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            _phase = (h & 0xFFFFu) / 65535f * Mathf.PI * 2f;
            _freq = 0.85f + ((h >> 16) & 0xFFu) / 255f * 0.3f;
            _swayDeg = 0f; _bend = 0f; _bendVel = 0f; _squash = 0f;

            _appliedStage = -1;
            ApplyStage();
        }

        /// <summary>Jumps accumulated growth to the last stage (debug/cheat).</summary>
        public void ForceMature()
        {
            if (_species == null) return;
            _growthHours = LastStageIndex * Mathf.Max(0.01f, _species.hoursPerStage);
            ApplyStage();
        }

        /// <summary>Console helper: pins this plant's harvest tier.</summary>
        public void Debug_SetTier(CropTier tier)
        {
            _debugScore = tier == CropTier.Gleaming ? 1f
                : tier == CropTier.Fine ? CropQuality.FineThreshold
                : 0f;
        }

        /// <summary>True when worked-in compost is still possible for this cycle.</summary>
        public bool CanCompost => HasQuality && !IsMature && !_composted;

        /// <summary>Works compost into the soil under this plant (CompostManager calls this).</summary>
        public void ApplyCompost()
        {
            _composted = true;
        }

        private void Update()
        {
            AccumulateGrowth();
            ApplyStage();
            UpdateSway();
            UpdateWilt();
        }

        /// <summary>This plant's soil is wet: water species always, Dirt crops per the grid.</summary>
        private bool IsSoilWet()
        {
            if (_species != null && _species.requiredSurface == Surface.Water) return true;
            return TerrainGrid.Instance != null && TerrainGrid.Instance.IsWatered(_cell);
        }

        /// <summary>
        /// Adds this frame's elapsed game-hours, scaled by soil moisture:
        /// watered dirt = full speed, dry = half (compost x1.25). Watering is a
        /// boost, never a death sentence — owner law: work is never wasted.
        /// Until maturity the cycle clock + wet clock also tick (the quality driver).
        /// </summary>
        private void AccumulateGrowth()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            float now = clock.TotalHours;
            if (!float.IsNaN(_lastClockHours))
            {
                float deltaHours = Mathf.Max(0f, now - _lastClockHours);
                bool watered = IsSoilWet();

                if (!IsMature)
                {
                    _elapsedHours += deltaHours;
                    if (watered) _wateredHours += deltaHours;
                }

                float speed = watered ? 1f : DrySpeed;
                if (_composted) speed *= CompostGrowthMult;
                _growthHours += deltaHours * speed;
            }
            _lastClockHours = now;
        }

        private void ApplyStage()
        {
            if (_species == null || _renderer == null) return;

            int stage = Stage;
            if (stage == _appliedStage) return;
            _appliedStage = stage;

            if (_species.stageSprites != null && _species.stageSprites.Length > 0)
                _renderer.sprite = _species.stageSprites[Mathf.Clamp(stage, 0, _species.stageSprites.Length - 1)];
            _renderer.color = _species.tint;
            _halfHeight = _renderer.sprite != null ? _renderer.sprite.bounds.extents.y * _baseScale.y : 0f;
            _appliedTilt = float.NaN; // re-seat the base pivot for the new sprite on the next write
        }

        /// <summary>Wilt visual: an unwatered, unfinished tilled-soil crop droops
        /// and greys; it eases back the moment the soil is wet. Never lethal.</summary>
        private void UpdateWilt()
        {
            if (_species == null || _renderer == null) return;

            float target = HasQuality && !IsMature && !IsSoilWet() ? 1f : 0f;
            _wilt = Mathf.MoveTowards(_wilt, target, WiltRate * Time.deltaTime);

            Color tint = _species.tint;
            float g = tint.grayscale;
            var dull = new Color(g * 0.95f, g * 0.85f, g * 0.65f, tint.a);
            // Only write when the value actually changed: hundreds of plants must not
            // dirty their renderer every frame (== is Unity's approximate compare).
            Color wiltColor = Color.Lerp(tint, dull, _wilt * WiltDesaturate);
            if (_renderer.color != wiltColor) _renderer.color = wiltColor;

            ApplyScaleAndTilt();
        }

        // ---- wind sway + bend --------------------------------------------------

        /// <summary>First plant to update each frame refreshes the shared wind and
        /// mover positions (cached transforms and one spirit-list pass; no Find per frame).</summary>
        private static void RefreshShared()
        {
            s_frame = Time.frameCount;
            s_dt = Time.timeScale > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;
            s_windClock += s_dt; // unscaled: 'ff' must not turn the breeze into a gale

            // Slow gust: perlin drifts roughly 0.15..0.85 over tens of seconds; rain blows harder.
            float g = Mathf.InverseLerp(0.2f, 0.8f, Mathf.PerlinNoise(s_windClock * 0.06f, 3.7f));
            var weather = WeatherManager.Instance;
            if (weather != null && weather.IsRaining) g += 0.25f;
            s_gust = DebugGust >= 0f ? Mathf.Clamp01(DebugGust) : Mathf.Clamp01(g);

            if (s_shepherd == null && Time.frameCount >= s_nextFindFrame)
            {
                s_nextFindFrame = Time.frameCount + 60; // retry about once a second while there is no player
                var player = GameObject.FindWithTag("Player");
                if (player != null) s_shepherd = player.transform;
            }
            s_hasShepherd = s_shepherd != null;
            if (s_hasShepherd)
            {
                Vector3 p = s_shepherd.position;
                s_shepherdFeet = new Vector2(p.x, p.y - 0.3f);
            }

            s_spiritCount = 0;
            var spirits = AnimalFarm.Spirits.SpiritManager.Instance;
            if (spirits != null)
            {
                var list = spirits.AllSpirits;
                for (int i = 0; i < list.Count && s_spiritCount < s_spiritPos.Length; i++)
                {
                    var a = list[i];
                    if (a == null || !a.gameObject.activeInHierarchy) continue;
                    Vector3 p = a.transform.position;
                    s_spiritPos[s_spiritCount++] = new Vector2(p.x, p.y);
                }
            }
        }

        /// <summary>Strongest-brush-wins lean away from one mover (smoothstep falloff).</summary>
        private void Brush(Vector2 mover, float radius, float strength, ref float target, ref float squash)
        {
            float dx = _basePos.x - mover.x, dy = _basePos.y - mover.y;
            float sqr = dx * dx + dy * dy;
            if (sqr >= radius * radius) return;

            float d = Mathf.Sqrt(sqr);
            float f = 1f - d / radius;
            f = f * f * (3f - 2f * f) * strength;
            float dirX = d > 0.02f ? dx / d : (_phase > Mathf.PI ? 1f : -1f);
            float lean = -dirX * BendMaxDeg * f; // mover on the left (dx > 0): tops lean right = negative z
            if (Mathf.Abs(lean) > Mathf.Abs(target)) target = lean;
            if (f > squash) squash = f;
        }

        private void UpdateSway()
        {
            if (_species == null) return;
            if (s_frame != Time.frameCount) RefreshShared();

            // Far from the camera's reach: rest the plant (one guarded write) and do no work.
            if (s_hasShepherd)
            {
                float cx = _basePos.x - s_shepherdFeet.x, cy = _basePos.y - s_shepherdFeet.y;
                if (cx * cx + cy * cy > SwayCullSqr)
                {
                    _swayDeg = 0f; _bend = 0f; _bendVel = 0f; _squash = 0f;
                    return;
                }
            }

            // Ambient: amplitude by growth stage, scaled by the global gust; the gust
            // also leans the tops downwind. Floating lilies barely move.
            int stage = _appliedStage < 0 ? Stage : _appliedStage;
            float amp = stage <= 0 ? AmbientSprout : stage >= LastStageIndex ? AmbientRipe : AmbientMid;
            if (_species.requiredSurface == Surface.Water) amp *= 0.6f;
            float t = s_windClock * _freq;
            float calm = 0.35f + 0.65f * s_gust;
            float ambient = amp * calm * (Mathf.Sin(t * 1.9f + _phase) + 0.35f * Mathf.Sin(t * 3.1f + _phase * 1.7f));
            float lean = -s_gust * amp * 0.6f;

            // Bend: lean away from the shepherd / any spirit within brushing range.
            float target = 0f, squashTarget = 0f;
            if (s_hasShepherd) Brush(s_shepherdFeet, BendRadius, 1f, ref target, ref squashTarget);
            for (int i = 0; i < s_spiritCount; i++)
                Brush(s_spiritPos[i], SpiritBendRadius, SpiritBendStrength, ref target, ref squashTarget);

            float dt = s_dt;
            if (dt > 0f)
            {
                float accel = (target - _bend) * BendSpring - _bendVel * BendDamp;
                _bendVel += accel * dt;
                _bend = Mathf.Clamp(_bend + _bendVel * dt, -BendMaxDeg * 1.4f, BendMaxDeg * 1.4f);
                if (target == 0f && Mathf.Abs(_bend) < 0.05f && Mathf.Abs(_bendVel) < 0.3f)
                {
                    _bend = 0f; _bendVel = 0f; // settled: stop dirtying the transform
                }
                _squash = Mathf.MoveTowards(_squash, squashTarget, 6f * dt);
            }

            _swayDeg = ambient + lean + _bend;
        }

        private void ApplyScaleAndTilt()
        {
            float focus = _focused ? 1.08f : 1f;
            float squash = 1f - BendSquash * _squash;
            var scale = new Vector3(
                _baseScale.x * focus, _baseScale.y * focus * (1f - WiltDroop * _wilt) * squash, _baseScale.z);
            if (transform.localScale != scale) transform.localScale = scale;

            // Wilt sag + wind sway + bend combine into ONE tilt, quantized so the
            // transform is only written when the visible angle actually moves.
            float sign = ((_cell.x + _cell.y) & 1) == 0 ? 1f : -1f;
            float tilt = sign * WiltTilt * _wilt + _swayDeg;
            tilt = Mathf.Round(tilt / TiltQuantum) * TiltQuantum;
            if (!Mathf.Approximately(tilt, _appliedTilt))
            {
                _appliedTilt = tilt;
                transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                // Pivot about the plant's base (sprites pivot at centre): shift the
                // centre so the foot stays planted as the stalk leans.
                float rad = tilt * Mathf.Deg2Rad;
                transform.position = _basePos + new Vector3(
                    -_halfHeight * Mathf.Sin(rad), _halfHeight * (Mathf.Cos(rad) - 1f), 0f);
            }
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText
        {
            get
            {
                string label = _species != null ? _species.displayName : "Plant";
                if (IsMature)
                {
                    string stars = CropQuality.Stars(Tier);
                    return "Harvest " + label + (stars.Length > 0 ? " " + stars : "");
                }
                if (CanCompostNow()) return "Compost " + label;
                return label + " (growing)";
            }
        }

        private bool CanCompostNow() =>
            CanCompost && CompostManager.Instance != null && CompostManager.Instance.Available > 0;

        public bool CanInteract(GameObject actor) => IsMature || CanCompostNow();

        public void Interact(GameObject actor)
        {
            if (_species == null) return;

            if (!IsMature)
            {
                if (CanCompostNow()) CompostManager.Instance.TryApplyToPlant(this);
                return;
            }

            CropTier tier = Tier;
            string itemId = CropQuality.ItemId(_species.produceId, tier);
            if (Inventory.Instance != null)
                Inventory.Instance.Add(itemId, _species.produceAmount);

            Debug.Log("[Plants] Harvested " + _species.displayName + " -> "
                      + _species.produceAmount + "x " + itemId + " (q=" + QualityScore.ToString("0.00") + ")");

            if (tier != CropTier.Normal)
            {
                // Star badge on the harvest.
                Vector3 at = transform.position + Vector3.up * 0.7f;
                AnimalFarm.UI.FloatingText.Show(at,
                    CropQuality.Stars(tier) + " " + (tier == CropTier.Gleaming ? "Gleaming" : "Fine") + " "
                    + _species.displayName + " " + CropQuality.Stars(tier),
                    CropQuality.TierColor(tier));
                if (tier == CropTier.Gleaming)
                    Puffs.Burst(transform.position, CropQuality.GleamingColor, 8, 1.2f);
            }

            AnimalFarm.Core.ShepherdProgress.Grant("harvest");
            VendorArrivals.Note("cropsHarvested"); // hidden vendor move-in milestone

            if (_species.regrows)
            {
                BeginRegrow();
                return;
            }

            if (PlantManager.Instance != null)
                PlantManager.Instance.RemovePlant(this);
            else
                Destroy(gameObject);
        }

        /// <summary>Berry-bush style: drop back to the regrow stage and start a fresh
        /// quality cycle (compost is spent with the harvest).</summary>
        private void BeginRegrow()
        {
            int stage = Mathf.Clamp(_species.regrowStage, 0, Mathf.Max(0, LastStageIndex - 1));
            _growthHours = stage * Mathf.Max(0.01f, _species.hoursPerStage);
            _elapsedHours = 0f;
            _wateredHours = 0f;
            _composted = false;
            _debugScore = -1f;
            _appliedStage = -1;
            ApplyStage();
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
            ApplyScaleAndTilt();
        }
    }
}
