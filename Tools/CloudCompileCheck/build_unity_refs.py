#!/usr/bin/env python3
"""
Builds *reference-only* UnityEngine.dll / UnityEditor.dll assemblies from Unity's public
UnityCsReference repository (the managed half of the engine, tag = project editor version).
The resulting dlls contain the real Unity 6.3 managed API surface (signatures), but no working
native implementation: they are only used to compile-check scripts in the cloud. Never ship them.

Usage: python3 build_unity_refs.py [engine|editor|all] [--verbose]
Env:   VRR_SCRATCH (scratch root), VRR_CSREF (UnityCsReference checkout)
"""
import os, sys, re, glob, subprocess, time, json

S = os.environ.get("VRR_SCRATCH", "/tmp/claude-0/-home-user-VR-shooting-range/66ba9ce3-abcf-5db2-a840-2cdba49b1e04/scratchpad")
CSREF = os.environ.get("VRR_CSREF", os.path.join(S, "pkgsrc/UnityCsReference"))
CSC = os.path.join(S, "roslyn/csc/csc")
BCL = os.path.join(S, "roslyn/csc")
OUT = os.path.join(S, "unityrefs/engine6")
EDOUT = os.path.join(S, "unityrefs/editor")
os.makedirs(OUT, exist_ok=True); os.makedirs(EDOUT, exist_ok=True)

EDITOR_ONLY_MODULES = {
    "VectorGraphics",  # needs third-party LibTessDotNet, skipped entirely
    "AssetDatabase", "PackageManager", "PackageManagerUI", "QuickSearch", "UIBuilder", "SceneView", "GridAndSnap",
    "ShortcutManager", "Progress", "SafeMode", "Licensing", "QuickInstall", "ContentBuild", "UMPE", "EditorToolbar",
    "TreeEditor", "Hierarchy", "MeshLODGenerator", "SketchUpEditor", "UIAutomationEditor",
}
DEFINES = [
    "UNITY_6000_3", "UNITY_6000_3_OR_NEWER", "UNITY_6000_2_OR_NEWER", "UNITY_6000_1_OR_NEWER", "UNITY_6000_0_OR_NEWER", "UNITY_6000",
    "UNITY_2023_3_OR_NEWER", "UNITY_2023_2_OR_NEWER", "UNITY_2023_1_OR_NEWER", "UNITY_2022_3_OR_NEWER", "UNITY_2022_2_OR_NEWER", "UNITY_2022_1_OR_NEWER",
    "UNITY_2021_3_OR_NEWER", "UNITY_2021_2_OR_NEWER", "UNITY_2021_1_OR_NEWER", "UNITY_2020_3_OR_NEWER", "UNITY_2020_2_OR_NEWER", "UNITY_2020_1_OR_NEWER",
    "UNITY_2019_4_OR_NEWER", "UNITY_2019_3_OR_NEWER", "UNITY_2019_2_OR_NEWER", "UNITY_2019_1_OR_NEWER", "UNITY_2018_4_OR_NEWER", "UNITY_2018_3_OR_NEWER",
    "UNITY_2018_2_OR_NEWER", "UNITY_2018_1_OR_NEWER", "UNITY_2017_4_OR_NEWER", "UNITY_2017_3_OR_NEWER", "UNITY_2017_2_OR_NEWER", "UNITY_2017_1_OR_NEWER",
    "UNITY_5_6_OR_NEWER", "UNITY_5_5_OR_NEWER", "UNITY_5_4_OR_NEWER", "UNITY_5_3_OR_NEWER",
    "UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_WIN", "UNITY_64", "UNITY_STANDALONE_WIN", "UNITY_STANDALONE",
    "ENABLE_MONO", "NET_4_6", "NET_STANDARD_2_1", "NET_UNITY_4_8", "CSHARP_7_3_OR_NEWER",
    "ENABLE_PROFILER", "ENABLE_AUDIO", "ENABLE_PHYSICS", "ENABLE_CLOTH", "ENABLE_TERRAIN", "ENABLE_UNITYWEBREQUEST", "ENABLE_VR", "ENABLE_XR",
    "ENABLE_MANAGED_JOBS", "ENABLE_MANAGED_TRANSFORM_JOBS", "ENABLE_MANAGED_ANIMATION_JOBS", "ENABLE_SPATIALTRACKING", "ENABLE_DIRECTOR",
    "ENABLE_UNITYEVENTS", "ENABLE_MICROPHONE", "ENABLE_NETWORK", "ENABLE_VIDEO", "ENABLE_RUNTIME_GI", "ENABLE_TEXTURE_STREAMING", "ENABLE_LZMA",
    "ENABLE_CRUNCH_TEXTURE_COMPRESSION", "ENABLE_ACCELEROMETER", "INCLUDE_DYNAMIC_GI", "ENABLE_SCRIPTING_GC_WBARRIERS", "UNITY_ASSERTIONS",
    "ENABLE_INPUT_SYSTEM", "ENABLE_LEGACY_INPUT_MANAGER", "ENABLE_CLOUD_SERVICES", "ENABLE_CLOUD_SERVICES_ANALYTICS", "UNITY_ANALYTICS",
    "ENABLE_UNET", "ENABLE_WEBSOCKET_HOST", "PLATFORM_SUPPORTS_MONO", "ENABLE_IL2CPP", "ENABLE_VIRTUALTEXTURING", "INCLUDE_IL2CPP",
]

