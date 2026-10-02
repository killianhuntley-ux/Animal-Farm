using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Global flags for code that reads input devices DIRECTLY (Keyboard.current
    /// etc.) and therefore bypasses GameInput.SetGameplayBlocked. Every direct
    /// reader must check these — typing "compete" in the console must never
    /// push a boulder or open the journal.
    /// </summary>
    public static class UIInputLock
    {
        /// <summary>A text field is capturing keystrokes (console, name prompt).</summary>
        public static bool TextInputActive;

        /// <summary>Any modal UI is open (toolbelt, seed picker, entry menus, journal).</summary>
        public static bool ModalOpen;

        /// <summary>
        /// A ceremony (naming, Styx crossing, weave rite) is playing. Ceremonies own
        /// the input block for their whole run: no other closer may release it.
        /// </summary>
        public static bool CeremonyActive =>
            AnimalFarm.Spirits.NamingCeremony.Running
            || AnimalFarm.Spirits.StyxCrossingCeremony.Running
            || AnimalFarm.Spirits.WeaveRiteCeremony.Running;

        /// <summary>A competition event is running (it owns the world and the input block).</summary>
        public static bool EventActive =>
            AnimalFarm.Competitions.CompetitionManager.Instance != null
            && AnimalFarm.Competitions.CompetitionManager.Instance.EventRunning;

        /// <summary>Any owner (modal, text field, ceremony, competition event) still needs gameplay blocked.</summary>
        public static bool AnyOwnerHolds => ModalOpen || TextInputActive || CeremonyActive || EventActive;

        /// <summary>
        /// Owners other than a modal that just cleared its own ModalOpen flag: a
        /// focused text field or a running ceremony. Closers check this (plus
        /// pause) before calling SetGameplayBlocked(false).
        /// </summary>
        public static bool NonModalOwnerHolds => TextInputActive || CeremonyActive;

        /// <summary>True when direct gameplay key reads must be ignored.</summary>
        public static bool BlockDirectKeys => TextInputActive || ModalOpen;

        // Statics survive play-mode entry when domain reload is disabled: never
        // start a session with a stale lock left over from the last one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            TextInputActive = false;
            ModalOpen = false;
        }
    }
}
