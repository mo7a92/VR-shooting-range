using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRRange.UI;

namespace VRRange.Editor
{
    /// <summary>World-space VR menu, HUD, lane controls and results panels wired to <see cref="RangeUI"/>.</summary>
    public static class UiBuilder
    {
        const float PixelsPerMetre = 500f; // canvas scale 0.002: a 500 px wide panel is 1 m
        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        static readonly Color ButtonColor = new Color(0.16f, 0.36f, 0.62f, 1f);
        static readonly Color ButtonHover = new Color(0.22f, 0.48f, 0.78f, 1f);
        static readonly Color AccentColor = new Color(0.95f, 0.78f, 0.1f, 1f);
        static TMP_FontAsset s_Font;
        static Sprite s_PanelSprite, s_ButtonSprite, s_OutlineSprite, s_ShadowSprite;
        static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{RangePaths.ArtUI}/{name}.png");

        public static RangeUI Build(Transform sceneRoot)
        {
            SetupLog.Section("VR UI");
            s_Font = GetFont();
            s_PanelSprite = LoadSprite("ui_panel_rounded"); s_ButtonSprite = LoadSprite("ui_button"); s_OutlineSprite = LoadSprite("ui_outline"); s_ShadowSprite = LoadSprite("ui_shadow");
            EnsureEventSystem(sceneRoot);
            var root = new GameObject("RangeUI"); root.transform.SetParent(sceneRoot, false);
            var ui = root.AddComponent<RangeUI>();

            // Main menu: left of the spawn point, angled towards the player
            var menu = Panel(root, "MainMenuPanel", new Vector3(-1.45f, 1.45f, -0.8f), new Vector3(0f, -35f, 0f), 520, 720);
            Title(menu, "VR SHOOTING RANGE", 44);
            Label(menu, "SubTitle", "Choose a mode", 24, new Vector2(0, 280), 480, 40, Color.white);
            ui.freePracticeButton = Button(menu, "FreePractice", "Free Practice", new Vector2(0, 200), 440, 80);
            ui.accuracyButton = Button(menu, "Accuracy", "Accuracy Challenge", new Vector2(0, 105), 440, 80);
            ui.movingButton = Button(menu, "Moving", "Moving Target Challenge", new Vector2(0, 10), 440, 80);
            ui.difficultyLabel = Label(menu, "DifficultyLabel", "Difficulty: Medium", 24, new Vector2(0, -70), 480, 36, AccentColor);
            ui.bestLabel = Label(menu, "BestLabel", "", 20, new Vector2(0, -185), 480, 30, new Color(0.85f, 0.85f, 0.85f));
            ui.difficultyButtons = new[]
            {
                Button(menu, "Easy", "Easy", new Vector2(-150, -130), 130, 60), Button(menu, "Medium", "Medium", new Vector2(0, -130), 130, 60), Button(menu, "Hard", "Hard", new Vector2(150, -130), 130, 60)
            };
            Label(menu, "Hint", "Grip: grab   Trigger: fire   A/X: reload   B/Y: fire mode\nLeft stick: teleport   Right stick: turn", 18, new Vector2(0, -225), 480, 70, new Color(0.8f, 0.8f, 0.8f));
            ui.exitButton = Button(menu, "Exit", "Exit", new Vector2(0, -310), 200, 56, new Color(0.5f, 0.15f, 0.15f));
            ui.mainMenuPanel = menu;

            // Lane controls: right of the spawn point
            var lane = Panel(root, "LaneControlsPanel", new Vector3(1.45f, 1.45f, -0.8f), new Vector3(0f, 35f, 0f), 520, 620);
            Title(lane, "TARGET CONTROLS", 36);
            Label(lane, "DistanceTitle", "Distance", 24, new Vector2(0, 220), 480, 36, AccentColor);
            ui.distanceButtons = new[]
            {
                Button(lane, "D5", "5 m", new Vector2(-180, 160), 100, 60), Button(lane, "D10", "10 m", new Vector2(-60, 160), 100, 60),
                Button(lane, "D15", "15 m", new Vector2(60, 160), 100, 60), Button(lane, "D25", "25 m", new Vector2(180, 160), 100, 60)
            };
            Label(lane, "MotionTitle", "Target motion", 24, new Vector2(0, 90), 480, 36, AccentColor);
            ui.motionButtons = new[]
            {
                Button(lane, "MStatic", "Static", new Vector2(-120, 30), 200, 56), Button(lane, "MHoriz", "Horizontal", new Vector2(120, 30), 200, 56),
                Button(lane, "MNearFar", "Near / Far", new Vector2(-120, -40), 200, 56), Button(lane, "MRandom", "Random", new Vector2(120, -40), 200, 56),
                Button(lane, "MPopUp", "Pop-up", new Vector2(-120, -110), 200, 56)
            };
            ui.resetTargetsButton = Button(lane, "ResetTargets", "Reset Targets", new Vector2(120, -110), 200, 56, new Color(0.35f, 0.35f, 0.4f));
            ui.laneStatusLabel = Label(lane, "LaneStatus", "Distance 10 m   Motion: Static", 20, new Vector2(0, -190), 480, 40, Color.white);
            ui.laneControlsPanel = lane;

            // HUD above the centre lane shelf, facing the shooter
            var hud = Panel(root, "HudPanel", new Vector3(0f, 2.05f, 0.85f), new Vector3(15f, 0f, 0f), 760, 230); // canvases face -Z: visible to the shooter looking downrange
            ui.modeLabel = Label(hud, "Mode", "Free Practice", 30, new Vector2(0, 80), 720, 40, AccentColor);
            ui.scoreLabel = Label(hud, "Score", "Score 0", 34, new Vector2(-230, 25), 240, 44, Color.white);
            ui.shotsLabel = Label(hud, "Shots", "Shots 0   Hits 0", 26, new Vector2(60, 25), 300, 44, Color.white);
            ui.accuracyLabel = Label(hud, "Accuracy", "Accuracy 0%", 26, new Vector2(270, 25), 200, 44, Color.white);
            ui.timerLabel = Label(hud, "Timer", "Time 0.0s", 26, new Vector2(-230, -25), 240, 40, Color.white);
            ui.ammoLabel = Label(hud, "Ammo", "Pick a weapon from the counter", 22, new Vector2(110, -25), 440, 40, new Color(0.85f, 0.9f, 1f));
            ui.lastHitLabel = Label(hud, "LastHit", "", 30, new Vector2(-150, -75), 420, 44, AccentColor);
            ui.comboLabel = Label(hud, "Combo", "", 30, new Vector2(230, -75), 260, 44, new Color(1f, 0.55f, 0.2f));
            ui.hudPanel = hud;

            // Results (same spot as the main menu, shown when a challenge ends)
            var results = Panel(root, "ResultsPanel", new Vector3(-1.45f, 1.45f, -0.8f), new Vector3(0f, -35f, 0f), 520, 620);
            ui.resultsTitle = Title(results, "RESULTS", 40);
            ui.resultsBody = Label(results, "Body", "", 26, new Vector2(0, 60), 480, 320, Color.white, TextAlignmentOptions.TopLeft);
            ui.restartButton = Button(results, "Restart", "Restart", new Vector2(-115, -220), 210, 70);
            ui.backToMenuButton = Button(results, "Menu", "Main Menu", new Vector2(115, -220), 210, 70, new Color(0.35f, 0.35f, 0.4f));
            ui.resultsPanel = results;
            results.SetActive(false);

            // Hint text on the HUD (first-launch help)
            ui.hintLabel = Label(hud, "Hint", "", 16, new Vector2(0, -105), 740, 24, new Color(0.7f, 0.7f, 0.7f));

            SetupLog.Ok("World-space UI built (menu, lane controls, HUD, results)");
            return ui;
        }

