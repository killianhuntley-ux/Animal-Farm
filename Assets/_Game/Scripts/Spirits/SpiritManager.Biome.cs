using AnimalFarm.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Rain half of the SpiritManager (muscle 02 verdict 5): hooks
    /// WeatherManager.RainChanged and tells every spirit, each with its own
    /// random delay so a field of spirits never chirps in unison (and the
    /// SpiritVoice gate never has to eat a pile-up). What each species DOES
    /// about rain lives on the agent (SpiritAgent.Biome.cs).
    /// </summary>
    public partial class SpiritManager
    {
        private const float RainReactMinDelay = 0.4f;
        private const float RainReactMaxDelay = 5f;

        private bool _rainHooked;

        private void Start()
        {
            HookRain();
        }

        private void HookRain()
        {
            if (_rainHooked) return;
            var weather = WeatherManager.GetOrCreate();
            if (weather == null) return;
            weather.RainChanged += OnRainChanged;
            _rainHooked = true;
        }

        private void UnhookRain()
        {
            if (!_rainHooked) return;
            _rainHooked = false;
            if (WeatherManager.Instance != null)
                WeatherManager.Instance.RainChanged -= OnRainChanged;
        }

        private void OnRainChanged(bool raining)
        {
            for (int i = 0; i < _spirits.Count; i++)
            {
                var a = _spirits[i];
                if (a != null) a.NotifyRain(raining, Random.Range(RainReactMinDelay, RainReactMaxDelay));
            }
        }
    }
}
