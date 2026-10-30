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
            var rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt;
            var mainCam = Camera.main; if (mainCam != null) mainCam.enabled = false;

            IEnumerator Shot(string name, Vector3 pos, Vector3 lookAt)
            {
                cam.transform.position = pos; cam.transform.LookAt(lookAt);
                yield return null; yield return new WaitForEndOfFrame();
                cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }

            yield return Shot("01_spawn_downrange", new Vector3(0f, 1.6f, -1.4f), new Vector3(0f, 1.3f, 10f));
            yield return Shot("02_counter", new Vector3(0f, 1.5f, -1.8f), new Vector3(0f, 1.0f, -3.2f));
            yield return Shot("03_booth_left", new Vector3(-1.2f, 1.6f, -0.9f), new Vector3(-2.4f, 1.2f, 1.0f));
            yield return Shot("04_waiting_area", new Vector3(1.5f, 1.6f, -3.0f), new Vector3(-1.5f, 1.0f, -7.0f));
            yield return Shot("05_lanes_from_above", new Vector3(0f, 2.9f, 3f), new Vector3(0f, 1.0f, 12f));

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
                }
            }
            if (left != null) yield return Shot("10_left_hand_free", left.transform.parent.position + new Vector3(0.3f, 0.2f, -0.3f), left.transform.parent.position);
            cam.targetTexture = null; rt.Release();
            Debug.Log($"[VRRange] review shots written to {dir}");
        }
    }
}
