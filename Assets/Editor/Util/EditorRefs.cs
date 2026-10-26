using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Helpers for wiring private [SerializeField] references and common component setup from editor code.</summary>
    public static class EditorRefs
    {
        public static void Set(Object target, string field, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[VRRange] Field '{field}' not found on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value) { var so = new SerializedObject(target); var p = so.FindProperty(field); if (p != null) { p.floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); } }
        public static void SetInt(Object target, string field, int value) { var so = new SerializedObject(target); var p = so.FindProperty(field); if (p != null) { p.intValue = value; so.ApplyModifiedPropertiesWithoutUndo(); } }
        public static void SetBool(Object target, string field, bool value) { var so = new SerializedObject(target); var p = so.FindProperty(field); if (p != null) { p.boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); } }

        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(field);
            if (p == null) return;
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static GameObject Child(GameObject parent, string name, Vector3 localPos = default, Vector3 localEuler = default)
        {
            var t = parent.transform.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos; go.transform.localEulerAngles = localEuler; go.transform.localScale = Vector3.one;
            return go;
        }

        public static T Ensure<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();

        public static GameObject MeshObject(GameObject parent, string name, Mesh mesh, Material[] materials, Vector3 localPos, Vector3 localEuler = default, int layer = -1, bool collider = false, bool isStatic = false)
        {
            var go = Child(parent, name, localPos, localEuler);
            var mf = Ensure<MeshFilter>(go); mf.sharedMesh = mesh;
            var mr = Ensure<MeshRenderer>(go); mr.sharedMaterials = materials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collider) { var bc = Ensure<BoxCollider>(go); bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
            if (layer >= 0) go.layer = layer;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        public static int Layer(string name) { int l = LayerMask.NameToLayer(name); return l >= 0 ? l : 0; }

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }
    }
}
