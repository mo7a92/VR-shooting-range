// Compile-check-only stand-ins for the two editor files that need URP / OpenXR editor assemblies, which are not
// part of the cloud reference build (see compile_check.py). Never imported by Unity (lives outside Assets/).
namespace VRRange.Editor
{
    static class UrpSettingsSetup { public static object Apply() => null; }
    static class OpenXRSettingsSetup { public static void Apply() { } public static void ValidateFeatures() { } }
}
