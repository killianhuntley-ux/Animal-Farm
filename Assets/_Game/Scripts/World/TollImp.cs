using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// A cheeky checkpoint imp on the west road (muscle 08, "Toll imps").
    /// RoadTravel camps one at the road's head on roughly 40% of days
    /// (deterministic per day) between 06:00 and 20:00. It wants a SMALL toll:
    /// 2 obols + 1 per escorted spirit, capped at 6. Pay (Interact / "Pay
    /// toll") and it waves you through for the rest of the day, in both
    /// directions. Refuse -- by the menu or just by walking past it -- and it
    /// blows a raspberry: every escort loses 5 Spirit (floored at 35), nothing
    /// else, and it forgets all about it at dawn. Small and recoverable by
    /// design. It never blocks the lane.
    ///
    /// Spawned and despawned only by RoadTravel (derived from the calendar,
    /// never saved; the per-day paid/refused stamp lives in RoadTravel).
    /// </summary>
    public class TollImp : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.08f;
        private const float QuipRadius = 4f;
        private const float QuipCooldown = 9f;
        private const float MaxPassStep = 3f; // ignore teleports when detecting a walk-past

        private static readonly Color ImpGold = new Color(1f, 0.84f, 0.45f);

        private static readonly string[] DemandLines =
        {
            "Toll! Small. Tiny. Practically a hug.",
            "Papers? No? Obols will do.",
            "The road is free. Using it is not.",
            "Pay the imp. The imp has a family. Allegedly."
        };

        private static readonly string[] PaidLines =
        {
            "Pleasure doing business!",
            "Mm. Warm. Pass, pass.",
            "A generous soul. Tell no one I said so."
        };

        private RoadTravel _owner;
        private Vector3 _baseScale = Vector3.one;
        private SpriteRenderer _renderer;
        private float _bobPhase;
        private float _quipReadyAt;
        private Transform _player;
        private float _lastPlayerX;
        private bool _hasLastX;

        public static TollImp Spawn(Vector3 pos, RoadTravel owner)
        {
            var go = new GameObject("TollImp");
            go.transform.position = pos;
            var imp = go.AddComponent<TollImp>();
            imp.Init(owner);
            return imp;
        }

        private void Init(RoadTravel owner)
        {
            _owner = owner;
            transform.localScale = Vector3.one * 1.5f;
            _baseScale = transform.localScale;
            _bobPhase = Random.Range(0f, 6.28f);

            var bodyGo = new GameObject("Body"); // child, so the idle hop never moves the imp itself
            bodyGo.transform.SetParent(transform, false);
            _renderer = FrontierArt.AddSprite(bodyGo, FrontierArt.Imp, 2);

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.7f;

            WorldLabel.Attach(gameObject, "Toll Imp", -0.7f);
        }

        private int Toll => _owner != null ? _owner.CurrentToll : 2;
        private bool Settled => _owner != null && _owner.TollSettledToday;

        private void Update()
        {
            // idle bounce: a tiny impatient hop
            _bobPhase += Time.deltaTime * 4f;
            if (_renderer != null)
                _renderer.transform.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.06f, 0f);

            if (_player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p == null) return;
                _player = p.transform;
            }

            Vector3 pp = _player.position;
            float dist = Vector2.Distance(pp, transform.position);

            // proximity demand
            if (!Settled && dist <= QuipRadius && Time.time >= _quipReadyAt)
            {
                _quipReadyAt = Time.time + QuipCooldown;
                string line = DemandLines[Random.Range(0, DemandLines.Length)];
                FloatingText.Show(transform.position + Vector3.up * 1.0f,
                    line + " (" + Toll + " obols)", ImpGold);
            }

            // walk-past detection: crossing the imp's line unpaid = refusal
            if (_hasLastX && !Settled && dist <= 3.2f
                && Mathf.Abs(pp.x - _lastPlayerX) < MaxPassStep
                && (pp.x - transform.position.x) * (_lastPlayerX - transform.position.x) < 0f)
            {
                Refuse(auto: true);
            }
            _lastPlayerX = pp.x;
            _hasLastX = true;
        }

        // ---- paying / refusing -------------------------------------------------------

        private void Pay()
        {
            if (_owner == null || Settled) return;
            var inv = Inventory.Instance;
            int toll = Toll;
            Vector3 at = transform.position + Vector3.up * 1.0f;

            if (inv != null && inv.Consume("coin", toll))
            {
                _owner.SettleToll(true);
                FloatingText.Show(at, "-" + toll + " obols. " + PaidLines[Random.Range(0, PaidLines.Length)], ImpGold);
                Bleeps.Play(BleepKind.Coin, 0.6f);
                Puffs.Burst(transform.position + Vector3.up * 0.4f, ImpGold, 5, 1.2f, 0.3f, 0.08f);
            }
            else
            {
                FloatingText.Show(at, "(needs " + toll + " obols)", UIStyle.Danger);
                Bleeps.Play(BleepKind.Denied, 0.7f);
            }
        }

        private void Refuse(bool auto)
        {
            if (_owner == null || Settled) return;
            _owner.SettleToll(false);
            _owner.ApplyTollRefusal();

            FloatingText.Show(transform.position + Vector3.up * 1.0f,
                auto ? "Hmph! Rude! *raspberry*" : "Fine! *raspberry*", UIStyle.Danger);
            Puffs.Burst(transform.position + Vector3.up * 0.5f, new Color(0.78f, 0.28f, 0.22f), 6, 1.4f, 0.3f, 0.09f);
        }

        // ---- IInteractable -------------------------------------------------------------

        public string PromptText => "Pay toll (" + Toll + " obols)";

        public bool CanInteract(GameObject actor) => _owner != null && !Settled;

        public void Interact(GameObject actor) => Pay();

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- ISelectable ----------------------------------------------------------------

        public string SelectableTitle => "Toll Imp";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            if (Settled)
            {
                into.Add(new SelectAction(_owner != null && _owner.TollPaidToday
                    ? "(paid up for today)" : "(it is sulking)", () => { }, false));
                return;
            }
            into.Add(new SelectAction("Pay toll (" + Toll + " obols)", Pay));
            into.Add(new SelectAction("Refuse", () => Refuse(auto: false)));
        }
    }
}