        /// <summary>Digital scoreboard text on each booth's screen housing (built by EnvironmentBuilder).</summary>
        public static void BuildLaneScoreboards(Transform sceneRoot, System.Collections.Generic.List<VRRange.Targets.TargetLane> lanes)
        {
            if (s_Font == null) s_Font = GetFont();
            var root = new GameObject("LaneScoreboards"); root.transform.SetParent(sceneRoot, false);
            int n = 0;
            foreach (var lane in lanes)
            {
                if (lane == null) continue;
                float x = EnvironmentBuilder.LaneX(lane.LaneIndex);
                var go = new GameObject($"Scoreboard{lane.LaneIndex + 1}", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(x, 2.62f, 0.95f - 0.052f); // on the screen face (-Z side of the housing)
                go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one / PixelsPerMetre;
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 170);
                var text = Label(go, "Text", "LANE", 30, Vector2.zero, 410, 160, new Color(0.3f, 1f, 0.5f));
                text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
                var sb = go.AddComponent<VRRange.UI.LaneScoreboard>();
                sb.Initialize(lane, text);
                n++;
            }
            SetupLog.Ok($"{n} lane scoreboards built");
        }

        static void EnsureEventSystem(Transform sceneRoot)
        {
            var existing = Object.FindFirstObjectByType<EventSystem>();
            var go = existing != null ? existing.gameObject : new GameObject("EventSystem");
            go.transform.SetParent(sceneRoot, false);
            EditorRefs.Ensure<EventSystem>(go);
            var legacy = go.GetComponent<StandaloneInputModule>(); if (legacy != null) Object.DestroyImmediate(legacy);
            var module = EditorRefs.Ensure<XRUIInputModule>(go);
            module.enableXRInput = true; module.enableMouseInput = false; module.enableTouchInput = false;
        }