def walk_cs(root, exclude_dir_names=("Tests", "Test", "Editor"), extra_exclude_paths=()):
    out = []
    for dp, dn, fn in os.walk(root):
        rel = os.path.relpath(dp, root)
        parts = [p for p in rel.split(os.sep) if p and p != "."]
        if any(p in exclude_dir_names or p.startswith(".") or p.endswith("~") for p in parts):
            dn[:] = []; continue
        if any(os.path.abspath(dp).startswith(os.path.abspath(e)) for e in extra_exclude_paths):
            dn[:] = []; continue
        for f in fn:
            if f.endswith(".cs"): out.append(os.path.join(dp, f))
    return out

def bcl_refs():
    skip = ("Microsoft.CodeAnalysis", "csc.dll", "vbc.dll", "VBCSCompiler", "Microsoft.DiaSymReader", "System.Collections.Immutable", "System.Reflection.Metadata")
    return sorted(p for p in glob.glob(os.path.join(BCL, "*.dll")) if not any(s in os.path.basename(p) for s in skip))

LAST_ERROR_FILES = []

def run_csc(name, out_dir, sources, refs, defines, exclude_file=None, verbose=False):
    out = os.path.join(out_dir, name + ".dll"); rsp = os.path.join(out_dir, name + ".rsp")
    excluded = set()
    if exclude_file and os.path.exists(exclude_file):
        excluded = set(l.strip() for l in open(exclude_file) if l.strip() and not l.startswith("#"))
    sources = [s for s in sources if os.path.relpath(s, CSREF) not in excluded]
    with open(rsp, "w") as f:
        f.write("-nologo\n-noconfig\n-nostdlib\n-target:library\n-unsafe\n-langversion:latest\n-warn:0\n-deterministic\n-debug-\n-optimize-\n")
        f.write(f"-out:{out}\n-define:" + ";".join(defines) + "\n")
        for r in bcl_refs() + list(refs): f.write(f'-reference:"{r}"\n')
        for s in sources: f.write(f'"{s}"\n')
    t0 = time.time()
    p = subprocess.run([CSC, "@" + rsp], capture_output=True, text=True)
    log = p.stdout + p.stderr
    open(os.path.join(out_dir, name + ".log"), "w").write(log)
    errs = sorted(set(l for l in log.splitlines() if re.search(r"error CS\d+", l)))
    ok = p.returncode == 0 and os.path.exists(out)
    print(f"[{name}] {'OK' if ok else 'FAILED'} files={len(sources)} errors={len(errs)} ({time.time()-t0:.0f}s)")
    if not ok:
        # summarize by file
        byfile = {}
        for e in errs:
            m = re.match(r"(.*?)\((\d+),\d+\): error (CS\d+): (.*)", e)
            if m: byfile.setdefault(os.path.relpath(m.group(1), CSREF), []).append(f"{m.group(3)} L{m.group(2)}: {m.group(4)[:110]}")
        global LAST_ERROR_FILES
        LAST_ERROR_FILES = sorted(byfile.keys())
        for f, es in sorted(byfile.items(), key=lambda kv: -len(kv[1]))[: (400 if verbose else 40)]:
            print(f"   {f} ({len(es)})"); 
            for e in es[: (10 if verbose else 2)]: print("      ", e)
    return ok

def engine(verbose):
    srcs = walk_cs(os.path.join(CSREF, "Runtime"))
    for m in sorted(os.listdir(os.path.join(CSREF, "Modules"))):
        if m.endswith("Editor") or m in EDITOR_ONLY_MODULES: continue
        srcs += walk_cs(os.path.join(CSREF, "Modules", m))
    srcs += walk_cs(os.path.join(os.path.dirname(os.path.abspath(__file__)), "shims"))
    return run_csc("UnityEngine", OUT, sorted(srcs), [], DEFINES, os.path.join(os.path.dirname(__file__), "engine_exclude.txt"), verbose)

