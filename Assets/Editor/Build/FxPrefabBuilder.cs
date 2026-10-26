using UnityEditor;
using UnityEngine;
using VRRange.Ballistics;

namespace VRRange.Editor
{
    /// <summary>Projectile, decal, impact particle, muzzle flash, impact marker and audio-source prefabs.</summary>
    public static class FxPrefabBuilder
    {
        public static GameObject Projectile { get; private set; }
        public static GameObject Decal { get; private set; }
        public static GameObject ImpactParticles { get; private set; }
        public static GameObject MuzzleFlash { get; private set; }
        public static GameObject ImpactMarker { get; private set; }
        public static GameObject OneShotAudio { get; private set; }

        public static void Build()
        {
            SetupLog.Section("FX prefabs");
            RangePaths.EnsureFolder(RangePaths.Prefabs);
            Projectile = BuildProjectile();
            Decal = BuildDecal();
            ImpactParticles = BuildImpactParticles();
            MuzzleFlash = BuildMuzzleFlash();
            ImpactMarker = BuildImpactMarker();
            OneShotAudio = BuildOneShotAudio();
            SetupLog.Ok("FX prefabs built");
        }

        static GameObject Save(GameObject go, string name)
        {
            var path = $"{RangePaths.Prefabs}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildProjectile()
        {
            var go = new GameObject("Projectile"); go.layer = EditorRefs.Layer("Projectile");
            var mesh = EditorMeshUtil.Box("ProjectileTracer", new Vector3(0.008f, 0.008f, 0.07f), 1f);
            var mat = MaterialLibrary.Flat("Tracer", new Color(1f, 0.8f, 0.4f), 0.5f, 0f, new Color(2.5f, 1.6f, 0.6f));
            var vis = EditorRefs.MeshObject(go, "Visual", mesh, new[] { mat }, Vector3.zero);
            vis.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.09f; trail.startWidth = 0.012f; trail.endWidth = 0.0f; trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = MaterialLibrary.ParticleAdditive("TracerTrail", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/particle_soft.png"));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false; trail.autodestruct = false;
            var proj = go.AddComponent<Projectile>();
            EditorRefs.Set(proj, "m_Trail", trail); EditorRefs.Set(proj, "m_Renderer", vis.GetComponent<MeshRenderer>());
            return Save(go, "Projectile");
        }

        static GameObject BuildDecal()
        {
            var go = new GameObject("BulletHoleDecal"); go.layer = EditorRefs.Layer("Ignore Raycast");
            var mesh = EditorMeshUtil.Quad("DecalQuad", 1f, 1f);
            var mat = MaterialLibrary.UnlitTexture("BulletHole", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/bullet_hole.png"), transparent: true, lit: false);
            var q = EditorRefs.MeshObject(go, "Quad", mesh, new[] { mat }, Vector3.zero);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return Save(go, "BulletHoleDecal");
        }

        static GameObject BuildImpactParticles()
        {
            var go = new GameObject("ImpactParticles"); go.layer = EditorRefs.Layer("Ignore Raycast");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.duration = 0.3f; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f); main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.02f); main.gravityModifier = 1f;
            main.maxParticles = 32; main.simulationSpace = ParticleSystemSimulationSpace.World; main.stopAction = ParticleSystemStopAction.None;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.005f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.ParticleAdditive("ImpactSpark", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/particle_soft.png"));
            r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 3f; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return Save(go, "ImpactParticles");
        }

        static GameObject BuildMuzzleFlash()
        {
            var go = new GameObject("MuzzleFlash"); go.layer = EditorRefs.Layer("Ignore Raycast");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.duration = 0.1f; main.loop = false; main.playOnAwake = false; main.startLifetime = 0.05f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.stopAction = ParticleSystemStopAction.None;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.ParticleAdditive("MuzzleFlash", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/muzzle_flash.png"));
            r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // sparks child
            var sparks = EditorRefs.Child(go, "Sparks");
            var ps2 = sparks.AddComponent<ParticleSystem>();
            var m2 = ps2.main; m2.duration = 0.1f; m2.loop = false; m2.playOnAwake = false; m2.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            m2.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f); m2.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.01f); m2.maxParticles = 12; m2.stopAction = ParticleSystemStopAction.None;
            var e2 = ps2.emission; e2.rateOverTime = 0f; e2.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            var s2 = ps2.shape; s2.shapeType = ParticleSystemShapeType.Cone; s2.angle = 12f; s2.radius = 0.004f;
            var r2 = sparks.GetComponent<ParticleSystemRenderer>();
            r2.sharedMaterial = MaterialLibrary.ParticleAdditive("ImpactSpark", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/particle_soft.png"));
            r2.renderMode = ParticleSystemRenderMode.Stretch; r2.lengthScale = 4f; r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return Save(go, "MuzzleFlash");
        }

        static GameObject BuildImpactMarker()
        {
            var go = new GameObject("ImpactMarker"); go.layer = EditorRefs.Layer("Ignore Raycast");
            var mesh = EditorMeshUtil.Quad("MarkerQuad", 0.03f, 0.03f);
            var mat = MaterialLibrary.UnlitTexture("ImpactMarker", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtFx}/impact_marker.png"), transparent: true, lit: false);
            var q = EditorRefs.MeshObject(go, "Quad", mesh, new[] { mat }, Vector3.zero);
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return Save(go, "ImpactMarker");
        }

        static GameObject BuildOneShotAudio()
        {
            var go = new GameObject("OneShotAudio");
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 1.5f; src.maxDistance = 60f; src.dopplerLevel = 0f;
            return Save(go, "OneShotAudio");
        }
    }
}