        static GameObject Panel(GameObject parent, string name, Vector3 pos, Vector3 euler, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = Vector3.one / PixelsPerMetre;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = null;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(width, height);
            var bg = go.AddComponent<Image>(); bg.color = PanelColor; bg.sprite = s_PanelSprite; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.5f;
            if (s_ShadowSprite != null)
            {
                var shadow = new GameObject("Shadow", typeof(RectTransform)); shadow.transform.SetParent(go.transform, false); shadow.transform.SetAsFirstSibling();
                var srt = shadow.GetComponent<RectTransform>(); srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = new Vector2(-24, -30); srt.offsetMax = new Vector2(24, 18);
                var simg = shadow.AddComponent<Image>(); simg.sprite = s_ShadowSprite; simg.type = Image.Type.Sliced; simg.color = new Color(0, 0, 0, 0.6f); simg.raycastTarget = false;
            }
            var border = new GameObject("Border", typeof(RectTransform)); border.transform.SetParent(go.transform, false);
            var brt = border.GetComponent<RectTransform>(); brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = new Vector2(4, 4); brt.offsetMax = new Vector2(-4, -4);
            var bimg = border.AddComponent<Image>(); bimg.color = AccentColor; bimg.sprite = s_OutlineSprite; bimg.type = Image.Type.Sliced; bimg.pixelsPerUnitMultiplier = 0.5f; bimg.raycastTarget = false;
            return go;
        }

        static TMP_Text Title(GameObject panel, string text, float size)
        {
            var rt = panel.GetComponent<RectTransform>();
            var t = Label(panel, "Title", text, size, new Vector2(0, rt.sizeDelta.y * 0.5f - 50), rt.sizeDelta.x - 40, 70, AccentColor);
            var bar = new GameObject("TitleBar", typeof(RectTransform)); bar.transform.SetParent(panel.transform, false); bar.transform.SetAsFirstSibling(); bar.transform.SetSiblingIndex(1);
            var brt = bar.GetComponent<RectTransform>(); brt.anchoredPosition = new Vector2(0, rt.sizeDelta.y * 0.5f - 50); brt.sizeDelta = new Vector2(rt.sizeDelta.x - 12, 80);
            var img = bar.AddComponent<Image>(); img.color = new Color(0.1f, 0.2f, 0.35f, 1f); img.raycastTarget = false; img.sprite = s_PanelSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.5f;
            return t;
        }

        static TMP_Text Label(GameObject panel, string name, string text, float size, Vector2 pos, float width, float height, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(panel.transform, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, height);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align; tmp.raycastTarget = false;
            if (s_Font != null) tmp.font = s_Font;
            return tmp;
        }

        static Button Button(GameObject panel, string name, string text, Vector2 pos, float width, float height, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(panel.transform, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, height);
            var img = go.AddComponent<Image>(); img.color = color ?? ButtonColor; img.sprite = s_ButtonSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.6f;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f); colors.pressedColor = new Color(0.7f, 0.7f, 0.7f); colors.selectedColor = Color.white; btn.colors = colors;
            Label(go, "Label", text, Mathf.Min(28f, height * 0.42f), Vector2.zero, width - 10, height, Color.white);
            return btn;
        }

        /// <summary>Default TMP font if TMP Essentials are imported; otherwise a dynamic font asset generated from Unity's built-in font.</summary>
        public static TMP_FontAsset GetFont()
        {
            var settings = TMP_Settings.instance;
            if (settings != null && TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
            const string path = RangePaths.Settings + "/RangeUIFont SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) { SetupLog.Warn("No font available for UI text; import TMP Essentials (Window > TextMeshPro > Import TMP Essential Resources)."); return null; }
            var asset = TMP_FontAsset.CreateFontAsset(font);
            if (asset == null) return null;
            RangePaths.EnsureFolder(RangePaths.Settings);
            AssetDatabase.CreateAsset(asset, path);
            if (asset.material != null) { asset.material.name = "RangeUIFont Material"; AssetDatabase.AddObjectToAsset(asset.material, asset); }
            if (asset.atlasTexture != null) { asset.atlasTexture.name = "RangeUIFont Atlas"; AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset); }
            AssetDatabase.SaveAssets();
            SetupLog.Warn("TMP Essentials not imported; created a fallback dynamic font asset for the UI.");
            return asset;
        }
    }
}
