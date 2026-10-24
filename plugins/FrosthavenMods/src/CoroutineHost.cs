using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A MonoBehaviour to run coroutines on. BepInEx's own plugin object is no good for it here: the game takes that
    /// object down soon after startup (its Update never runs either; see devtools/), and a coroutine started on it never
    /// ticks, so every "give the lent clip back once the state ends" ran only when the next animation request came and
    /// cut the roar short. In a scenario the game's own choreographer carries them (it lives as long as the scenario);
    /// elsewhere an object of ours does, hidden, kept across scenes, and made again if the game ever takes it down.
    /// </summary>
    internal sealed class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost _host;

        internal static MonoBehaviour Get()
        {
            Choreographer scenario = Choreographer.s_Choreographer;
            if (scenario != null && scenario.isActiveAndEnabled) return scenario;
            if (_host != null && _host.isActiveAndEnabled) return _host;
            var go = new GameObject("FrosthavenMods coroutines") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _host = go.AddComponent<CoroutineHost>();
            return _host;
        }
    }
}
