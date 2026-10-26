using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Collects setup results and writes Logs/VRRangeSetupReport.md so the owner (or a local Claude session) sees exactly what happened.</summary>
    public static class SetupLog
    {
        static readonly List<string> s_Lines = new List<string>();
        static readonly List<string> s_Problems = new List<string>();

        public static void Begin(string title)
        {
            s_Lines.Clear(); s_Problems.Clear();
            s_Lines.Add($"# {title}");
            s_Lines.Add($"Unity {Application.unityVersion} — {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            s_Lines.Add("");
        }

        public static void Info(string msg) { s_Lines.Add($"- {msg}"); Debug.Log($"[VRRange] {msg}"); }
        public static void Ok(string msg) { s_Lines.Add($"- ✅ {msg}"); Debug.Log($"[VRRange] OK: {msg}"); }
        public static void Warn(string msg) { s_Lines.Add($"- ⚠️ {msg}"); s_Problems.Add(msg); Debug.LogWarning($"[VRRange] {msg}"); }
        public static void Error(string msg) { s_Lines.Add($"- ❌ {msg}"); s_Problems.Add(msg); Debug.LogError($"[VRRange] {msg}"); }
        public static void Section(string name) { s_Lines.Add(""); s_Lines.Add($"## {name}"); }
        public static int ProblemCount => s_Problems.Count;

        public static string Write()
        {
            var sb = new StringBuilder();
            foreach (var l in s_Lines) sb.AppendLine(l);
            sb.AppendLine();
            sb.AppendLine(s_Problems.Count == 0 ? "## Result: no problems reported" : $"## Result: {s_Problems.Count} item(s) need attention");
            foreach (var p in s_Problems) sb.AppendLine($"- {p}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RangePaths.ReportFile));
                File.WriteAllText(RangePaths.ReportFile, sb.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning($"[VRRange] Could not write report: {e.Message}"); }
            return sb.ToString();
        }
    }
}
