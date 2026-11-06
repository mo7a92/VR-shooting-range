using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRRange.Audio;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// The jazz radio: Poly Haven "Boombox" (CC0) on the left end of the weapons counter, behind the ammo box, with a 3D
    /// AudioSource, the <see cref="JazzRadio"/> playlist (Kevin MacLeod, CC BY 4.0, Assets/Audio/Music), a grip interactable
    /// and a small display. Not static: it is lit by light probes, so placing it needs no re-bake. Idempotent.
    /// </summary>
    public static class RadioBuilder
    {
        public const string Name = "JazzRadio";
        static readonly (string file, string title)[] s_Tracks =
        {
            ("jazz_bossa_antigua", "Bossa Antigua"), ("jazz_cool_vibes", "Cool Vibes"), ("jazz_hot_swing", "Hot Swing"), ("jazz_lobby_time", "Lobby Time"),
        };
        // counter-local: left end, back half (the ammo box stands at the front-left corner)
        static readonly Vector3 CounterLocal = new Vector3(-1.27f, 1.03f, -0.2f);
        const float Scale = 0.55f; // the 72 cm boombox at 40 cm: a portable radio

        public static JazzRadio Build(Transform sceneRoot)
        {
            var counter = GameObject.Find("WeaponCounter");
            if (counter == null) { SetupLog.Warn("WeaponCounter not found; radio skipped"); return null; }
            var old = counter.transform.Find(Name); if (old != null) Object.DestroyImmediate(old.gameObject);
            var radio = PropLibrary.Place(counter, "boombox", CounterLocal, new Vector3(0f, 180f, 0f), Scale, CounterLocal.y, SurfaceType.Metal, true, false, 0f, Name);
            if (radio == null) { SetupLog.Warn("Boombox model missing (python Tools/AssetGen/fetch_polyhaven.py boombox); radio skipped"); return null; }
            foreach (var t in radio.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0; // Default: reachable by the hand rays
            // one box collider around the whole radio for the grip
            foreach (var c in radio.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var b = Bounds(radio);
            // (the FBX root carries an axis / unit compensation, so the box lives on an unrotated, unscaled child)
            var grip = new GameObject("GripCollider"); grip.transform.SetParent(radio.transform, false);
            grip.transform.position = b.center; grip.transform.rotation = counter.transform.rotation;
            var ls = grip.transform.lossyScale; var box = grip.AddComponent<BoxCollider>();
            box.size = new Vector3(b.size.x / Mathf.Abs(ls.x), (b.size.y + 0.02f) / Mathf.Abs(ls.y), (b.size.z + 0.02f) / Mathf.Abs(ls.z));
            var src = EditorRefs.Ensure<AudioSource>(radio);
            src.playOnAwake = false; src.spatialBlend = 1f; src.minDistance = 1.2f; src.maxDistance = 14f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0f; src.priority = 200;
            EditorRefs.Ensure<XRSimpleInteractable>(radio);
            var clips = s_Tracks.Select(t => AssetDatabase.LoadAssetAtPath<AudioClip>($"{RangePaths.Audio}/Music/{t.file}.mp3")).ToArray();
            if (clips.Any(c => c == null)) SetupLog.Warn("Some radio tracks are missing in Assets/Audio/Music");
            // display: a small text plate above the radio, readable from the player's side of the counter
            var displayGo = new GameObject("RadioDisplay"); displayGo.transform.SetParent(counter.transform, false);
            displayGo.transform.localPosition = CounterLocal + new Vector3(0f, b.size.y + 0.035f, 0.02f); displayGo.transform.localRotation = Quaternion.Euler(15f, 180f, 0f);
            displayGo.transform.SetParent(radio.transform, true);
            var tmp = displayGo.AddComponent<TextMeshPro>();
            tmp.fontSize = 0.6f; tmp.alignment = TextAlignmentOptions.Center; tmp.textWrappingMode = TextWrappingModes.NoWrap; tmp.color = new Color(1f, 0.78f, 0.35f);
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.1f);
            var font = UiBuilder.GetFont(); if (font != null) tmp.font = font;
            tmp.text = "RADIO";
            var mr = displayGo.GetComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var jr = EditorRefs.Ensure<JazzRadio>(radio);
            jr.Configure(src, clips.Where(c => c != null).ToArray(), s_Tracks.Where((t, i) => clips[i] != null).Select(t => t.title).ToArray(), tmp);
            EditorUtility.SetDirty(jr);
            // what else is on the counter near it
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(radio.transform) || r.bounds.size.x > 2.5f) continue;
                if (r.bounds.Intersects(b) && r.bounds.max.y > b.min.y + 0.005f) SetupLog.Warn($"Radio overlaps {r.name} ({r.bounds.center})");
            }
            SetupLog.Ok($"Jazz radio on the counter at {radio.transform.position} ({b.size.x * 100f:0}x{b.size.y * 100f:0}x{b.size.z * 100f:0} cm), {clips.Count(c => c != null)} tracks");
            return jr;
        }

        static Bounds Bounds(GameObject go)
        {
            Bounds? b = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
            return b ?? new Bounds(go.transform.position, Vector3.one * 0.1f);
        }
    }
}
