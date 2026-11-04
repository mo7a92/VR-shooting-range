using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRRange.Core
{
    /// <summary>
    /// Places the "how to play" sign (a prefab generated into Assets/Generated/Resources by the editor setup) on the
    /// counter when the range scene loads. Loading it at runtime keeps the committed scene and its baked lightmaps
    /// untouched; the sign is small, unlit by lightmaps (it uses probes) and is skipped silently when the prefab is missing.
    /// </summary>
    public static class HowToPlaySign
    {
        public const string ResourceName = "HowToPlaySign";
        public const string SceneName = "ShootingRange";
        public static readonly Vector3 CounterPosition = new Vector3(1.18f, 1.2f, -3.3f);
        public static readonly Vector3 CounterEuler = new Vector3(12f, 180f, 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName) return;
            Place();
        }

        /// <summary>Instantiates the sign if the prefab exists and no sign is in the scene yet. Returns the instance (or null).</summary>
        public static GameObject Place()
        {
            var existing = GameObject.Find(ResourceName);
            if (existing != null) return existing;
            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, CounterPosition, Quaternion.Euler(CounterEuler));
            go.name = ResourceName;
            return go;
        }
    }
}
