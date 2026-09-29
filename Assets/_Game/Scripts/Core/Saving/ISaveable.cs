namespace AnimalFarm.Core.Saving
{
    /// <summary>
    /// Anything that persists in the save file. Register happens automatically:
    /// SaveSystem scans the scene for MonoBehaviours implementing this.
    /// Capture/Restore exchange JSON fragments (use JsonUtility with a private
    /// [System.Serializable] state struct).
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Stable unique key, e.g. "clock", "shepherd".</summary>
        string SaveKey { get; }

        string Capture();

        void Restore(string json);
    }
}
