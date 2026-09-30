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

        /// <summary>True when direct gameplay key reads must be ignored.</summary>
        public static bool BlockDirectKeys => TextInputActive || ModalOpen;
    }
}
