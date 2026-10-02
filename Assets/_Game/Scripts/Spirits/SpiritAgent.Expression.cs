using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Muscle 03 expression layer of the spirit agent, kept in its own file
    /// to stay out of the main agent's way: the per-species 3-state
    /// animation read (SpiritAnimState), the naming-ceremony bow, and the
    /// silhouette shy fade-back. Hooks live in SpiritAgent.Update /
    /// TickVisuals / TickWander / TickStateGates.
    /// </summary>
    public partial class SpiritAgent
    {
        // ---- silhouette shyness tuning -------------------------------------------
        private const float ShyRadius = 3.2f;            // approach closer than this and it shies
        private const float ShyRetreatSeconds = 1.6f;    // drift back while fading
        private const float ShyRetreatSpeed = 1.1f;
        private const float ShyReappearSeconds = 2.4f;
        private const float ShyHiddenMinSeconds = 20f;   // scaled seconds out of sight
        private const float ShyHiddenMaxSeconds = 45f;
        private const float ShyCooldownSeconds = 6f;     // after reappearing, before it may shy again
        private const float ShyMinReappearDistance = 8f; // from the shepherd
        private const int ShyReappearTries = 8;

        private enum ShyPhase { None, Retreat, Hidden, Reappear }

        private ShyPhase _shyPhase = ShyPhase.None;
        private float _shyTimer;
        private float _shyHiddenFor;
        private float _shyReadyAt;
        private float _shyFade = 1f;          // alpha multiplier read by TickVisuals
        private Vector3 _shyAway;
        private bool _debugPinned;            // console: skip gate checks so a forced silhouette stays
        private static bool _shyHintShown;    // one teaching line per session

        // ---- naming-ceremony bow --------------------------------------------------
        private float _bowTimer = -1f;
        private float _bowDuration;

        // ---- animation state ------------------------------------------------------

        /// <summary>
        /// Happy / neutral / sad-sick read (muscle 03 verdict 8). A starving
        /// resident counts as sick even when its Spirit is still high.
        /// Silhouettes and runaways stay neutral (they keep their own motion).
        /// </summary>
        public SpiritAnimState AnimState
        {
            get
            {
                if (_state != SpiritState.Visitor && _state != SpiritState.Resident)
                    return SpiritAnimState.Neutral;

                var band = MoodBand;
                if (band == SpiritMoodBand.Low) return SpiritAnimState.SadSick;
                if (_state == SpiritState.Resident && _hunger01 >= 1f) return SpiritAnimState.SadSick;
                return band == SpiritMoodBand.Happy ? SpiritAnimState.Happy : SpiritAnimState.Neutral;
            }
        }

        /// <summary>The species' pose for the current state (Plain when not expressive).</summary>
        private SpiritPose CurrentPose
        {
            get
            {
                if (_species == null || _species.animProfile == null) return SpiritPose.Plain;
                if (_state != SpiritState.Visitor && _state != SpiritState.Resident) return SpiritPose.Plain;
                return _species.animProfile.Get(AnimState);
            }
        }

        // ---- bow ------------------------------------------------------------------

        /// <summary>
        /// A short respectful dip (naming ceremony). Paced against the
        /// ceremony's softened time scale so it always lasts about
        /// <paramref name="seconds"/> of real time.
        /// </summary>
        public void PlayBow(float seconds = 1.1f)
        {
            _bowTimer = 0f;
            _bowDuration = Mathf.Max(0.2f, seconds);
        }

        /// <summary>Advances the bow; returns 0..1 (down and back up).</summary>
        private float TickBow()
        {
            if (_bowTimer < 0f) return 0f;

            // dt / timeScale undoes the ceremony's time softening; paused (scale 0) stalls it.
            _bowTimer += Time.deltaTime / Mathf.Max(0.05f, Time.timeScale);
            float t = _bowTimer / _bowDuration;
            if (t >= 1f) { _bowTimer = -1f; return 0f; }
            return Mathf.SmoothStep(0f, 1f, Mathf.Sin(t * Mathf.PI));
        }

        // ---- silhouette shyness -----------------------------------------------------

        /// <summary>
        /// Silhouette shy fade-back (verdict 6). Approach one and it drifts
        /// away while fading, then reappears much later somewhere else on the
        /// border. It can never be touched (CanInteract is false for
        /// silhouettes) - the only way in is making the land it wants.
        /// Returns true while it owns movement (retreating / out of sight).
        /// </summary>
        private bool TickShyness()
        {
            float dt = Time.deltaTime;

            if (_state != SpiritState.Silhouette || _despawning || _ceremony)
            {
                // Became a visitor (or never shy): ease back to fully visible.
                _shyPhase = ShyPhase.None;
                if (_shyFade < 1f) _shyFade = Mathf.MoveTowards(_shyFade, 1f, dt);
                return false;
            }

            switch (_shyPhase)
            {
                case ShyPhase.None:
                {
                    _shyFade = 1f;
                    if (Time.time < _shyReadyAt) return false;
                    float dist = DistanceToPlayer();
                    if (float.IsPositiveInfinity(dist) || dist > ShyRadius) return false;

                    _shyPhase = ShyPhase.Retreat;
                    _shyTimer = 0f;
                    _hasTarget = false;
                    _shyAway = transform.position - _playerTransform.position;
                    _shyAway.z = 0f;
                    if (_shyAway.sqrMagnitude < 0.0001f)
                        _shyAway = (Vector3)Random.insideUnitCircle.normalized;
                    _shyAway.Normalize();

                    if (!_shyHintShown)
                    {
                        _shyHintShown = true;
                        AnimalFarm.UI.FloatingText.Show(transform.position + Vector3.up * 0.9f,
                            "(it shies away - it wants something from the land)", HerdGreyBlue);
                    }
                    return true;
                }

                case ShyPhase.Retreat:
                {
                    _shyTimer += dt;
                    float k = Mathf.Clamp01(_shyTimer / ShyRetreatSeconds);
                    _shyFade = 1f - Mathf.SmoothStep(0f, 1f, k);
                    transform.position += _shyAway * (ShyRetreatSpeed * dt);

                    if (k >= 1f)
                    {
                        Puffs.Burst(transform.position, new Color(0.45f, 0.5f, 0.7f), 5, 0.8f, 0.5f, 0.1f);
                        transform.position = PickShyReappearPoint();
                        _shyFade = 0f;
                        _shyPhase = ShyPhase.Hidden;
                        _shyTimer = 0f;
                        _shyHiddenFor = Random.Range(ShyHiddenMinSeconds, ShyHiddenMaxSeconds);
                        _hasTarget = false;
                        _idleTimer = Random.Range(1f, 3f);
                    }
                    return true;
                }

                case ShyPhase.Hidden:
                {
                    _shyFade = 0f;
                    _shyTimer += dt;
                    if (_shyTimer >= _shyHiddenFor)
                    {
                        _shyPhase = ShyPhase.Reappear;
                        _shyTimer = 0f;
                    }
                    return true;
                }

                default: // Reappear: fades in while pacing the border again
                {
                    _shyTimer += dt;
                    float k = Mathf.Clamp01(_shyTimer / ShyReappearSeconds);
                    _shyFade = Mathf.SmoothStep(0f, 1f, k);
                    if (k >= 1f)
                    {
                        _shyFade = 1f;
                        _shyPhase = ShyPhase.None;
                        _shyReadyAt = Time.time + ShyCooldownSeconds;
                    }
                    return false;
                }
            }
        }

        /// <summary>A border point well away from the shepherd and from here.</summary>
        private Vector3 PickShyReappearPoint()
        {
            var grid = TerrainGrid.Instance;
            if (grid == null)
                return transform.position + (Vector3)(Random.insideUnitCircle.normalized * 6f);

            Vector3 playerPos = _playerTransform != null ? _playerTransform.position : transform.position;
            Vector3 best = transform.position;
            float bestScore = -1f;
            for (int i = 0; i < ShyReappearTries; i++)
            {
                Vector3 p = grid.RandomUsableBorderPoint();
                float fromPlayer = (p - playerPos).magnitude;
                float fromHere = (p - transform.position).magnitude;
                float score = Mathf.Min(fromPlayer, ShyMinReappearDistance) + Mathf.Min(fromHere, 6f) * 0.5f;
                if (score > bestScore) { bestScore = score; best = p; }
                if (fromPlayer >= ShyMinReappearDistance && fromHere >= 6f) { best = p; break; }
            }
            best.z = 0f;
            return best;
        }

        // ---- console helpers ----------------------------------------------------------

        /// <summary>Console: keep this silhouette alive regardless of its Appear/Visit gates (shyness testing).</summary>
        public void Debug_PinSilhouette() => _debugPinned = true;
    }
}
