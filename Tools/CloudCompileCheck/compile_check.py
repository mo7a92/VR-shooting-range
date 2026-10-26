#!/usr/bin/env python3
"""
Cloud compile check for the VR Shooting Range Unity project.

Compiles the Unity packages this project depends on (from their public GitHub
source mirrors) and then the project's own assemblies with a standalone Roslyn
compiler, so C# errors are caught without a Unity Editor. It is a *static*
validation only: it cannot run Unity, import assets, or build an APK.

Environment variables (all optional, defaults match the cloud session layout):
  VRR_CSC        path to csc executable (self-contained Roslyn)
  VRR_BCL        directory containing .NET BCL reference dlls
  VRR_UNITYREFS  directory containing UnityEngine*.dll reference assemblies
  VRR_PKGSRC     directory containing cloned package sources (<name>@<version>)
  VRR_OUT        output directory for compiled dlls and logs
Usage: python3 compile_check.py [--only NAME] [--project-only] [--verbose]
"""
import os, sys, json, subprocess, glob, re, time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
S = os.environ.get("VRR_SCRATCH", "/tmp/claude-0/-home-user-VR-shooting-range/66ba9ce3-abcf-5db2-a840-2cdba49b1e04/scratchpad")
CSC = os.environ.get("VRR_CSC", os.path.join(S, "roslyn/csc/csc"))
BCL = os.environ.get("VRR_BCL", os.path.join(S, "roslyn/csc"))
UNITYREFS = os.environ.get("VRR_UNITYREFS", os.path.join(S, "unityrefs/engine6"))  # built by build_unity_refs.py from UnityCsReference 6000.3
PKGSRC = os.environ.get("VRR_PKGSRC", os.path.join(S, "pkgsrc"))
OUT = os.environ.get("VRR_OUT", os.path.join(S, "build"))
EDITOR_STUBS = os.environ.get("VRR_EDITOR_STUBS", os.path.join(S, "unityrefs/editor/UnityEditor.dll"))

os.makedirs(OUT, exist_ok=True)

UNITY_VERSION_DEFINES = [
    "UNITY_6000_3", "UNITY_6000_3_OR_NEWER", "UNITY_6000_2_OR_NEWER", "UNITY_6000_1_OR_NEWER", "UNITY_6000_0_OR_NEWER", "UNITY_6000",
    "UNITY_2023_3_OR_NEWER", "UNITY_2023_2_OR_NEWER", "UNITY_2023_1_OR_NEWER",
    "UNITY_2022_3_OR_NEWER", "UNITY_2022_2_OR_NEWER", "UNITY_2022_1_OR_NEWER",
    "UNITY_2021_3_OR_NEWER", "UNITY_2021_2_OR_NEWER", "UNITY_2021_1_OR_NEWER",
    "UNITY_2020_3_OR_NEWER", "UNITY_2020_2_OR_NEWER", "UNITY_2020_1_OR_NEWER",
    "UNITY_2019_4_OR_NEWER", "UNITY_2019_3_OR_NEWER", "UNITY_2019_2_OR_NEWER", "UNITY_2019_1_OR_NEWER",
    "UNITY_2018_4_OR_NEWER", "UNITY_2018_3_OR_NEWER", "UNITY_2018_2_OR_NEWER", "UNITY_2018_1_OR_NEWER",
    "UNITY_2017_4_OR_NEWER", "UNITY_2017_3_OR_NEWER", "UNITY_2017_2_OR_NEWER", "UNITY_2017_1_OR_NEWER",
    "UNITY_5_6_OR_NEWER", "UNITY_5_5_OR_NEWER", "UNITY_5_4_OR_NEWER", "UNITY_5_3_OR_NEWER",
]
PLAYER_DEFINES = UNITY_VERSION_DEFINES + [
    "UNITY_ANDROID", "PLATFORM_ANDROID", "UNITY_64", "ENABLE_IL2CPP",
    "ENABLE_INPUT_SYSTEM", "ENABLE_VR", "ENABLE_XR", "ENABLE_AUDIO", "ENABLE_PHYSICS", "ENABLE_CLOTH", "ENABLE_TERRAIN",
    "ENABLE_UNITYWEBREQUEST", "ENABLE_MANAGED_JOBS", "ENABLE_MANAGED_TRANSFORM_JOBS", "ENABLE_MANAGED_ANIMATION_JOBS",
    "ENABLE_SPATIALTRACKING", "ENABLE_DIRECTOR", "ENABLE_UNITYEVENTS", "ENABLE_MICROPHONE", "ENABLE_NETWORK",
    "ENABLE_VIDEO", "ENABLE_RUNTIME_GI", "ENABLE_TEXTURE_STREAMING", "ENABLE_LZMA", "ENABLE_CRUNCH_TEXTURE_COMPRESSION",
    "ENABLE_ACCELEROMETER", "INCLUDE_DYNAMIC_GI", "ENABLE_SCRIPTING_GC_WBARRIERS", "ENABLE_PROFILER",
    "NET_4_6", "NET_STANDARD_2_1", "NET_STANDARD", "NET_UNITY_4_8", "CSHARP_7_3_OR_NEWER", "UNITY_ASSERTIONS",
]
EDITOR_DEFINES = PLAYER_DEFINES + ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_WIN", "ENABLE_MONO", "UNITY_INCLUDE_TESTS"]
EDITOR_DEFINES = [d for d in EDITOR_DEFINES if d != "ENABLE_IL2CPP"]

