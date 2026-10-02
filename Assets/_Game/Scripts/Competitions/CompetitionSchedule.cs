using System.Collections.Generic;
using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Competitions
{
    /// <summary>One named festival event on a fixed season day.</summary>
    public struct FestivalEvent
    {
        public string name;
        public string format;    // "Boulder Trial" or "Sprint"
        public int season;       // index into GameCalendar's season table
        public int dayOfSeason;  // 1..GameCalendar.DaysPerSeason
    }

    /// <summary>A placeholder rival shepherd and the spirit they bring.</summary>
    public struct RivalEntrant
    {
        public string shepherd;
        public string species;
        public string flavor;
    }

    /// <summary>An upcoming event with its countdown, as the board lists it.</summary>
    public struct UpcomingEvent
    {
        public FestivalEvent festival;
        public int index;          // position in the schedule (seeds the rival line-up)
        public int daysUntil;      // 0 = today
        public string seasonName;
    }

    /// <summary>
    /// The held-competition schedule (owner verdict 2026-10-01: while competitions
    /// are held the board is a read-only "what is coming up and who is competing"
    /// notice). Everything here is DETERMINISTIC from the underworld calendar -
    /// nothing is saved. Two festival events per season: one Boulder Trial format
    /// and one race ("Sprint" - the race is NOT called The Crossing; that name is
    /// retired to the lifecycle rework). Names and rivals are placeholders
    /// (ASSUMPTION) for the owner to rename.
    /// </summary>
    public static class CompetitionSchedule
    {
        /// <summary>Shown on the board while competitions are held.</summary>
        public const string HeldLine = "Entries open with the festival season.";

        private static readonly FestivalEvent[] Events =
        {
            new FestivalEvent { name = "Hushstep Sprint",     format = "Sprint",        season = 0, dayOfSeason = 5 },
            new FestivalEvent { name = "Quietstone Lift",     format = "Boulder Trial", season = 0, dayOfSeason = 12 },
            new FestivalEvent { name = "Puddle Dash",         format = "Sprint",        season = 1, dayOfSeason = 4 },
            new FestivalEvent { name = "Soggy Slab Trial",    format = "Boulder Trial", season = 1, dayOfSeason = 11 },
            new FestivalEvent { name = "Ember Sprint",        format = "Sprint",        season = 2, dayOfSeason = 6 },
            new FestivalEvent { name = "Cinderstone Heave",   format = "Boulder Trial", season = 2, dayOfSeason = 13 },
            new FestivalEvent { name = "Lanternlight Dash",   format = "Sprint",        season = 3, dayOfSeason = 3 },
            new FestivalEvent { name = "Dimstone Trial",      format = "Boulder Trial", season = 3, dayOfSeason = 10 }
        };

        private static readonly RivalEntrant[] Rivals =
        {
            new RivalEntrant { shepherd = "Marrow Pell",    species = "Bansheep",     flavor = "Heaves first, apologises later." },
            new RivalEntrant { shepherd = "Ostler Tam",     species = "Wrabbit",      flavor = "Has never once lost a footrace. Says so often." },
            new RivalEntrant { shepherd = "Sister Dunmoth", species = "Phantomoth",   flavor = "Arrives late, glowing, and unbothered." },
            new RivalEntrant { shepherd = "Old Barrow Wick", species = "Mausoleum",   flavor = "Slow, steady, and quietly unbeatable." },
            new RivalEntrant { shepherd = "Neve Mirefoot",  species = "Reedhen",      flavor = "Trains in the shallows and sulks about it." },
            new RivalEntrant { shepherd = "Cobb Ashgrove",  species = "Wailpertinger", flavor = "Warms up by screaming at the crowd." },
            new RivalEntrant { shepherd = "Tilly Gravestitch", species = "Mothmaus",  flavor = "Small, sharp, and wildly overconfident." },
            new RivalEntrant { shepherd = "Hob Lowlantern", species = "Sloughling",   flavor = "Mostly here for the snacks. Wins anyway." }
        };

        public const int RivalsPerEvent = 3;

        public static int EventCount => Events.Length;

        // ---- festival day (entry mode) ---------------------------------------------

        /// <summary>Entries on an event day are open from this hour (ASSUMPTION).</summary>
        public const float WindowOpenHour = 8f;
        /// <summary>...until this hour (ASSUMPTION).</summary>
        public const float WindowCloseHour = 18f;

        private static int _forcedIndex = -1; // console 'festival now': pretend this event is today

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _forcedIndex = -1; }

        public static bool Forced => _forcedIndex >= 0;

        public static FestivalEvent Get(int index) => Events[Mathf.Clamp(index, 0, Events.Length - 1)];

        /// <summary>
        /// True when a scheduled event falls on today's calendar date (or one is
        /// forced from the console); <paramref name="index"/> is its schedule slot.
        /// </summary>
        public static bool TryGetToday(out int index)
        {
            index = -1;
            if (_forcedIndex >= 0) { index = _forcedIndex; return true; }

            var cal = GameCalendar.Instance;
            if (cal == null) return false;
            int season = cal.SeasonIndex, day = cal.DayOfSeason;
            for (int i = 0; i < Events.Length; i++)
            {
                if (Events[i].season == season && Events[i].dayOfSeason == day) { index = i; return true; }
            }
            return false;
        }

        /// <summary>
        /// An event is today AND we are inside the entry window (a forced
        /// festival ignores the window so it can be tested at any hour).
        /// </summary>
        public static bool EntriesOpenNow(out int index)
        {
            if (!TryGetToday(out index)) return false;
            if (_forcedIndex >= 0) return true;
            var clock = GameClock.Instance;
            return clock != null && clock.Hours >= WindowOpenHour && clock.Hours < WindowCloseHour;
        }

        /// <summary>Console: force today to be an event day (the soonest event) or clear it.</summary>
        public static string Debug_Force(bool on)
        {
            if (!on) { _forcedIndex = -1; return "Festival cleared; the calendar decides again."; }
            var next = NextEvents(1);
            if (next.Count == 0) return "No festival calendar available.";
            _forcedIndex = next[0].index;
            return "Today is now a festival day: " + Events[_forcedIndex].name
                + " (" + Events[_forcedIndex].format + "), entries open at any hour.";
        }

        /// <summary>Console: one-line status of today's festival state.</summary>
        public static string Debug_Status()
        {
            if (!TryGetToday(out int i)) return "No festival today.";
            return "Festival today: " + Events[i].name + " (" + Events[i].format + "), entries "
                + (EntriesOpenNow(out _) ? "OPEN" : "closed (open " + WindowOpenHour.ToString("0") + ":00-"
                    + WindowCloseHour.ToString("0") + ":00)") + (Forced ? " [forced]" : "");
        }

        /// <summary>The rival line-up for a schedule index (stable across saves).</summary>
        public static RivalEntrant GetRival(int eventIndex, int slot)
        {
            int i = ((eventIndex * RivalsPerEvent + slot) % Rivals.Length + Rivals.Length) % Rivals.Length;
            return Rivals[i];
        }

        /// <summary>
        /// The next <paramref name="max"/> events, soonest first, counted from
        /// today's calendar date (an event happening today is listed with 0 days).
        /// Empty when there is no calendar.
        /// </summary>
        public static List<UpcomingEvent> NextEvents(int max)
        {
            var list = new List<UpcomingEvent>();
            var cal = GameCalendar.Instance;
            if (cal == null) return list;

            int seasons = cal.SeasonCount;
            int cycle = seasons * GameCalendar.DaysPerSeason;
            int today = cal.SeasonIndex * GameCalendar.DaysPerSeason + cal.DayOfSeason;

            for (int i = 0; i < Events.Length; i++)
            {
                var e = Events[i];
                if (e.season < 0 || e.season >= seasons) continue;
                int at = e.season * GameCalendar.DaysPerSeason + e.dayOfSeason;
                int until = ((at - today) % cycle + cycle) % cycle;
                list.Add(new UpcomingEvent
                {
                    festival = e,
                    index = i,
                    daysUntil = until,
                    seasonName = cal.GetSeasonName(e.season)
                });
            }

            list.Sort((a, b) => a.daysUntil.CompareTo(b.daysUntil));
            if (max > 0 && list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>"today", "tomorrow" or "in N days".</summary>
        public static string CountdownText(int daysUntil)
        {
            if (daysUntil <= 0) return "today";
            if (daysUntil == 1) return "tomorrow";
            return "in " + daysUntil + " days";
        }
    }
}
