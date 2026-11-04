using TMPro;
using UnityEditor;
using UnityEngine;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// "How to play" counter sign: a small dark board with the controls, saved as Assets/Generated/Resources/HowToPlaySign.prefab
    /// and placed by <see cref="HowToPlaySign"/> when the scene loads (no scene edit, lightmaps stay valid).
    /// </summary>
    public static class SignPrefabBuilder
    {
        public const string ResourcesFolder = RangePaths.Generated + "/Resources";
        public static string PrefabPath => $"{ResourcesFolder}/{HowToPlaySign.ResourceName}.prefab";

        public static GameObject Build()
        {
            SetupLog.Section("How-to-play sign");
            RangePaths.EnsureFolder(ResourcesFolder);
            var root = new GameObject(HowToPlaySign.ResourceName);
            const float w = 0.56f, h = 0.4f;
            var board = EditorMeshUtil.Box("HowToPlayBoard", new Vector3(w, h, 0.012f), 1f);
            var boardMat = MaterialLibrary.Flat("HowToPlayBoard", new Color(0.07f, 0.075f, 0.085f), 0.45f, 0.1f);
            var frameMat = MaterialLibrary.Flat("HowToPlayFrame", new Color(0.85f, 0.6f, 0.18f), 0.5f, 0.6f);
            EditorRefs.MeshObject(root, "Board", board, new[] { boardMat }, Vector3.zero, default, -1, true);
            var frame = EditorMeshUtil.Box("HowToPlayFrame", new Vector3(w + 0.02f, 0.012f, 0.016f), 1f);
            EditorRefs.MeshObject(root, "FrameTop", frame, new[] { frameMat }, new Vector3(0f, h * 0.5f + 0.004f, 0f));
            EditorRefs.MeshObject(root, "FrameBottom", frame, new[] { frameMat }, new Vector3(0f, -h * 0.5f - 0.004f, 0f));
            // stand: a short angled foot so the board rests on the counter top
            var foot = EditorMeshUtil.Box("HowToPlayFoot", new Vector3(0.18f, 0.012f, 0.12f), 1f);
            EditorRefs.MeshObject(root, "Foot", foot, new[] { frameMat }, new Vector3(0f, -h * 0.5f - 0.01f, 0.05f), new Vector3(-12f, 0f, 0f));
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var font = UiBuilder.GetFont();
            Text(root, "Title", "HOW TO PLAY", font, 0.038f, new Vector3(0f, h * 0.5f - 0.04f, -0.0075f), new Color(1f, 0.72f, 0.2f), FontStyles.Bold, w - 0.04f, 0.05f);
            string body =
                "<b>GRIP</b>  grab a weapon with either hand\n" +
                "<b>TRIGGER</b>  fire (shooting hand only)\n" +
                "<b>SUPPORT HAND</b>  grip the handguard, or cup the pistol hand: steadier, less recoil\n" +
                "<b>A / X</b>  reload      <b>B / Y</b>  semi / auto\n" +
                "<b>LEFT STICK</b>  teleport      <b>RIGHT STICK</b>  turn\n" +
                "<b>FLOOR HEIGHT</b>  rest a controller on the floor, click both sticks for 1 s";
            Text(root, "Body", body, font, 0.021f, new Vector3(0f, -0.035f, -0.0075f), new Color(0.92f, 0.92f, 0.9f), FontStyles.Normal, w - 0.05f, h - 0.1f, TextAlignmentOptions.TopLeft);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            SetupLog.Ok($"How-to-play sign prefab: {PrefabPath} (placed at runtime on the counter)");
            return prefab;
        }

        static TextMeshPro Text(GameObject parent, string name, string text, TMP_FontAsset font, float size, Vector3 localPos, Color color, FontStyles style, float width, float height, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = EditorRefs.Child(parent, name, localPos, new Vector3(0f, 180f, 0f)); // TMP faces +Z; the board's front is -Z
            var tmp = EditorRefs.Ensure<TextMeshPro>(go);
            tmp.text = text; tmp.fontSize = size * 10f; tmp.color = color; tmp.fontStyle = style; tmp.alignment = align; tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
            tmp.transform.localScale = Vector3.one * 0.1f;
            tmp.rectTransform.sizeDelta = new Vector2(width * 10f, height * 10f);
            if (font != null) tmp.font = font;
            tmp.lineSpacing = 8f;
            var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tmp;
        }
    }
}