def editor(verbose):
    srcs = walk_cs(os.path.join(CSREF, "Editor/Mono"), exclude_dir_names=("Tests", "Test"))
    for m in sorted(os.listdir(os.path.join(CSREF, "Modules"))):
        if m.endswith("Editor") or m in EDITOR_ONLY_MODULES:
            srcs += walk_cs(os.path.join(CSREF, "Modules", m), exclude_dir_names=("Tests", "Test"))
        else:
            ed = os.path.join(CSREF, "Modules", m, "Editor")
            if os.path.isdir(ed): srcs += walk_cs(ed, exclude_dir_names=("Tests", "Test"))
    return run_csc("UnityEditor", EDOUT, sorted(srcs), [os.path.join(OUT, "UnityEngine.dll")], DEFINES, os.path.join(os.path.dirname(__file__), "editor_exclude.txt"), verbose)

STUBGEN = os.path.join(S, "roslyn/csc/stubgen")
STUBDIR = os.path.join(S, "unityrefs/editor_stubs")

def editor_sources():
    srcs = walk_cs(os.path.join(CSREF, "Editor/Mono"), exclude_dir_names=("Tests", "Test"))
    for m in sorted(os.listdir(os.path.join(CSREF, "Modules"))):
        if m.endswith("Editor") or m in EDITOR_ONLY_MODULES:
            if m == "VectorGraphics": continue
            srcs += walk_cs(os.path.join(CSREF, "Modules", m), exclude_dir_names=("Tests", "Test"))
        else:
            ed = os.path.join(CSREF, "Modules", m, "Editor")
            if os.path.isdir(ed): srcs += walk_cs(ed, exclude_dir_names=("Tests", "Test"))
    return sorted(srcs)

def editor_stubbed(verbose, max_rounds=60):
    """Generate declaration-only stubs of the editor sources with StubGen, then compile; prune members that
    reference unavailable (third-party / private) types until the stub assembly compiles."""
    import shutil
    if os.path.isdir(STUBDIR): shutil.rmtree(STUBDIR)
    os.makedirs(STUBDIR, exist_ok=True)
    lst = os.path.join(EDOUT, "editor_sources.txt")
    open(lst, "w").write("\n".join(editor_sources()))
    p = subprocess.run([STUBGEN, "gen", "--out", STUBDIR, "--root", CSREF, "--defines", ";".join(DEFINES), "@" + lst], capture_output=True, text=True)
    print(p.stdout.strip(), p.stderr.strip()[:2000])
    for r in range(max_rounds):
        srcs = walk_cs(STUBDIR, exclude_dir_names=())
        if run_csc("UnityEditor", EDOUT, srcs, [os.path.join(OUT, "UnityEngine.dll")], DEFINES, None, verbose):
            return True
        q = subprocess.run([STUBGEN, "prune", "--log", os.path.join(EDOUT, "UnityEditor.log"), "--root", STUBDIR], capture_output=True, text=True)
        print("   ", q.stdout.strip(), q.stderr.strip()[:500])
        if q.returncode == 3: print("   no prunable nodes; giving up"); return False
    return False

def auto_exclude(fn, exclude_file, max_iters=25):
    """Iteratively exclude source files that fail to compile (third-party deps such as Bee, NiceIO,
    Mono.Cecil, Newtonsoft.Json are not part of UnityCsReference). Reference build only."""
    for i in range(max_iters):
        if fn(False): return True
        if not LAST_ERROR_FILES: return False
        with open(exclude_file, "a") as f:
            f.write(f"# auto-exclude round {i+1}\n")
            for e in LAST_ERROR_FILES: f.write(e + "\n")
        print(f"   -> excluded {len(LAST_ERROR_FILES)} files, retrying")
    return False

if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "all"
    v = "--verbose" in sys.argv
    ok = True
    if what in ("engine", "all"): ok = engine(v) and ok
    if what == "editor-stub":
        ok = editor_stubbed(v) and ok
    if what in ("editor", "all"):
        if "--auto-exclude" in sys.argv:
            ok = auto_exclude(editor, os.path.join(os.path.dirname(os.path.abspath(__file__)), "editor_exclude.txt")) and ok
        else:
            ok = editor(v) and ok
    sys.exit(0 if ok else 1)