# Packages in Packages/manifest.json (name -> version). Used for asmdef versionDefines.
PRESENT_PACKAGES = {
    "com.unity.inputsystem": "1.14.2", "com.unity.mathematics": "1.3.2",
    "com.unity.render-pipelines.universal": "17.3.0", "com.unity.render-pipelines.core": "17.3.0", "com.unity.shadergraph": "17.3.0",
    "com.unity.test-framework": "1.4.6", "com.unity.ugui": "2.0.0", "com.unity.xr.core-utils": "2.5.3",
    "com.unity.xr.interaction.toolkit": "3.3.2", "com.unity.xr.management": "4.5.4", "com.unity.xr.openxr": "1.16.1",
    "com.unity.xr.legacyinputhelpers": "2.1.11",
    "com.unity.modules.animation": "1.0.0", "com.unity.modules.physics2d": "1.0.0", "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.physics": "1.0.0", "com.unity.modules.audio": "1.0.0", "com.unity.modules.xr": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.ui": "1.0.0",
}

def vtuple(v):
    v = v.split("-")[0]
    return tuple(int(x) if x.isdigit() else 0 for x in v.split("."))

def version_defines_for(asmdef_path):
    try:
        j = json.load(open(asmdef_path))
    except Exception:
        return []
    out = []
    for vd in j.get("versionDefines", []):
        name, expr, define = vd.get("name"), vd.get("expression", ""), vd.get("define")
        if name in PRESENT_PACKAGES and define:
            if not expr:
                out.append(define); continue
            m = re.match(r"^\[?(\d+\.\d+\.\d+)", expr)
            if not m or vtuple(PRESENT_PACKAGES[name]) >= vtuple(m.group(1)):
                out.append(define)
    return out

def collect_sources(dirs, exclude_dirs=(), exclude_patterns=()):
    files = []
    for d in dirs:
        for dp, dn, fn in os.walk(d):
            rel = os.path.relpath(dp, d)
            parts = [x for x in rel.split(os.sep) if x and x != "."]
            if any(p.endswith("~") or p.startswith(".") for p in parts):
                dn[:] = []; continue
            if any(os.path.abspath(dp).startswith(os.path.abspath(e)) for e in exclude_dirs):
                dn[:] = []; continue
            if "Tests" in parts or "Test" in parts:
                continue
            for f in fn:
                if f.endswith(".cs") and not any(re.search(p, os.path.join(dp, f)) for p in exclude_patterns):
                    files.append(os.path.join(dp, f))
    return sorted(files)

def bcl_refs():
    skip = ("Microsoft.CodeAnalysis", "csc.dll", "vbc.dll", "VBCSCompiler", "Microsoft.DiaSymReader", "System.Collections.Immutable", "System.Reflection.Metadata")
    return sorted(p for p in glob.glob(os.path.join(BCL, "*.dll")) if not any(s in os.path.basename(p) for s in skip))

