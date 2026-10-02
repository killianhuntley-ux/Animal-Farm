using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Master output safety net. A self-spawned controller finds the active
    /// AudioListener (re-checking twice a second, so a camera swap is
    /// followed) and parks an <see cref="AudioLimiterFilter"/> on its
    /// GameObject; that filter limits the final mix on the audio thread.
    /// The controller's Update reads the filter's plain-field stats on the
    /// main thread and writes loud moments, suspected high-frequency squeal
    /// and frame hitches into the AudioGuard diagnostic file together with
    /// the last 20 play requests, so a bad moment is attributed to sounds.
    /// </summary>
    public class AudioLimiter : MonoBehaviour
    {
        public static AudioLimiter Instance { get; private set; }

        /// <summary>Highest pre-limit peak seen this session (1.0 = full scale).</summary>
        public static float SessionMaxPeak;

        private const float LoudPeak = 0.85f;       // log pre-limit peaks at or above this
        private const float ClipPeak = 1.0f;        // true over-full-scale: also a console warning
        private const float SquealSeconds = 0.5f;
        private const float HitchSeconds = 0.15f;

        private AudioListener _listener;
        private AudioLimiterFilter _filter;
        private float _nextFind;
        private float _nextPeakLog;
        private float _nextClipWarn;
        private float _nextSquealLog;
        private float _nextHitchLog;

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioLimiter (runtime)");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioLimiter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SessionMaxPeak = 0f;
            AudioGuard.EnsureInit();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AudioGuard.Flush();
        }

        private void OnApplicationQuit()
        {
            AudioGuard.Flush();
        }

        private void Update()
        {
            AudioGuard.Tick();
            PollPanicKey();

            float now = Time.unscaledTime;
            if (now >= _nextFind)
            {
                _nextFind = now + 0.5f;
                EnsureFilter();
            }

            // A long frame starves the audio thread and can sound like a buzz: record it.
            float dt = Time.unscaledDeltaTime;
            if (dt > HitchSeconds && now >= _nextHitchLog)
            {
                _nextHitchLog = now + 1f;
                AudioGuard.LogEvent("hitch: frame took " + (dt * 1000f).ToString("0", CultureInfo.InvariantCulture)
                    + " ms (audio buffer underrun risk)");
            }

            if (_filter == null) return;

            _filter.TakeStats(out float peak, out float hotSeconds, out float rms, out float zcr);

            if (peak > SessionMaxPeak) SessionMaxPeak = peak;

            if (peak >= LoudPeak && now >= _nextPeakLog)
            {
                _nextPeakLog = now + 1f;
                AudioGuard.LastLimiterPeak = peak;
                AudioGuard.LastLimiterPeakAt = AudioGuard.SessionTime;
                string t = AudioGuard.SessionTime.ToString("0.000", CultureInfo.InvariantCulture);
                AudioGuard.LogEvent("limiter: pre-limit peak " + peak.ToString("0.00", CultureInfo.InvariantCulture)
                    + " at t=" + t + " (rms " + rms.ToString("0.00", CultureInfo.InvariantCulture)
                    + "); last 20 requests:\n" + AudioGuard.RecentDump(20));

                if (peak >= ClipPeak && now >= _nextClipWarn)
                {
                    _nextClipWarn = now + 3f;
                    Debug.LogWarning("[AudioLimiter] mix peaked at " + peak.ToString("0.00", CultureInfo.InvariantCulture)
                        + " (over full scale) at t=" + t + " - see " + (AudioGuard.DiagPath ?? "diag log"));
                }
            }

            if (hotSeconds >= SquealSeconds && now >= _nextSquealLog)
            {
                _nextSquealLog = now + 5f;
                string t = AudioGuard.SessionTime.ToString("0.000", CultureInfo.InvariantCulture);
                AudioGuard.LogEvent("SQUEAL suspected: sustained " + hotSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + " s of high zero-crossing rate (" + zcr.ToString("0.00", CultureInfo.InvariantCulture)
                    + "/sample) with rms " + rms.ToString("0.00", CultureInfo.InvariantCulture)
                    + " at t=" + t + "; last 20 requests:\n" + AudioGuard.RecentDump(20));
                Debug.LogWarning("[AudioLimiter] possible high-frequency squeal at t=" + t
                    + " - see " + (AudioGuard.DiagPath ?? "diag log"));
            }
        }

        /// <summary>
        /// Ctrl+M: panic toggle for ALL audio (guard kill + listener volume 0),
        /// so a screech can be stopped even with the console unreachable.
        /// Direct device read, so it honors UIInputLock.BlockDirectKeys.
        /// </summary>
        private static void PollPanicKey()
        {
            if (UIInputLock.BlockDirectKeys) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.mKey.wasPressedThisFrame) return;
            if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) return;

            AudioGuard.SetKilled(!AudioGuard.Killed);
            string msg = AudioGuard.Killed
                ? "AUDIO KILLED (Ctrl+M to restore)"
                : "Audio restored";
            Debug.Log("[AudioGuard] " + msg);
            AudioGuard.LogEvent("panic key: " + msg);

            var cam = Camera.main;
            if (cam != null)
            {
                var p = cam.transform.position;
                AnimalFarm.UI.FloatingText.Show(new Vector3(p.x, p.y, 0f), msg,
                    AudioGuard.Killed ? new Color(1f, 0.45f, 0.4f) : new Color(0.7f, 1f, 0.7f));
            }
        }

        /// <summary>Keeps exactly one filter alive, on the GameObject of the active listener.</summary>
        private void EnsureFilter()
        {
            var l = _listener;
            if (l == null || !l.isActiveAndEnabled)
                l = FindFirstObjectByType<AudioListener>();

            if (l == null)
            {
                if (_filter != null) Destroy(_filter);
                _filter = null;
                _listener = null;
                return;
            }

            if (l == _listener && _filter != null) return;

            if (_filter != null) Destroy(_filter);
            _listener = l;
            _filter = l.gameObject.GetComponent<AudioLimiterFilter>();
            if (_filter == null) _filter = l.gameObject.AddComponent<AudioLimiterFilter>();
            AudioGuard.LogEvent("limiter attached to listener on '" + l.gameObject.name + "'");
        }
    }

    /// <summary>
    /// The audio-thread half: a smooth peak limiter (threshold 0.7, ~0.3 ms
    /// attack, 150 ms release) followed by a hard clamp at +/-0.95. Runs in
    /// OnAudioFilterRead on the AudioListener's GameObject, so it processes
    /// the FINAL mix. AUDIO THREAD: no Unity API in OnAudioFilterRead - only
    /// plain/volatile/interlocked fields, read back by AudioLimiter.Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioLimiterFilter : MonoBehaviour
    {
        private const float Threshold = 0.7f;
        private const float Ceiling = 0.95f;

        // Set on the main thread (OnEnable) before the first block.
        private float _attackCoef = 0.07f;
        private float _releaseCoef = 0.00015f;
        private float _sampleRate = 48000f;

        // Audio-thread private.
        private float _gain = 1f;

        // Shared with the main thread.
        private int _peakBits;                 // max pre-limit peak since last read (float bits)
        private volatile float _lastRms;
        private volatile float _lastZcr;
        private volatile float _hotSeconds;    // consecutive seconds of "loud and very high-pitched"

        private void OnEnable()
        {
            _sampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            _attackCoef = 1f - (float)Math.Exp(-1.0 / (0.0003 * _sampleRate));
            _releaseCoef = 1f - (float)Math.Exp(-1.0 / (0.150 * _sampleRate));
            _gain = 1f;
        }

        /// <summary>Main thread: reads and clears the max peak; returns the latest squeal stats.</summary>
        public void TakeStats(out float peak, out float hotSeconds, out float rms, out float zcr)
        {
            int bits = Interlocked.Exchange(ref _peakBits, 0);
            peak = BitConverter.Int32BitsToSingle(bits);
            hotSeconds = _hotSeconds;
            rms = _lastRms;
            zcr = _lastZcr;
        }

        // AUDIO THREAD.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels < 1) return;
            int frames = data.Length / channels;
            if (frames <= 0) return;

            float g = _gain;
            float blockPeak = 0f;
            double sumSq = 0.0;
            int crossings = 0;
            int lastSign = 0;

            for (int f = 0; f < frames; f++)
            {
                int b = f * channels;

                float framePeak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[b + c];
                    if (a < 0f) a = -a;
                    if (a > framePeak) framePeak = a;
                }
                if (framePeak > blockPeak) blockPeak = framePeak;

                // Squeal analysis on channel 0 (dead band keeps hiss from counting).
                float x0 = data[b];
                sumSq += x0 * x0;
                int s = x0 > 0.02f ? 1 : (x0 < -0.02f ? -1 : 0);
                if (s != 0)
                {
                    if (lastSign != 0 && s != lastSign) crossings++;
                    lastSign = s;
                }

                // Gain follows the peak: fast down, slow back up.
                float target = framePeak > Threshold ? Threshold / framePeak : 1f;
                if (target < g) g += (target - g) * _attackCoef;
                else g += (target - g) * _releaseCoef;
                if (float.IsNaN(g) || float.IsInfinity(g)) g = 1f; // never let a bad sample poison the gain

                for (int c = 0; c < channels; c++)
                {
                    float o = data[b + c] * g;
                    // NaN-safe: anything not inside [-Ceiling, Ceiling] (incl. NaN) is clamped.
                    if (!(o <= Ceiling && o >= -Ceiling)) o = o > 0f ? Ceiling : (o < 0f ? -Ceiling : 0f);
                    data[b + c] = o;
                }
            }
            _gain = g;

            // Publish max pre-limit peak (positive floats order like their bit patterns).
            int newBits = BitConverter.SingleToInt32Bits(blockPeak);
            int cur;
            do
            {
                cur = Volatile.Read(ref _peakBits);
                if (newBits <= cur) break;
            } while (Interlocked.CompareExchange(ref _peakBits, newBits, cur) != cur);

            float rms = (float)Math.Sqrt(sumSq / frames);
            float zcr = crossings / (float)frames;
            _lastRms = rms;
            _lastZcr = zcr;

            // > ~2.9 kHz-equivalent crossing rate at real volume = candidate squeal.
            bool hot = zcr > 0.12f && rms > 0.15f;
            _hotSeconds = hot ? _hotSeconds + frames / _sampleRate : 0f;
        }
    }
}
