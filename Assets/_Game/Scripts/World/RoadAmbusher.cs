using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// A villain ambush on the road (muscle 08, "Villain ambush"): a Scarer or
    /// Devourer BURSTS out of the dark ahead of the shepherd. After a short
    /// telegraph it strikes ONCE:
    ///   SCARER   - frightens every escort within 4.5 units (-6 Spirit, floored
    ///              at 25 by ApplyFright); each frightened escort bolts with 60%.
    ///   DEVOURER - lunges at the escort nearest to it (always bolts) and snaps
    ///              at a second with 35%; no item or spirit is ever lost.
    /// A bolted escort becomes a Runaway already mid-chase (SpiritAgent.RoadBolt)
    /// and is recovered with the existing herding tags. CALIBRATION: at most 2
    /// bolts per ambush; nothing is permanent. The ambusher then lurks a few
    /// seconds; the shepherd walking up to it (within 2 units) drives it off, or
    /// it slinks away on its own. A fended ambush (ward charm / Watchlight)
    /// recoils at once and strikes nobody.
    ///
    /// Spawned only by RoadTravel.StartAmbush.
    /// </summary>
    public class RoadAmbusher : MonoBehaviour
    {
        private const float TelegraphSeconds = 0.8f;
        private const float LurkSeconds = 7f;
        private const float DriveOffRadius = 2.0f;
        private const float ScarerRadius = 4.5f;
        private const float FrightLoss = 6f;
        private const float ScarerBoltChance = 0.6f;
        private const float DevourerSecondChance = 0.35f;
        private const int MaxBolts = 2;
        private const float LeaveSpeed = 6.5f;
        private const float LeaveSeconds = 1.6f;

        private static readonly Color ScarerTint = new Color(0.52f, 0.40f, 0.64f, 0.95f);
        private static readonly Color DevourerTint = new Color(0.34f, 0.46f, 0.28f, 0.95f);

        private enum Phase { Telegraph, Lurk, Leave }

        private RoadTravel _owner;
        private VillainKind _kind;
        private bool _fended;
        private bool _bannerShown;
        private Phase _phase = Phase.Telegraph;
        private float _phaseStart;
        private SpriteRenderer _body;
        private Vector3 _leaveDir;
        private Transform _player;
        private float _wobble;

        public static RoadAmbusher Spawn(VillainKind kind, Vector3 pos, bool fended, RoadTravel owner)
        {
            var go = new GameObject("RoadAmbusher (" + kind + ")");
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var a = go.AddComponent<RoadAmbusher>();
            a.Init(kind, fended, owner);
            return a;
        }

        private void Init(VillainKind kind, bool fended, RoadTravel owner)
        {
            _owner = owner;
            _kind = kind;
            _fended = fended;
            _phaseStart = Time.time;
            _wobble = Random.Range(0f, 6.28f);

            transform.localScale = Vector3.one * 1.5f;
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            _body = FrontierArt.AddSprite(bodyGo, FrontierArt.Wraith, 3,
                kind == VillainKind.Devourer ? DevourerTint : ScarerTint);
            WorldLabel.Attach(gameObject, kind.ToString(), -0.8f);

            Puffs.Burst(transform.position, new Color(0.2f, 0.18f, 0.25f), 10, 2.2f, 0.45f, 0.12f);

            if (_fended)
            {
                FloatingText.Show(transform.position + Vector3.up * 0.9f,
                    "(it recoils from your ward)", UIStyle.Gold);
                StartLeave();
                return;
            }

            Bleeps.Play(BleepKind.Alarm, 0.4f);
            FloatingText.Show(transform.position + Vector3.up * 1.0f,
                _kind == VillainKind.Devourer ? "something hungry lunges from the dark!"
                                              : "something bursts out of the dark!", UIStyle.Danger);
            AlarmBannerUI.Show(this, "AMBUSH ON THE ROAD");
            _bannerShown = true;
        }

        private void OnDestroy()
        {
            if (_bannerShown) AlarmBannerUI.Hide(this);
            if (_owner != null) _owner.OnAmbusherGone(this);
        }

        private void Update()
        {
            _wobble += Time.deltaTime * 5f;
            if (_body != null)
            {
                _body.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_wobble) * 7f);
                var c = _body.color;
                c.a = _phase == Phase.Leave
                    ? Mathf.Clamp01(1f - (Time.time - _phaseStart) / LeaveSeconds) * 0.95f
                    : 0.9f;
                _body.color = c;
            }

            switch (_phase)
            {
                case Phase.Telegraph:
                    if (Time.time - _phaseStart >= TelegraphSeconds)
                    {
                        Strike();
                        _phase = Phase.Lurk;
                        _phaseStart = Time.time;
                    }
                    break;

                case Phase.Lurk:
                    if (PlayerNear())
                    {
                        FloatingText.Show(transform.position + Vector3.up * 0.7f,
                            "it bolts!", UIStyle.Gold);
                        Bleeps.Play(BleepKind.Soothe, 0.6f);
                        StartLeave();
                    }
                    else if (Time.time - _phaseStart >= LurkSeconds)
                    {
                        FloatingText.Show(transform.position + Vector3.up * 0.7f,
                            "(it slinks back into the dark)", UIStyle.Grey);
                        StartLeave();
                    }
                    break;

                default:
                    transform.position += _leaveDir * (LeaveSpeed * Time.deltaTime);
                    if (Time.time - _phaseStart >= LeaveSeconds) Destroy(gameObject);
                    break;
            }
        }

        private bool PlayerNear()
        {
            if (_player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p == null) return false;
                _player = p.transform;
            }
            Vector2 d = _player.position - transform.position;
            return d.magnitude <= DriveOffRadius;
        }

        private void StartLeave()
        {
            _phase = Phase.Leave;
            _phaseStart = Time.time;
            // off the road sideways: the rails are no obstacle to a ghost
            _leaveDir = new Vector3(Random.Range(-0.3f, 0.3f), transform.position.y >= 0f ? 1f : -1f, 0f).normalized;
            if (_bannerShown) { AlarmBannerUI.Hide(this); _bannerShown = false; }
        }

        // ---- the strike ----------------------------------------------------------------

        private void Strike()
        {
            var travel = _owner;
            if (travel == null) return;

            // snapshot: RoadBolt flips escorts to Runaway mid-loop
            var escorts = new List<SpiritAgent>(travel.Escorts);
            escorts.Sort((p, q) => (p.transform.position - transform.position).sqrMagnitude
                .CompareTo((q.transform.position - transform.position).sqrMagnitude));

            int bolts = 0;
            int hit = 0;

            if (_kind == VillainKind.Devourer)
            {
                // lunge at the nearest two: first always bolts, second sometimes
                for (int i = 0; i < escorts.Count && i < 2; i++)
                {
                    var a = escorts[i];
                    if (a == null) continue;
                    if (i == 0) transform.position = Vector3.Lerp(transform.position, a.transform.position, 0.6f);
                    a.ApplyFright(FrightLoss * 0.5f); // a snap, not a scare: half a fright
                    hit++;
                    bool bolt = i == 0 || Random.value < DevourerSecondChance;
                    if (bolt && bolts < MaxBolts && a.RoadBolt(transform.position)) bolts++;
                }
            }
            else
            {
                for (int i = 0; i < escorts.Count; i++)
                {
                    var a = escorts[i];
                    if (a == null) continue;
                    if (Vector2.Distance(a.transform.position, transform.position) > ScarerRadius) continue;
                    a.ApplyFright(FrightLoss);
                    hit++;
                    FloatingText.Show(a.transform.position + Vector3.up * 0.8f, "!", UIStyle.Danger);
                    if (bolts < MaxBolts && Random.value < ScarerBoltChance && a.RoadBolt(transform.position))
                        bolts++;
                }
            }

            if (bolts > 0)
            {
                FloatingText.Show(transform.position + Vector3.up * 1.2f,
                    bolts == 1 ? "(one bolts! herd it back)" : "(they scatter! herd them back)", UIStyle.Danger);
            }
            else if (hit > 0)
            {
                FloatingText.Show(transform.position + Vector3.up * 1.2f,
                    "(they hold steady)", UIStyle.Gold);
            }
        }
    }
}
