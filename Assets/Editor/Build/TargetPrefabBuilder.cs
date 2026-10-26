using UnityEditor;
using UnityEngine;
using VRRange.Core;
using VRRange.Targets;

namespace VRRange.Editor
{
    /// <summary>Bullseye target prefab: hanger + pop-up pivot + paper face with collider, scoring and markers.</summary>
    public static class TargetPrefabBuilder
    {
        public const float FaceSize = 0.6f;
        public const float FaceHeight = 1.5f;   // centre of the face above the floor
        public const float RailHeight = 3.0f;

        public static GameObject Target { get; private set; }

        public static void Build()
        {
            SetupLog.Section("Target prefab");
            var root = new GameObject("Target_Bullseye");
            int targetLayer = EditorRefs.Layer("Target");

            // Hanger from the ceiling rail down to the top of the frame (the carrier sits on the rail).
            var hanger = EditorRefs.MeshObject(root, "Hanger", EditorMeshUtil.Box("TargetHanger", new Vector3(0.03f, RailHeight - (FaceHeight + FaceSize * 0.5f + 0.05f), 0.03f), 2f),
                new[] { MaterialLibrary.Pbr("metal_brushed", "RailMetal", 2f) }, new Vector3(0f, (RailHeight + FaceHeight + FaceSize * 0.5f + 0.05f) * 0.5f, 0f), default, targetLayer);
            var trolley = EditorRefs.MeshObject(root, "Trolley", EditorMeshUtil.Box("TargetTrolley", new Vector3(0.14f, 0.08f, 0.2f), 2f),
                new[] { MaterialLibrary.Pbr("gunmetal", "TrolleyMetal", 4f) }, new Vector3(0f, RailHeight - 0.08f, 0f), default, targetLayer);

            // Pop-up pivot at the top of the face so the target folds back and down when hidden.
            var pivot = EditorRefs.Child(root, "PopUpPivot", new Vector3(0f, FaceHeight + FaceSize * 0.5f + 0.05f, 0f));
            var frame = EditorRefs.MeshObject(pivot, "Frame", EditorMeshUtil.Box("TargetFrame", new Vector3(FaceSize + 0.06f, FaceSize + 0.1f, 0.025f), 2f),
                new[] { MaterialLibrary.Flat("TargetBacking", new Color(0.55f, 0.45f, 0.33f), 0.2f) }, new Vector3(0f, -(FaceSize * 0.5f + 0.05f), 0.02f), default, targetLayer);
            frame.AddComponent<SurfaceMaterialTag>().surface = SurfaceType.Wood;
            var frameCol = frame.AddComponent<BoxCollider>(); frameCol.size = new Vector3(FaceSize + 0.06f, FaceSize + 0.1f, 0.025f);

            var faceTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/target_face_albedo.png");
            var face = EditorRefs.MeshObject(pivot, "Face", EditorMeshUtil.Quad("TargetFace", FaceSize, FaceSize),
                new[] { MaterialLibrary.UnlitTexture("TargetFace", faceTex, false, true) }, new Vector3(0f, -(FaceSize * 0.5f + 0.05f), 0f), default, targetLayer);
            face.AddComponent<SurfaceMaterialTag>().surface = SurfaceType.Paper;
            var faceCol = face.AddComponent<BoxCollider>(); faceCol.size = new Vector3(FaceSize, FaceSize, 0.01f); faceCol.center = new Vector3(0f, 0f, 0.004f);

            var target = root.AddComponent<BullseyeTarget>();
            target.Initialize(SettingsAssetBuilder.Rules, face.transform, FxPrefabBuilder.ImpactMarker);

            var path = $"{RangePaths.Prefabs}/Target_Bullseye.prefab";
            Target = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            SetupLog.Ok("Target prefab built");
        }
    }
}
