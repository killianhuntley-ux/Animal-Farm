using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>The three untamable archetypes (muscle 07, verdict 3).</summary>
    public enum VillainKind { Digger, Devourer, Scarer }

    /// <summary>
    /// One villain spirit on a visit. Shared body, per-kind behavior:
    ///   DIGGER   - burrows to 2-4 usable Dirt/Grass cells and leaves a hole
    ///              at each (cap 4/visit). Recoils from the player; two close
    ///              approaches drive it off.
    ///   DEVOURER - beelines to the nearest plant (PREFERS immature ones) and
    ///              eats it over a telegraphed 6-8 real seconds (cap 2/visit).
    ///              Seeds are NOT refunded - the small real loss. Bolts the
    ///              instant the player comes within 2 units.
    ///   SCARER   - drifts past residents pulsing fear: -6 Spirit, one-time
    ///              per resident, floored at 25 inside SpiritAgent.ApplyFright
    ///              (cap 4 frights/visit). Hates being seen: the player within
    ///              1.5 units drives it off.
    /// Visits last 1-2 game-hours, then it slinks off at the fence. It never
    /// steps inside a Watchlight ward; when everything it wants is warded it
    /// paces, grumbles, and leaves early. Like spirits, villains are ghosts:
    /// no rigidbody, no blocking collider - pure transform drift.
    /// Active villains are never saved (VillainManager handles persistence).
    /// </summary>
    public class VillainAgent : MonoBehaviour
    {
        // ---- shared tuning -----------------------------------------------------
        private const float VisitHoursMin = 1f;
        private const float VisitHoursMax = 2f;
        private const float NoClockHourRealSeconds = 50f; // degrade: 20-min day pace
        private const float DriftSpeed = 1.7f;
        private const float LeaveSpeed = 2.4f;
        private const float BoltSpeed = 6.5f;
        private const float ArriveDistance = 0.15f;
        private const int TargetHuntRounds = 3;    // failed hunts before giving up
        private const float PaceSeconds = 4f;      // grumble time before an early exit
        private const float GrumbleCooldown = 2.5f;
        private const float ScareCooldown = 1.2f;  // one player-approach counts once

        // ---- Digger (DAMAGE CAP: max 4 holes/visit; holes never spread) --------
        private const int HolesPerVisitMin = 2;
        private const int HolesPerVisitMax = 4;
        private const float BurrowSeconds = 2.5f;
        private const float DiggerScareRadius = 1.5f;
        private const int DiggerScaresToFlee = 2;

        // ---- Devourer (DAMAGE CAP: max 2 plants/visit) --------------------------
        private const int PlantsPerVisitMax = 2;
        private const float ChompSecondsMin = 6f;  // telegraphed, real seconds
        private const float ChompSecondsMax = 8f;
        private const float ChompReach = 0.55f;
        private const float DevourerScareRadius = 2f;

        // ---- Scarer (DAMAGE CAP: max 4 frights/visit; floor 25 in ApplyFright) --
        private const int FrightsPerVisitMax = 4;
        private const float FrightRadius = 2.5f;
        private const float FrightSpiritLoss = 6f;
        private const float ScarerScareRadius = 1.5f;
        private const float FrightPulseInterval = 0.8f;

        private static readonly Color DiggerTint = new Color(0.38f, 0.28f, 0.22f, 0.95f);
        private static readonly Color DevourerTint = new Color(0.30f, 0.40f, 0.24f, 0.95f);
        private static readonly Color ScarerTint = new Color(0.44f, 0.34f, 0.54f, 0.95f);
        private static readonly Color DirtBrown = new Color(0.45f, 0.33f, 0.22f);
        private static readonly Color CrumbGreen = new Color(0.55f, 0.65f, 0.35f);

        private static Sprite _blob;

        private enum Phase { Work, Pace, Leave, Bolt }

        private VillainManager _owner;
        private VillainKind _kind;
        private Phase _phase = Phase.Work;
        private bool _resolved; // OnVillainGone already sent (or cancelled)

        private SpriteRenderer _body;
        private float _wobblePhase;
        private bool _actingWobble; // set each frame while burrowing/chomping

        private float _departAtTotalHours = -1f;
        private float _departAtRealTime = -1f;  // no-clock fallback
        private Vector3 _exitPoint;
        private bool _drivenOff;
        private float _paceUntil;
        private bool _pacedByWards;
        private Vector3 _paceTarget;
        private float _nextPaceRetarget;
        private float _nextGrumbleAt;
        private float _scareReadyAt;
        private int _targetFails;

        private Transform _playerTransform;

        // Digger state
        private bool _hasDigCell;
        private Vector2Int _digCell;
        private float _burrowTimer = -1f;
        private float _nextBurrowPuffAt;
        private int _holesDug;
        private int _plannedHoles;
        private int _diggerScares;

        // Devourer state
        private Plant _plantTarget;
        private float _chompTimer = -1f;
        private float _chompDuration;
        private float _nextCrumbAt;
        private int _plantsEaten;

        // Scarer state
        private readonly HashSet<SpiritAgent> _frightened = new HashSet<SpiritAgent>();
        private SpiritAgent _scareTarget;
        private float _nextPulseAt;
        private int _frights;

        // ---- setup ---------------------------------------------------------------

        /// <summary>Builds a villain at the entry point. Only VillainManager calls this.</summary>
        public static VillainAgent Spawn(VillainManager owner, VillainKind kind, Vector3 entry)
        {
            var go = new GameObject("Villain (" + kind + ")");
            go.transform.position = entry;
            var agent = go.AddComponent<VillainAgent>();
            agent.Init(owner, kind);
            return agent;
        }

        private void Init(VillainManager owner, VillainKind kind)
        {
            _owner = owner;
            _kind = kind;
            transform.localScale = Vector3.one * 1.5f;

            // Body renderer on a child so the wobble never fights root movement
            // (SpiritAgent pattern). Placeholder body: a generated eerie blob,
            // tinted dark per kind.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            _body = bodyGo.AddComponent<SpriteRenderer>();
            _body.sprite = Blob;
            _body.sortingOrder = 2;
            _body.color = kind == VillainKind.Digger ? DiggerTint
                : kind == VillainKind.Devourer ? DevourerTint
                : ScarerTint;

            WorldLabel.Attach(gameObject, kind.ToString(), -0.8f);

            float stay = Random.Range(VisitHoursMin, VisitHoursMax);
            if (GameClock.Instance != null)
                _departAtTotalHours = GameClock.Instance.TotalHours + stay;
            else
                _departAtRealTime = Time.time + stay * NoClockHourRealSeconds;

            _plannedHoles = Random.Range(HolesPerVisitMin, HolesPerVisitMax + 1);
            _wobblePhase = Random.Range(0f, Mathf.PI * 2f);
        }

        /// <summary>Shared eerie-blob sprite, generated once (Puffs pattern -
        /// villains self-spawn, so no bootstrapper hands them art).</summary>
        private static Sprite Blob
        {
            get
            {
                if (_blob == null)
                {
                    const int size = 24;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var px = new Color32[size * size];
                    Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 d = new Vector2(x, y) - c;
                        float ang = Mathf.Atan2(d.y, d.x);
                        float edge = 1f + 0.16f * Mathf.Sin(ang * 3f)
                                        + 0.10f * Mathf.Sin(ang * 7f + 1.7f);
                        bool inside = d.magnitude <= size * 0.38f * edge;
                        // Frayed lower edge: the bottom trails off raggedly.
                        if (inside && d.y < -size * 0.18f && ((x * 7 + y * 13) % 4) == 0)
                            inside = false;
                        px[x + y * size] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                    }
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.filterMode = FilterMode.Point;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _blob = Sprite.Create(tex, new Rect(0, 0, size, size),
                        new Vector2(0.5f, 0.45f), size);
                    _blob.hideFlags = HideFlags.HideAndDontSave;
                }
                return _blob;
            }
        }

        /// <summary>Teardown without drama (save-restore kills active villains).</summary>
        public void CancelSilently()
        {
            _resolved = true;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Destroyed without departing (scene teardown, restore): make sure
            // the manager drops its reference and the banner, quietly.
            if (!_resolved && _owner != null) _owner.OnVillainVanished(this);
        }

        // ---- per-frame --------------------------------------------------------------

        private void Update()
        {
            _actingWobble = false;

            switch (_phase)
            {
                case Phase.Leave:
                case Phase.Bolt:
                    TickLeave();
                    break;

                case Phase.Pace:
                    TickPace();
                    break;

                default:
                    if (TimeIsUp())
                    {
                        StartLeave("(it slinks away into the dark)", false);
                    }
                    else if (!TickPlayerScare())
                    {
                        switch (_kind)
                        {
                            case VillainKind.Digger: TickDigger(); break;
                            case VillainKind.Devourer: TickDevourer(); break;
                            default: TickScarer(); break;
                        }
                    }
                    break;
            }

            TickVisuals();
        }

        private bool TimeIsUp()
        {
            if (_departAtTotalHours >= 0f && GameClock.Instance != null)
                return GameClock.Instance.TotalHours >= _departAtTotalHours;
            return _departAtRealTime >= 0f && Time.time >= _departAtRealTime;
        }

        // ---- player drive-off ---------------------------------------------------------

        /// <summary>Per-kind player proximity reaction. True when it consumed the
        /// frame (bolted, or the Digger abandoned its spot).</summary>
        private bool TickPlayerScare()
        {
            if (Time.time < _scareReadyAt) return false;
            float dist = DistanceToPlayer();
            if (float.IsPositiveInfinity(dist)) return false;

            switch (_kind)
            {
                case VillainKind.Devourer:
                    // Sprinting at it feels great: it bolts instantly, meal unfinished.
                    if (dist <= DevourerScareRadius)
                    {
                        FloatingText.Show(transform.position + Vector3.up * 0.6f,
                            "it bolts!", UIStyle.Gold);
                        StartBolt();
                        return true;
                    }
                    return false;

                case VillainKind.Scarer:
                    if (dist <= ScarerScareRadius)
                    {
                        FloatingText.Show(transform.position + Vector3.up * 0.6f,
                            "(it hates being seen)", UIStyle.Gold);
                        StartBolt();
                        return true;
                    }
                    return false;

                default: // Digger: recoils and relocates; two approaches drive it off
                    if (dist <= DiggerScareRadius)
                    {
                        _scareReadyAt = Time.time + ScareCooldown;
                        _diggerScares++;
                        _hasDigCell = false;
                        _burrowTimer = -1f;
                        if (_diggerScares >= DiggerScaresToFlee)
                        {
                            FloatingText.Show(transform.position + Vector3.up * 0.6f,
                                "it flees underground!", UIStyle.Gold);
                            StartBolt();
                        }
                        else
                        {
                            FloatingText.Show(transform.position + Vector3.up * 0.6f,
                                "(it recoils)", UIStyle.Grey);
                        }
                        return true;
                    }
                    return false;
            }
        }

        // ---- Digger ---------------------------------------------------------------------

        private void TickDigger()
        {
            if (!_hasDigCell)
            {
                if (!TryFindDigCell(out _digCell))
                {
                    HuntFailed();
                    return;
                }
                _targetFails = 0;
                _hasDigCell = true;
                _burrowTimer = -1f;
            }

            Vector3 target = CellCenter(_digCell);
            if ((transform.position - target).sqrMagnitude > ArriveDistance * ArriveDistance)
            {
                if (!MoveWardAware(target, DriftSpeed)) _hasDigCell = false; // ward in the way: repick
                return;
            }

            // Burrowing: fast wobble + dirt kicked up, then the hole appears.
            _actingWobble = true;
            if (_burrowTimer < 0f) _burrowTimer = 0f;
            _burrowTimer += Time.deltaTime;

            if (Time.time >= _nextBurrowPuffAt)
            {
                _nextBurrowPuffAt = Time.time + 0.5f;
                Puffs.Burst(transform.position + Vector3.down * 0.2f, DirtBrown, 5, 1.2f, 0.3f, 0.09f);
            }

            if (_burrowTimer < BurrowSeconds) return;

            _burrowTimer = -1f;
            _hasDigCell = false;

            if (CellStillDiggable(_digCell) && _owner != null
                && _owner.SpawnHole(_digCell) != null)
            {
                _holesDug++;
                Bleeps.Play(BleepKind.Build, 0.4f); // a dull, wrong little thump
                FloatingText.Show(CellCenter(_digCell) + Vector3.up * 0.5f,
                    "it burrowed a hole!", UIStyle.Danger);
            }

            if (_holesDug >= _plannedHoles)
                StartLeave("(it slinks off, satisfied)", false);
        }

        /// <summary>Random usable Dirt/Grass cell: never under plants, homes, or
        /// existing holes, never inside a Watchlight ward.</summary>
        private bool TryFindDigCell(out Vector2Int cell)
        {
            cell = default;
            var grid = TerrainGrid.Instance;
            if (grid == null) return false;

            for (int tries = 0; tries < 20; tries++)
            {
                Vector3 pos = grid.RandomUsableInteriorPoint();
                if (!grid.TryWorldToCell(pos, out var candidate)) continue;
                if (!CellStillDiggable(candidate)) continue;
                if (VillainManager.IsWarded(pos)) continue;

                cell = candidate;
                return true;
            }
            return false;
        }

        private static bool CellStillDiggable(Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.IsUsable(cell)) return false;
            var surface = grid.GetSurface(cell);
            if (surface != Surface.Dirt && surface != Surface.Grass) return false;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return false;
            if (Home.AnyAtCell(cell)) return false;
            if (VillainHole.AnyAt(cell)) return false;
            return true;
        }

        // ---- Devourer --------------------------------------------------------------------

        private void TickDevourer()
        {
            if (_plantTarget == null)
            {
                _chompTimer = -1f;
                if (!TryFindPlant(out _plantTarget))
                {
                    HuntFailed();
                    return;
                }
                _targetFails = 0;
            }

            Vector3 target = _plantTarget.transform.position;
            if ((transform.position - target).sqrMagnitude > ChompReach * ChompReach)
            {
                if (!MoveWardAware(target, DriftSpeed)) _plantTarget = null; // warded mid-path
                return;
            }

            // Telegraph the meal loudly: the player gets 6-8 real seconds.
            if (_chompTimer < 0f)
            {
                _chompTimer = 0f;
                _chompDuration = Random.Range(ChompSecondsMin, ChompSecondsMax);

                string label = _plantTarget.Species != null
                    && !string.IsNullOrEmpty(_plantTarget.Species.displayName)
                    ? _plantTarget.Species.displayName.ToLowerInvariant()
                    : "plants";
                Vector3 warnAt = _playerTransform != null
                    ? _playerTransform.position : target;
                FloatingText.Show(warnAt + Vector3.up * 1.0f,
                    "something is eating the " + label + "!", UIStyle.Danger);
                Bleeps.Play(BleepKind.Alarm, 0.3f);
            }

            _actingWobble = true; // chomping wobble
            _chompTimer += Time.deltaTime;

            if (Time.time >= _nextCrumbAt)
            {
                _nextCrumbAt = Time.time + 1.2f;
                Puffs.Burst(target + Vector3.up * 0.2f, CrumbGreen, 4, 1.0f, 0.3f, 0.08f);
            }

            if (_chompTimer < _chompDuration) return;

            // Eaten. Seeds are NOT refunded - the small real loss (and the
            // whole loss: one plant, capped at 2 per visit).
            FloatingText.Show(target + Vector3.up * 0.4f, "(devoured)", UIStyle.Grey);
            Puffs.Burst(target, CrumbGreen, 7, 1.4f, 0.35f, 0.1f);
            if (PlantManager.Instance != null) PlantManager.Instance.RemovePlant(_plantTarget);
            _plantTarget = null;
            _plantsEaten++;

            if (_plantsEaten >= PlantsPerVisitMax)
                StartLeave("(it waddles off, full)", false);
        }

        /// <summary>Nearest plant, PREFERRING immature ones (mature crops only as
        /// a fallback). Plants inside a Watchlight ward are unreachable.</summary>
        private bool TryFindPlant(out Plant found)
        {
            found = null;
            if (PlantManager.Instance == null) return false;

            Plant bestYoung = null, bestMature = null;
            float bestYoungSqr = float.MaxValue, bestMatureSqr = float.MaxValue;

            var plants = PlantManager.Instance.AllPlants;
            for (int i = 0; i < plants.Count; i++)
            {
                var p = plants[i];
                if (p == null) continue;
                if (VillainManager.IsWarded(p.transform.position)) continue;

                float sqr = (p.transform.position - transform.position).sqrMagnitude;
                if (p.IsMature)
                {
                    if (sqr < bestMatureSqr) { bestMatureSqr = sqr; bestMature = p; }
                }
                else
                {
                    if (sqr < bestYoungSqr) { bestYoungSqr = sqr; bestYoung = p; }
                }
            }

            found = bestYoung != null ? bestYoung : bestMature;
            return found != null;
        }

        // ---- Scarer -----------------------------------------------------------------------

        private void TickScarer()
        {
            // Fear pulse on a short beat; each resident is hit at most once.
            if (Time.time >= _nextPulseAt)
            {
                _nextPulseAt = Time.time + FrightPulseInterval;
                PulseFright();
                if (_frights >= FrightsPerVisitMax)
                {
                    StartLeave("(it drifts off, smug)", false);
                    return;
                }
            }

            // Drift past the next unfrightened resident.
            if (_scareTarget == null || _frightened.Contains(_scareTarget)
                || _scareTarget.State != SpiritState.Resident)
            {
                if (!TryFindScareTarget(out _scareTarget, out bool anyLeft))
                {
                    if (!anyLeft)
                    {
                        // Nobody left to torment: the visit has run its course.
                        StartLeave("(it drifts off, bored)", false);
                    }
                    else
                    {
                        HuntFailed(); // targets exist but every one is warded
                    }
                    return;
                }
                _targetFails = 0;
            }

            if (!MoveWardAware(_scareTarget.transform.position, DriftSpeed))
                _scareTarget = null; // ward in the way: pick someone else
        }

        /// <summary>-6 Spirit to every unfrightened resident in 2.5 units, one
        /// time each, capped at 4 per visit. ApplyFright floors the result at
        /// 25 so a Scarer alone can never cause a runaway (calibration law).</summary>
        private void PulseFright()
        {
            if (SpiritManager.Instance == null) return;

            var spirits = SpiritManager.Instance.AllSpirits;
            for (int i = 0; i < spirits.Count && _frights < FrightsPerVisitMax; i++)
            {
                var a = spirits[i];
                if (a == null || a.State != SpiritState.Resident) continue;
                if (_frightened.Contains(a)) continue;
                if (Vector2.Distance(a.transform.position, transform.position) > FrightRadius)
                    continue;

                _frightened.Add(a);
                _frights++;
                a.ApplyFright(FrightSpiritLoss); // flinch + startle squeak inside
                FloatingText.Show(a.transform.position + Vector3.up * 0.8f,
                    "!", UIStyle.Danger);
            }
        }

        /// <summary>Nearest unfrightened resident outside every ward.
        /// anyLeft reports whether unfrightened residents exist at all.</summary>
        private bool TryFindScareTarget(out SpiritAgent target, out bool anyLeft)
        {
            target = null;
            anyLeft = false;
            if (SpiritManager.Instance == null) return false;

            float bestSqr = float.MaxValue;
            var spirits = SpiritManager.Instance.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || a.State != SpiritState.Resident) continue;
                if (_frightened.Contains(a)) continue;
                anyLeft = true;
                if (VillainManager.IsWarded(a.transform.position)) continue; // the light protects them

                float sqr = (a.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; target = a; }
            }
            return target != null;
        }

        // ---- movement / warding -----------------------------------------------------------

        /// <summary>
        /// Steps toward a target but NEVER into a Watchlight ward. False when
        /// the step was blocked (the caller should pick a new objective); a
        /// throttled grumble shows the ward doing its job.
        /// </summary>
        private bool MoveWardAware(Vector3 target, float speed)
        {
            target.z = 0f;
            Vector3 next = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if (VillainManager.IsWarded(next))
            {
                if (Time.time >= _nextGrumbleAt)
                {
                    _nextGrumbleAt = Time.time + GrumbleCooldown;
                    FloatingText.Show(transform.position + Vector3.up * 0.6f,
                        "(it grumbles at the light)", UIStyle.Grey);
                }
                return false;
            }
            transform.position = next;
            return true;
        }

        /// <summary>A target hunt came up empty (usually: everything warded).
        /// After a few failed rounds the villain paces and gives up early.</summary>
        private void HuntFailed()
        {
            _targetFails++;
            if (_targetFails >= TargetHuntRounds) StartPace();
        }

        // ---- pacing / leaving ----------------------------------------------------------------

        /// <summary>Warded out of everything it wants (or the farm offers it
        /// nothing): pace, grumble, then leave early. When Watchlights did the
        /// blocking, the exit reads as a win - the defense pays off visibly.</summary>
        private void StartPace()
        {
            _phase = Phase.Pace;
            _paceUntil = Time.time + PaceSeconds;
            _nextPaceRetarget = 0f;
            _pacedByWards = Watchlight.All.Count > 0;
            FloatingText.Show(transform.position + Vector3.up * 0.6f,
                _pacedByWards ? "(the watchlights hold it back)" : "(it finds nothing it wants)",
                _pacedByWards ? UIStyle.Gold : UIStyle.Grey);
        }

        private void TickPace()
        {
            if (Time.time >= _paceUntil)
            {
                StartLeave(_pacedByWards ? "(warded off, it gives up)" : "(it gives up)",
                    _pacedByWards);
                return;
            }

            if (Time.time >= _nextPaceRetarget)
            {
                _nextPaceRetarget = Time.time + 1f;
                _paceTarget = transform.position
                    + (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                _paceTarget.z = 0f;
            }
            MoveWardAware(_paceTarget, DriftSpeed);
        }

        private void StartLeave(string text, bool drivenOff)
        {
            if (_phase == Phase.Leave || _phase == Phase.Bolt) return;
            _phase = Phase.Leave;
            _drivenOff = _drivenOff || drivenOff;
            _exitPoint = PickExitPoint();
            FloatingText.Show(transform.position + Vector3.up * 0.6f, text, UIStyle.Grey);
        }

        private void StartBolt()
        {
            if (_phase == Phase.Bolt) return;
            _phase = Phase.Bolt;
            _drivenOff = true;
            _exitPoint = PickExitPoint();
        }

        private Vector3 PickExitPoint()
        {
            var grid = TerrainGrid.Instance;
            return grid != null
                ? grid.NearestUsableBorderPoint(transform.position)
                : transform.position;
        }

        private void TickLeave()
        {
            // Fleeing ignores wards on purpose: never trap a villain inside.
            float speed = _phase == Phase.Bolt ? BoltSpeed : LeaveSpeed;
            transform.position = Vector3.MoveTowards(
                transform.position, _exitPoint, speed * Time.deltaTime);

            if ((transform.position - _exitPoint).sqrMagnitude > ArriveDistance * ArriveDistance)
                return;

            _resolved = true;
            if (_owner != null) _owner.OnVillainGone(this, transform.position, _drivenOff);
            Puffs.Burst(transform.position, new Color(0.2f, 0.18f, 0.25f), 6, 1.2f, 0.3f, 0.09f);
            Destroy(gameObject);
        }

        // ---- visuals ------------------------------------------------------------------------

        /// <summary>Spooky idle wobble: a slow rock and low hover, sped up into a
        /// frantic shiver while burrowing/chomping. Alpha breathes slightly.</summary>
        private void TickVisuals()
        {
            if (_body == null) return;

            _wobblePhase += Time.deltaTime * (_actingWobble ? 16f : 4.5f);
            float rot = Mathf.Sin(_wobblePhase) * (_actingWobble ? 10f : 6f);
            _body.transform.localRotation = Quaternion.Euler(0f, 0f, rot);

            float y = Mathf.Sin(Time.time * 2f * Mathf.PI * 0.5f) * 0.05f;
            _body.transform.localPosition = new Vector3(0f, y, 0f);

            var c = _body.color;
            c.a = 0.88f + 0.07f * Mathf.Sin(Time.time * 2f * Mathf.PI * 1.3f);
            _body.color = c;
        }

        /// <summary>Planar distance to the shepherd (tag "Player"); +inf if absent.</summary>
        private float DistanceToPlayer()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player == null) return float.PositiveInfinity;
                _playerTransform = player.transform;
            }

            Vector3 to = _playerTransform.position - transform.position;
            to.z = 0f;
            return to.magnitude;
        }

        private static Vector3 CellCenter(Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            return grid != null ? grid.CellCenterWorld(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        }
    }
}