def unity_refs():
    return sorted(glob.glob(os.path.join(UNITYREFS, "*.dll")))

def compile_asm(name, sources, refs, defines, out_dir=OUT, unsafe=True, langversion="9.0", nowarn=True, verbose=False, extra_args=()):
    if not sources:
        print(f"[{name}] no sources"); return False, []
    out = os.path.join(out_dir, name + ".dll")
    rsp = os.path.join(out_dir, name + ".rsp")
    with open(rsp, "w") as f:
        f.write("-nologo\n-noconfig\n-nostdlib\n-target:library\n")
        f.write(f"-out:{out}\n")
        f.write(f"-langversion:{langversion}\n")
        if unsafe: f.write("-unsafe\n")
        f.write("-warn:0\n" if nowarn else "-warn:4\n")
        f.write("-nowarn:1701,1702,CS0618\n")
        f.write("-deterministic\n-debug-\n-optimize-\n")
        f.write("-define:" + ";".join(defines) + "\n")
        for r in bcl_refs() + unity_refs() + list(refs):
            f.write(f'-reference:"{r}"\n')
        for a in extra_args: f.write(a + "\n")
        for s in sources:
            f.write(f'"{s}"\n')
    t0 = time.time()
    p = subprocess.run([CSC, "@" + rsp], capture_output=True, text=True)
    log = p.stdout + p.stderr
    with open(os.path.join(out_dir, name + ".log"), "w") as f: f.write(log)
    errors = [l for l in log.splitlines() if re.search(r"error CS\d+", l)]
    uniq = sorted(set(errors))
    ok = p.returncode == 0 and os.path.exists(out)
    print(f"[{name}] {'OK' if ok else 'FAILED'} files={len(sources)} errors={len(uniq)} ({time.time()-t0:.1f}s)")
    if not ok:
        for e in uniq[: (200 if verbose else 25)]:
            print("   ", e.replace(ROOT + "/", "").replace(PKGSRC + "/", "pkg:"))
    return ok, uniq

def P(*a): return os.path.join(PKGSRC, *a)
def O(name): return os.path.join(OUT, name + ".dll")

UNITS = []
def unit(name, dirs, refs=(), defines=None, exclude_dirs=(), asmdef=None, editor=True, exclude_patterns=(), stub=False):
    """editor=True compiles like the Unity Editor does (UNITY_EDITOR defined, UnityEditor referenced), which is how
    every assembly is first compiled when the project is opened. stub=True compiles a declaration-only version
    (via StubGen) when a package's method bodies need engine internals that the reference build lacks."""
    UNITS.append(dict(name=name, dirs=dirs, refs=refs, defines=defines, exclude_dirs=exclude_dirs, asmdef=asmdef, editor=editor, exclude_patterns=exclude_patterns, stub=stub))

STUBGEN = os.path.join(S, "roslyn/csc/stubgen")

def stub_sources(name, sources, defines):
    """Generate declaration-only copies of sources (bodies -> throw null) and prune members that do not compile."""
    import shutil
    d = os.path.join(OUT, "stubs", name)
    if os.path.isdir(d): shutil.rmtree(d)
    os.makedirs(d)
    lst = os.path.join(OUT, name + ".sources.txt"); open(lst, "w").write("\n".join(sources))
    common = os.path.commonpath(sources)
    p = subprocess.run([STUBGEN, "gen", "--out", d, "--root", common, "--defines", ";".join(defines), "@" + lst], capture_output=True, text=True)
    if p.returncode != 0: print("   stubgen failed:", p.stderr[:500])
    return d

LIH = P("com.unity.xr.legacyinputhelpers@2.1.11")
IS = P("com.unity.inputsystem@1.14.2")
CU = P("com.unity.xr.core-utils@2.5.3")
MATH = P("com.unity.mathematics@1.3.2")
UGUI = P("com.unity.ugui@1.0.0")
TMP = P("com.unity.textmeshpro@3.0.9")
XRM = P("com.unity.xr.management@4.5.4")
OXR = P("com.unity.xr.openxr@1.16.1")
XRI = P("com.unity.xr.interaction.toolkit@3.3.2")
TF = P("com.unity.test-framework@1.4.6")

