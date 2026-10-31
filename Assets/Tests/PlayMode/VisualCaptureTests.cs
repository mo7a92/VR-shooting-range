using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Renders review screenshots of the generated scene (spawn view, counter, weapon in hand) to Logs/shots.
    /// Skips itself when no graphics device is available (batch -nographics runs).
    /// </summary>
    public class VisualCaptureTests
    {
        static int EnvInt(string name, int fallback) => int.TryParse(System.Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

        [UnityTest]
        public IEnumerator CaptureReviewShots()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (nographics run)");
            var load = SceneManager.LoadSceneAsync("ShootingRange", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            yield return null;
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(dir);

            var camGo = new GameObject("ReviewCamera"); var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 100f; cam.fieldOfView = 70f; cam.tag = "Untagged";
            // Resolution is overridable (VRRANGE_SHOT_WIDTH/HEIGHT) so software-GL containers can render small frames.
            int shotW = EnvInt("VRRANGE_SHOT_WIDTH", 1600), shotH = EnvInt("VRRANGE_SHOT_HEIGHT", 900);
            var rt = new RenderTexture(shotW, shotH, 24); cam.targetTexture = rt;
            var mainCam = Camera.main; if (mainCam != null) mainCam.enabled = false;

            IEnumerator Shot(string name, Vector3 pos, Vector3 lookAt)
            {
                cam.transform.position = pos; cam.transform.LookAt(lookAt);
                // No WaitForEndOfFrame here: it never resumes in -batchmode (no player loop render), which hung the first capture run.
                yield return null;
                cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }

            yield return Shot("01_spawn_downrange", new Vector3(0f, 1.6f, -1.4f), new Vector3(0f, 1.3f, 10f));
            yield return Shot("02_counter", new Vector3(0f, 1.55f, -1.9f), new Vector3(0f, 1.0f, -3.4f));
            yield return Shot("02b_staff_area", new Vector3(1.2f, 1.6f, -2.2f), new Vector3(-1.5f, 1.0f, -4.6f));
            yield return Shot("02c_officer_station", new Vector3(1.6f, 1.6f, -2.0f), new Vector3(3.3f, 0.9f, -3.8f));
            yield return Shot("03_booth", new Vector3(-0.6f, 1.6f, -1.6f), new Vector3(-2.4f, 1.1f, 0.9f));
            yield return Shot("03b_menu_panel", new Vector3(-0.3f, 1.5f, -1.4f), new Vector3(-1.35f, 1.38f, -0.75f));
            yield return Shot("04_waiting_area", new Vector3(1.5f, 1.6f, -3.2f), new Vector3(-1.0f, 1.0f, -7.0f));
            yield return Shot("04b_waiting_area_right", new Vector3(-1.5f, 1.6f, -3.6f), new Vector3(2.5f, 1.0f, -7.0f));
            yield return Shot("04c_lockers_and_rack", new Vector3(0.5f, 1.5f, -4.6f), new Vector3(3.0f, 1.0f, -7.2f));
            yield return Shot("04d_gun_rack", new Vector3(-0.8f, 1.5f, -2.6f), new Vector3(-2.5f, 1.0f, -4.4f));
            yield return Shot("05_lanes_from_above", new Vector3(-1.6f, 2.5f, 2.5f), new Vector3(0f, 1.0f, 14f));
            yield return Shot("05b_targets_close", new Vector3(0f, 1.6f, 7.5f), new Vector3(0f, 1.5f, 10f));
            yield return Shot("05c_wide_from_behind", new Vector3(0.55f, 1.95f, -2.75f), new Vector3(0f, 1.4f, 2f));
            yield return Shot("05d_downrange_from_wall", new Vector3(-4.0f, 1.7f, 12f), new Vector3(0f, 1.3f, 24f));
            // weapon in hand close-up: activate the right hand and grab the pistol with a scripted interactor placed at the controller
            var manager = Object.FindFirstObjectByType<XRInteractionManager>();
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var h in hands) for (var t = h.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            var right = hands.FirstOrDefault(h => !h.IsLeft); var left = hands.FirstOrDefault(h => h.IsLeft);
            yield return null;
            if (right != null && manager != null)
            {
                var controller = right.transform.parent; // "Right Controller"
                controller.position = new Vector3(0.25f, 1.3f, -1.0f); controller.rotation = Quaternion.Euler(0f, 0f, 0f);
                if (left != null) { left.transform.parent.position = new Vector3(-0.25f, 1.3f, -1.0f); left.transform.parent.rotation = Quaternion.identity; }
                var pistol = Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition.weaponClass == WeaponClass.Pistol);
                var rifle = Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition.weaponClass == WeaponClass.Rifle);
                var interactor = controller.GetComponentInChildren<XRBaseInputInteractor>(true);
                if (interactor != null)
                {
                    manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)pistol.Grab);
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot("06_pistol_in_right_hand_side", controller.position + new Vector3(0.5f, 0.05f, 0.05f), controller.position + new Vector3(0f, 0f, 0.05f));
                    yield return Shot("07_pistol_in_right_hand_behind", controller.position + new Vector3(0.05f, 0.25f, -0.45f), controller.position + new Vector3(0f, 0f, 0.1f));
                    manager.SelectExit((IXRSelectInteractor)interactor, (IXRSelectInteractable)pistol.Grab);
                    yield return null;
                    manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)rifle.Grab);
                    var leftInteractor = left != null ? left.transform.parent.GetComponentInChildren<XRBaseInputInteractor>(true) : null;
                    if (leftInteractor != null) { left.transform.parent.position = controller.position + new Vector3(-0.08f, 0.04f, 0.33f); yield return null; manager.SelectEnter((IXRSelectInteractor)leftInteractor, (IXRSelectInteractable)rifle.Grab); }
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("08_rifle_two_hands_side", controller.position + new Vector3(0.7f, 0.15f, 0.2f), controller.position + new Vector3(0f, 0f, 0.25f));
                    yield return Shot("09_rifle_two_hands_behind", controller.position + new Vector3(0.0f, 0.3f, -0.6f), controller.position + new Vector3(0f, 0f, 0.3f));
                    yield return Shot("09b_rifle_two_hands_front_quarter", controller.position + new Vector3(0.45f, 0.1f, 0.75f), controller.position + new Vector3(0f, 0.02f, 0.2f));
                    if (leftInteractor != null) manager.SelectExit((IXRSelectInteractor)leftInteractor, (IXRSelectInteractable)rifle.Grab);
                    manager.SelectExit((IXRSelectInteractor)interactor, (IXRSelectInteractable)rifle.Grab);
                    yield return null;
                    var smg = Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition.weaponClass == WeaponClass.SubmachineGun);
                    manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)smg.Grab);
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot("11_smg_one_hand_side", controller.position + new Vector3(0.5f, 0.08f, 0.1f), controller.position + new Vector3(0f, 0.03f, 0.05f));
                    if (leftInteractor != null) { left.transform.parent.position = controller.position + new Vector3(-0.05f, 0.04f, 0.12f); yield return null; manager.SelectEnter((IXRSelectInteractor)leftInteractor, (IXRSelectInteractable)smg.Grab); yield return new WaitForSeconds(0.7f); }
                    yield return Shot("12_smg_two_hands_behind", controller.position + new Vector3(0.05f, 0.28f, -0.5f), controller.position + new Vector3(0f, 0.02f, 0.1f));
                    yield return Shot("13_smg_two_hands_side", controller.position + new Vector3(0.55f, 0.12f, 0.15f), controller.position + new Vector3(0f, 0.03f, 0.08f));
                }
            }
            if (left != null) yield return Shot("10_left_hand_free", left.transform.parent.position + new Vector3(0.3f, 0.2f, -0.3f), left.transform.parent.position);
            cam.targetTexture = null; rt.Release();
            Debug.Log($"[VRRange] review shots written to {dir}");
        }
    }
}
