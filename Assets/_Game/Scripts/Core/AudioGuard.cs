using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>The mixer groups every synthesized sound belongs to.</summary>
    public enum AudioBus { Music, Ambience, Sfx, Voice }

    /// <summary>
    /// Central gate for every synthesized one-shot / loop start (Bleeps,
    /// SpiritVoice, AmbientMusic, rain, Charon's chord). Nothing plays
    /// unless <see cref="TryPlay"/> says yes. It:
    ///   - rejects plays while a bus is muted, another bus is soloed, or the
    ///     global panic kill (Ctrl+M) is on;
    ///   - rejects retriggers of the same key inside a min interval, caps
    ///     accepted one-shots per frame, per key per second and per bus per
    ///     second (so a burst of 25 simultaneous chirps becomes a handful);
    ///   - records EVERY request (accepted or not, with the reason) in a
    ///     ring buffer, flags floods (more than 8 requests of one key in 1 s)
    ///     with a LogWarning carrying the call stack, and writes a session
    ///     diagnostic file (persistentDataPath/audio_diag_*.log): every
    ///     request for the first 45 s, plus flood/limiter events all session.
    /// Also owns per-bus mute/solo and the Voice / Ambience volume prefs.
    /// Main-thread only; the audio-thread limiter lives in AudioLimiter.
    /// </summary>
    public static class AudioGuard
    {
        /// <summary>One recorded play request.</summary>
        public struct Entry
        {
            public float time;      // Time.unscaledTime (for window counts)
            public float session;   // seconds since the session started
            public int frame;
            public AudioBus bus;
            public string key;
            public float volume;
            public bool accepted;
            public string reason;   // "ok" or the rejection reason
        }

        private class KeyState
        {
            public float lastAccept = -999f;
            public readonly float[] req = NewRing(FloodRequests);       // recent request times
            public int reqPos;
            public readonly float[] acc = NewRing(MaxPerKeyPerSecond);  // recent accept times
            public int accPos;
            public float lastWarn = -999f;
        }

        // ---- tuning ----
        public const float DefaultMinInterval = 0.06f;
        private const int MaxPerFrame = 4;          // accepted Sfx/Voice one-shots per frame
        private const int MaxPerKeyPerSecond = 8;
        private const int FloodRequests = 8;        // more than this per second = flood
        private const float FloodWarnCooldown = 10f;
        private const int RingSize = 400;
        private const float DiagWindowSeconds = 45f;
        private const float FlushEverySeconds = 2f;
        private const int MaxFileLinesPerSecond = 200;

        // Per-bus global gap between accepted plays, and accepted plays per second (0 = unlimited).
        private static readonly float[] BusGap = { 0f, 0f, 0f, 0.12f };
        private static readonly int[] BusCapPerSecond = { 0, 0, 24, 6 };

        // ---- state ----
        private static readonly Entry[] _ring = new Entry[RingSize];
        private static int _ringHead;
        private static int _ringCount;
        private static readonly Dictionary<string, KeyState> _keys = new Dictionary<string, KeyState>();
        private static readonly bool[] _muted = new bool[4];
        private static int _solo = -1;
        private static bool _kill;
        private static readonly Queue<float>[] _busAccepts =
            { new Queue<float>(), new Queue<float>(), new Queue<float>(), new Queue<float>() };
        private static readonly float[] _busLastAccept = { -999f, -999f, -999f, -999f };
        private static int _capFrame = -1;
        private static int _capCount;

        private static bool _initialized;
        private static float _t0;
        private static bool _windowOpen;
        private static string _diagPath;
        private static readonly StringBuilder _sb = new StringBuilder(8192);
        private static float _lastFlushReal;
        private static int _lineSecond = -1;
        private static int _lineCount;
        private static int _suppressed;

        /// <summary>Full path of this session's diagnostic file (null if it could not be created).</summary>
        public static string DiagPath => _diagPath;

        /// <summary>Seconds since the audio session started (real time).</summary>
        public static float SessionTime => Time.realtimeSinceStartup - _t0;

        /// <summary>Last pre-limit peak the output limiter reported (set by AudioLimiter).</summary>
        public static float LastLimiterPeak;

        /// <summary>Session time of that peak.</summary>
        public static float LastLimiterPeakAt = -1f;

        // ------------------------------------------------------- Lifecycle

        private static float[] NewRing(int n)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = -999f;
            return a;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Enter-play-mode without domain reload keeps statics alive: start clean.
            _initialized = false;
            _windowOpen = false;
            _ringHead = 0;
            _ringCount = 0;
            _keys.Clear();
            Array.Clear(_muted, 0, _muted.Length);
            _solo = -1;
            _kill = false;
            for (int i = 0; i < 4; i++) { _busAccepts[i].Clear(); _busLastAccept[i] = -999f; }
            _capFrame = -1;
            _capCount = 0;
            _sb.Length = 0;
            _lineSecond = -1;
            _lineCount = 0;
            _suppressed = 0;
            _diagPath = null;
            LastLimiterPeak = 0f;
            LastLimiterPeakAt = -1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInit();
        }

        /// <summary>Opens the session (log file, quit hook). Safe to call repeatedly.</summary>
        public static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            _t0 = Time.realtimeSinceStartup;
            _lastFlushReal = _t0;
            _windowOpen = true;
            AudioListener.volume = 1f; // clear any stale panic kill

            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;

            try
            {
                string name = "audio_diag_"
                    + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log";
                _diagPath = Path.Combine(Application.persistentDataPath, name);
                File.WriteAllText(_diagPath,
                    "AudioGuard diagnostic. Every play request for the first "
                    + DiagWindowSeconds.ToString("0", CultureInfo.InvariantCulture)
                    + " s, then only flood/limiter events.\n"
                    + "columns: session-seconds frame bus key volume result\n");
            }
            catch (Exception e)
            {
                _diagPath = null;
                Debug.LogWarning("[AudioGuard] could not create diagnostic file: " + e.Message);
            }

            Debug.Log("[AudioGuard] diagnostic log: " + (_diagPath ?? "(unavailable)"));
        }

        private static void OnQuitting()
        {
            Flush();
        }

        /// <summary>Called every frame by AudioLimiter: closes the 45 s window and flushes.</summary>
        public static void Tick()
        {
            if (!_initialized) return;
            float real = Time.realtimeSinceStartup;
            if (_windowOpen && real - _t0 >= DiagWindowSeconds)
            {
                _windowOpen = false;
                AppendLineRaw("--- 45 s request window closed; only flood / limiter events from here ---");
                Flush();
            }
            else if (_sb.Length > 0 && real - _lastFlushReal >= FlushEverySeconds)
            {
                Flush();
            }
        }

        /// <summary>Writes buffered diagnostic text to the file.</summary>
        public static void Flush()
        {
            _lastFlushReal = Time.realtimeSinceStartup;
            if (_sb.Length == 0 || _diagPath == null) { _sb.Length = 0; return; }
            try { File.AppendAllText(_diagPath, _sb.ToString()); }
            catch { /* diagnostics must never throw */ }
            _sb.Length = 0;
        }

        private static void AppendLineRaw(string line)
        {
            _sb.Append(line).Append('\n');
        }

        /// <summary>
        /// Logs a non-request event (limiter peak, squeal, hitch, flood) into the
        /// diagnostic file for the whole session. Main thread only.
        /// </summary>
        public static void LogEvent(string text)
        {
            if (!_initialized) EnsureInit();
            AppendLineRaw(SessionTime.ToString("0.000", CultureInfo.InvariantCulture) + "s EVENT " + text);
            if (!_windowOpen) Flush(); // after the window every event goes straight to disk
        }

        // ------------------------------------------------------------ Gate

        /// <summary>
        /// Asks permission to start a sound. True = go ahead and play it.
        /// <paramref name="minInterval"/> below zero uses the 0.06 s default;
        /// pass 0 for sounds that may legitimately repeat faster (never for SFX bursts).
        /// </summary>
        public static bool TryPlay(AudioBus bus, string key, float volume, float minInterval = -1f)
        {
            if (!Application.isPlaying) return false;
            EnsureInit();
            if (key == null) key = "?";

            float now = Time.unscaledTime;
            int frame = Time.frameCount;
            int b = (int)bus;

            if (!_keys.TryGetValue(key, out var ks))
            {
                ks = new KeyState();
                _keys[key] = ks;
            }

            // Flood tracking counts EVERY request, accepted or not.
            float oldest = ks.req[ks.reqPos];
            ks.req[ks.reqPos] = now;
            ks.reqPos = (ks.reqPos + 1) % ks.req.Length;
            if (now - oldest <= 1f) WarnFlood(bus, key, ks, now);

            string reason = null;
            if (_kill) reason = "kill";
            else if (_muted[b]) reason = "bus-muted";
            else if (_solo >= 0 && _solo != b) reason = "solo";
            else
            {
                float gap = minInterval >= 0f ? minInterval : DefaultMinInterval;
                bool oneShotBus = bus == AudioBus.Sfx || bus == AudioBus.Voice;
                var q = _busAccepts[b];
                while (q.Count > 0 && now - q.Peek() > 1f) q.Dequeue();

                if (now - ks.lastAccept < gap) reason = "retrigger";
                else if (BusGap[b] > 0f && now - _busLastAccept[b] < BusGap[b]) reason = "bus-gap";
                else if (oneShotBus && frame == _capFrame && _capCount >= MaxPerFrame) reason = "frame-cap";
                else if (now - ks.acc[ks.accPos] <= 1f) reason = "key-rate";
                else if (BusCapPerSecond[b] > 0 && q.Count >= BusCapPerSecond[b]) reason = "bus-rate";
            }

            bool accepted = reason == null;
            if (accepted)
            {
                ks.lastAccept = now;
                ks.acc[ks.accPos] = now;
                ks.accPos = (ks.accPos + 1) % ks.acc.Length;
                _busAccepts[b].Enqueue(now);
                _busLastAccept[b] = now;
                if (frame != _capFrame) { _capFrame = frame; _capCount = 0; }
                _capCount++;
            }

            Record(now, frame, bus, key, volume, accepted, accepted ? "ok" : reason);
            return accepted;
        }

        private static void Record(float now, int frame, AudioBus bus, string key,
            float volume, bool accepted, string reason)
        {
            var e = new Entry
            {
                time = now,
                session = SessionTime,
                frame = frame,
                bus = bus,
                key = key,
                volume = volume,
                accepted = accepted,
                reason = reason
            };
            _ring[_ringHead] = e;
            _ringHead = (_ringHead + 1) % RingSize;
            if (_ringCount < RingSize) _ringCount++;

            if (_windowOpen) WriteRequestLine(e);
        }

        private static void WriteRequestLine(Entry e)
        {
            // A runaway flood must not make the log itself a problem: cap lines per second.
            int sec = (int)e.session;
            if (sec != _lineSecond)
            {
                if (_suppressed > 0)
                    AppendLineRaw("... " + _suppressed + " request lines suppressed (>" + MaxFileLinesPerSecond + "/s)");
                _suppressed = 0;
                _lineSecond = sec;
                _lineCount = 0;
            }
            if (_lineCount >= MaxFileLinesPerSecond) { _suppressed++; return; }
            _lineCount++;
            AppendLineRaw(FormatEntry(e));
        }

        private static void WarnFlood(AudioBus bus, string key, KeyState ks, float now)
        {
            if (now - ks.lastWarn < FloodWarnCooldown) return;
            ks.lastWarn = now;
            string msg = "[AudioGuard] FLOOD: key '" + key + "' (" + bus + ") got more than "
                + FloodRequests + " play requests in 1 s. Call site:\n" + Environment.StackTrace;
            Debug.LogWarning(msg);
            LogEvent("FLOOD key=" + key + " bus=" + bus + "\n" + Environment.StackTrace);
        }

        // ------------------------------------------------------ Mute / solo

        /// <summary>True when the bus may currently be heard (no kill, mute, or other-bus solo).</summary>
        public static bool IsAudible(AudioBus bus)
        {
            if (_kill || _muted[(int)bus]) return false;
            return _solo < 0 || _solo == (int)bus;
        }

        public static bool IsMuted(AudioBus bus) => _muted[(int)bus];

        public static void SetMuted(AudioBus bus, bool muted) => _muted[(int)bus] = muted;

        public static void MuteAll()
        {
            for (int i = 0; i < _muted.Length; i++) _muted[i] = true;
        }

        /// <summary>Clears every mute, the solo, and the panic kill.</summary>
        public static void UnmuteAll()
        {
            Array.Clear(_muted, 0, _muted.Length);
            _solo = -1;
            SetKilled(false);
        }

        /// <summary>The soloed bus, or null when nothing is soloed.</summary>
        public static AudioBus? SoloBus => _solo < 0 ? (AudioBus?)null : (AudioBus)_solo;

        public static void SetSolo(AudioBus? bus) => _solo = bus.HasValue ? (int)bus.Value : -1;

        /// <summary>Global panic kill (Ctrl+M): silences every bus AND the listener itself.</summary>
        public static bool Killed => _kill;

        public static void SetKilled(bool killed)
        {
            _kill = killed;
            AudioListener.volume = killed ? 0f : 1f; // also catches any sound that skips the gate
        }

        // ---------------------------------------------------------- Volumes

        private const string VoiceVolumePrefKey = "af_voice_volume";
        private const string AmbienceVolumePrefKey = "af_ambience_volume";
        private static float _voiceVolume = 1f;
        private static float _ambienceVolume = 1f;
        private static bool _volumesLoaded;

        private static void LoadVolumesOnce()
        {
            if (_volumesLoaded) return;
            _volumesLoaded = true;
            _voiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceVolumePrefKey, 1f));
            _ambienceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(AmbienceVolumePrefKey, 1f));
        }

        /// <summary>Master spirit-voice volume (0-1, default 1). PlayerPrefs-persisted.</summary>
        public static float VoiceVolume
        {
            get { LoadVolumesOnce(); return _voiceVolume; }
            set
            {
                LoadVolumesOnce();
                _voiceVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VoiceVolumePrefKey, _voiceVolume);
            }
        }

        /// <summary>Master ambience (rain) volume (0-1, default 1). PlayerPrefs-persisted.</summary>
        public static float AmbienceVolume
        {
            get { LoadVolumesOnce(); return _ambienceVolume; }
            set
            {
                LoadVolumesOnce();
                _ambienceVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(AmbienceVolumePrefKey, _ambienceVolume);
            }
        }

        /// <summary>Volume setting for a bus (Music / Sfx live on AmbientMusic / Bleeps).</summary>
        public static float GetBusVolume(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return AmbientMusic.MusicVolume;
                case AudioBus.Sfx: return Bleeps.SfxVolume;
                case AudioBus.Voice: return VoiceVolume;
                default: return AmbienceVolume;
            }
        }

        // -------------------------------------------------------- Inspection

        /// <summary>Accepted / rejected request counts over the last <paramref name="seconds"/>.</summary>
        public static void CountsInLast(float seconds, out int accepted, out int rejected)
        {
            accepted = 0;
            rejected = 0;
            float cutoff = Time.unscaledTime - seconds;
            for (int i = 0; i < _ringCount; i++)
            {
                int idx = (_ringHead - 1 - i + RingSize * 2) % RingSize;
                var e = _ring[idx];
                if (e.time < cutoff) break;
                if (e.accepted) accepted++; else rejected++;
            }
        }

        /// <summary>Appends the newest <paramref name="count"/> entries (oldest first) to <paramref name="into"/>.</summary>
        public static void GetRecent(int count, List<Entry> into)
        {
            int n = Mathf.Min(count, _ringCount);
            for (int i = n - 1; i >= 0; i--)
            {
                int idx = (_ringHead - 1 - i + RingSize * 2) % RingSize;
                into.Add(_ring[idx]);
            }
        }

        /// <summary>Multi-line dump of the newest entries, for limiter events.</summary>
        public static string RecentDump(int count)
        {
            var list = new List<Entry>(count);
            GetRecent(count, list);
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
                sb.Append("    ").Append(FormatEntry(list[i])).Append('\n');
            return sb.ToString();
        }

        public static string FormatEntry(Entry e)
        {
            return e.session.ToString("0.000", CultureInfo.InvariantCulture) + "s f" + e.frame
                + " " + e.bus + " " + e.key
                + " v=" + e.volume.ToString("0.00", CultureInfo.InvariantCulture)
                + " " + (e.accepted ? "OK" : "REJ:" + e.reason);
        }
    }
}