unit("UnityEngine.SpatialTracking", [os.path.join(LIH, "Runtime/TrackedPoseDriver")])
unit("UnityEngine.XR.LegacyInputHelpers", [os.path.join(LIH, "Runtime")], refs=[O("UnityEngine.SpatialTracking")], exclude_dirs=[os.path.join(LIH, "Runtime/TrackedPoseDriver")], asmdef=os.path.join(LIH, "Runtime/LegacyInputHelpers.asmdef"))
unit("Unity.Mathematics", [os.path.join(MATH, "Unity.Mathematics")], asmdef=os.path.join(MATH, "Unity.Mathematics/Unity.Mathematics.asmdef"))
unit("UnityEngine.UI", [os.path.join(UGUI, "Runtime")], asmdef=os.path.join(UGUI, "Runtime/UnityEngine.UI.asmdef"),
     exclude_patterns=[r"UIElements/PanelEventHandler\.cs$", r"UIElements/PanelRaycaster\.cs$"])  # 2021-era UI Toolkit bridge, not used by this project
unit("UnityEditor.UI", [os.path.join(UGUI, "Editor")], refs=[O("UnityEngine.UI")], asmdef=os.path.join(UGUI, "Editor/UnityEditor.UI.asmdef"))
unit("UnityEditor.UI", [os.path.join(UGUI, "Editor")], refs=[O("UnityEngine.UI")], asmdef=os.path.join(UGUI, "Editor/UnityEditor.UI.asmdef"))
unit("Unity.TextMeshPro", [os.path.join(TMP, "Scripts/Runtime")], refs=[O("UnityEngine.UI")], asmdef=os.path.join(TMP, "Scripts/Runtime/Unity.TextMeshPro.asmdef"),
     stub=True)  # TMP 3.0.9 font-generation bodies target older FontEngine signatures; Unity 6 ships TMP inside ugui 2.0.0. API surface only.
unit("Unity.InputSystem", [os.path.join(IS, "InputSystem")], refs=[O("UnityEngine.UI"), O("UnityEditor.UI")], exclude_dirs=[os.path.join(IS, "InputSystem/Plugins/InputForUI")],
     asmdef=os.path.join(IS, "InputSystem/Unity.InputSystem.asmdef"),
     stub=True)  # editor tooling bodies use UnityEditor internals that the stub reference lacks; API surface only
