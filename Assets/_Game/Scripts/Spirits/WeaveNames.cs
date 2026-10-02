using System.Text;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Name echo (muscle 06, verdict 3): builds the suggested blend of the two
    /// parents' names that the naming ceremony pre-fills after a weave. Pure
    /// ASCII, capitalized, kept short; the player can always edit it.
    /// </summary>
    public static class WeaveNames
    {
        private const int MinLen = 3;
        private const int MaxLen = 10;
        private const int MinPart = 2;
        private const int MaxPart = 5;

        /// <summary>
        /// Blend of two parent names: the head of one plus the tail of the
        /// other (which parent leads is a coin flip). A blank name stands in
        /// as a random pool name; if BOTH are blank returns null so the
        /// ceremony picks its own random suggestion.
        /// </summary>
        public static string Blend(string nameA, string nameB)
        {
            string a = Clean(nameA);
            string b = Clean(nameB);
            if (a.Length == 0 && b.Length == 0) return null;
            if (a.Length < 2) a = Clean(NamePromptUI.RandomSuggestion());
            if (b.Length < 2) b = Clean(NamePromptUI.RandomSuggestion());
            if (a.Length == 0 || b.Length == 0) return null;

            if (Random.value < 0.5f) { string t = a; a = b; b = t; }

            string best = Compose(a, b);
            // A blend that just reproduces one parent is no echo at all: flip the order once.
            if (SameName(best, a) || SameName(best, b))
            {
                string flipped = Compose(b, a);
                if (!SameName(flipped, a) && !SameName(flipped, b)) best = flipped;
            }
            return best;
        }

        private static string Compose(string lead, string follow)
        {
            int headLen = Mathf.Clamp((lead.Length + 1) / 2, MinPart, Mathf.Min(MaxPart, lead.Length));
            int tailLen = Mathf.Clamp(follow.Length / 2, MinPart, Mathf.Min(MaxPart, follow.Length));

            string head = lead.Substring(0, headLen);
            string tail = follow.Substring(follow.Length - tailLen);

            // Avoid a doubled seam letter ("Pipp" + "pin" -> "Pippin", not "Pipppin").
            if (head.Length > 0 && tail.Length > 0
                && char.ToLowerInvariant(head[head.Length - 1]) == char.ToLowerInvariant(tail[0]))
                tail = tail.Substring(1);

            string merged = (head + tail).ToLowerInvariant();

            // Too long: trim the tail first, then the head.
            while (merged.Length > MaxLen && tail.Length > 1)
            {
                tail = tail.Substring(1);
                merged = (head + tail).ToLowerInvariant();
            }
            if (merged.Length > MaxLen) merged = merged.Substring(0, MaxLen);

            // Too short: borrow one more letter from the lead's middle.
            if (merged.Length < MinLen && lead.Length > headLen)
                merged = (lead.Substring(0, Mathf.Min(lead.Length, headLen + 1)) + tail).ToLowerInvariant();

            if (merged.Length == 0) return null;
            return char.ToUpperInvariant(merged[0]) + merged.Substring(1);
        }

        /// <summary>First word, ASCII letters only ("Sir Pip-Pip!" -> "Sir"). Empty if none.</summary>
        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            bool started = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (letter) { sb.Append(c); started = true; }
                else if (started && (c == ' ' || c == '-')) break; // first word only
            }
            return sb.ToString();
        }

        private static bool SameName(string x, string y) =>
            x != null && y != null && string.Equals(x, y, System.StringComparison.OrdinalIgnoreCase);
    }
}