unit("Unity.XR.CoreUtils", [os.path.join(CU, "Runtime")], refs=[O("Unity.InputSystem"), O("UnityEngine.SpatialTracking"), O("UnityEngine.UI")], asmdef=os.path.join(CU, "Runtime/Unity.XR.CoreUtils.asmdef"))
unit("Unity.XR.Management", [os.path.join(XRM, "Runtime")], refs=[O("UnityEngine.XR.LegacyInputHelpers"), O("Unity.XR.CoreUtils")], asmdef=os.path.join(XRM, "Runtime/Unity.XR.Management.asmdef"))
unit("Unity.XR.OpenXR", [os.path.join(OXR, "Runtime")], refs=[O("Unity.XR.Management"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("UnityEngine.XR.LegacyInputHelpers"), O("UnityEngine.SpatialTracking")],
     exclude_dirs=[os.path.join(OXR, "Runtime", d) for d in ("RuntimeDebugger", "TestTooling", "MockRuntime", "MetaQuest", "OculusQuest", "ConformanceAutomation")],
     exclude_patterns=[r"AutomaticDynamicResolutionFeature\.cs$"],  # needs URP assembly, not part of this reference build
     asmdef=os.path.join(OXR, "Runtime/Unity.XR.OpenXR.asmdef"), editor=False)  # player mode: editor-mode build needs URP + XR Management editor internals
unit("Unity.XR.OpenXR.Features.MetaQuestSupport", [os.path.join(OXR, "Runtime/MetaQuest")], refs=[O("Unity.XR.OpenXR"), O("Unity.XR.Management")], asmdef=os.path.join(OXR, "Runtime/MetaQuest/Unity.XR.OpenXR.Features.MetaQuestSupport.asmdef"), editor=False)
unit("Unity.XR.Interaction.Toolkit", [os.path.join(XRI, "Runtime")],
     refs=[O("UnityEngine.UI"), O("UnityEngine.SpatialTracking"), O("UnityEngine.XR.LegacyInputHelpers"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("Unity.XR.Management"), O("Unity.Mathematics"), O("Unity.XR.OpenXR"), O("Unity.TextMeshPro")],
     asmdef=os.path.join(XRI, "Runtime/Unity.XR.Interaction.Toolkit.asmdef"), editor=False)  # player mode (see OpenXR)
unit("Unity.XR.Interaction.Toolkit.Samples.StarterAssets", [os.path.join(ROOT, "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets")],
     refs=[O("Unity.XR.Interaction.Toolkit"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("UnityEngine.UI"), O("Unity.TextMeshPro"), O("Unity.XR.Management"), O("Unity.XR.OpenXR"), O("Unity.Mathematics")],
     exclude_dirs=[os.path.join(ROOT, "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Editor")],
     asmdef=os.path.join(ROOT, "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/StarterAssets.asmdef"), editor=False)
# --- editor assemblies of packages needed by VRRange.Editor ---
unit("Unity.XR.CoreUtils.Editor", [os.path.join(CU, "Editor")], refs=[O("Unity.XR.CoreUtils"), O("Unity.InputSystem"), EDITOR_STUBS], asmdef=os.path.join(CU, "Editor/Unity.XR.CoreUtils.Editor.asmdef"), editor=True)
unit("Unity.XR.Management.Editor", [os.path.join(XRM, "Editor")], refs=[O("Unity.XR.Management"), O("Unity.XR.CoreUtils"), O("Unity.XR.CoreUtils.Editor"), O("Unity.InputSystem"), EDITOR_STUBS], asmdef=os.path.join(XRM, "Editor/Unity.XR.Management.Editor.asmdef"), editor=True)
# --- project assemblies ---
unit("VRRange.Runtime", [os.path.join(ROOT, "Assets/Scripts")],
     refs=[O("Unity.XR.Interaction.Toolkit"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("UnityEngine.UI"), O("Unity.TextMeshPro"), O("Unity.XR.Management"), O("Unity.XR.OpenXR"), O("Unity.Mathematics")],
     asmdef=os.path.join(ROOT, "Assets/Scripts/VRRange.Runtime.asmdef"))
unit("VRRange.Runtime.Player", [os.path.join(ROOT, "Assets/Scripts")],
     refs=[O("Unity.XR.Interaction.Toolkit"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("UnityEngine.UI"), O("Unity.TextMeshPro"), O("Unity.XR.Management"), O("Unity.XR.OpenXR"), O("Unity.Mathematics")],
     asmdef=os.path.join(ROOT, "Assets/Scripts/VRRange.Runtime.asmdef"), editor=False)
unit("VRRange.Editor", [os.path.join(ROOT, "Assets/Editor")],
     refs=[O("VRRange.Runtime"), O("Unity.XR.Interaction.Toolkit"), O("Unity.InputSystem"), O("Unity.XR.CoreUtils"), O("Unity.XR.CoreUtils.Editor"), O("UnityEngine.UI"), O("Unity.TextMeshPro"), O("Unity.XR.Management"), O("Unity.XR.Management.Editor"), O("Unity.XR.OpenXR"), O("Unity.XR.OpenXR.Features.MetaQuestSupport"), O("Unity.Mathematics"), EDITOR_STUBS],
     # Not compile-checked in the cloud (verified against package source by inspection): URP asset setup and OpenXR
     # feature setup use com.unity.render-pipelines.universal / Unity.XR.OpenXR.Editor which the reference build lacks.
     exclude_patterns=[r"UrpSettingsSetup\.cs$", r"OpenXRSettingsSetup\.cs$"],
     asmdef=os.path.join(ROOT, "Assets/Editor/VRRange.Editor.asmdef"), editor=True)


def apply_reference_patches():
    """The newest public mirror of com.unity.ugui is 1.0.0 (Unity 2021.1); Unity 6 ships ugui 2.0.0 built into
    the editor, which added a few PointerEventData members that Input System 1.14 uses. Add them to the local
    reference copy so the dependency compiles. This touches only the scratch copy used for reference builds."""
    f = os.path.join(UGUI, "Runtime/EventSystem/EventData/PointerEventData.cs")
    if os.path.exists(f):
        src = open(f).read()
        if "displayIndex" not in src:
            src = src.replace("public bool dragging { get; set; }",
                "public bool dragging { get; set; }\n        public int displayIndex { get; set; }\n        public bool fullyExited { get; set; }\n        public bool reentered { get; set; }", 1)
            open(f, "w").write(src); print("[patch] added ugui 2.0 PointerEventData members to reference copy")
    f = os.path.join(UGUI, "Runtime/EventSystem/UIElements/PanelEventHandler.cs")
    if os.path.exists(f):
        src = open(f).read()
        if "penStatus" not in src:
            src = re.sub(r"(class PointerEvent : IPointerEvent\s*\{)", r"\1\n            public UnityEngine.PenStatus penStatus => default; public Vector2 tilt => default;", src, count=1)
            open(f, "w").write(src); print("[patch] added Unity 6 IPointerEvent members to reference copy")

def main():
    args = sys.argv[1:]
    apply_reference_patches()
    if "--no-clean" not in args:
        for f in glob.glob(os.path.join(OUT, '*.dll')): os.remove(f)
    only = None; verbose = "--verbose" in args; project_only = "--project-only" in args
    if "--only" in args: only = args[args.index("--only") + 1]
    results = {}
    for u in UNITS:
        if only and u["name"] != only: continue
        if project_only and not u["name"].startswith("VRRange"): continue
        if u["editor"] and not os.path.exists(EDITOR_STUBS):
            print(f"[{u['name']}] SKIPPED (no UnityEditor reference assembly at {EDITOR_STUBS})"); results[u["name"]] = None; continue
        defines = list(EDITOR_DEFINES if u["editor"] else PLAYER_DEFINES)
        if u["asmdef"]: defines += version_defines_for(u["asmdef"])
        if u["name"] == "UnityEngine.UI": defines = [d for d in defines if d != "PACKAGE_UITOOLKIT"]  # ugui 1.0 bridge excluded
        if u["name"].startswith("Unity.XR.Interaction.Toolkit"): defines = [d for d in defines if d != "UIELEMENTS_MODULE_PRESENT"]  # XRI UI Toolkit path needs the excluded ugui bridge
        if u["defines"]: defines += u["defines"]
        srcs = collect_sources(u["dirs"], u["exclude_dirs"], u["exclude_patterns"])
        refs = list(u["refs"])
        if u["editor"] and EDITOR_STUBS not in refs: refs.append(EDITOR_STUBS)
        if u["stub"]:
            sd = stub_sources(u["name"], srcs, defines)
            ok = False
            for rnd in range(30):
                srcs = collect_sources([sd], (), ())
                ok, errs = compile_asm(u["name"], srcs, refs, defines, verbose=verbose)
                if ok: break
                q = subprocess.run([STUBGEN, "prune", "--log", os.path.join(OUT, u["name"] + ".log"), "--root", sd], capture_output=True, text=True)
                print("   ", q.stdout.strip())
                if q.returncode == 3: break
        else:
            ok, errs = compile_asm(u["name"], srcs, refs, defines, verbose=verbose)
        results[u["name"]] = ok
        if not ok and not u["name"].startswith("VRRange") and not only:
            print("   (dependency failed; continuing — later units may cascade)")
    print("\nSUMMARY:")
    for k, v in results.items(): print(f"  {k:45s} {'OK' if v else ('SKIPPED' if v is None else 'FAILED')}")
    sys.exit(0 if all(v is not False for v in results.values()) else 1)

if __name__ == "__main__":
    main()
