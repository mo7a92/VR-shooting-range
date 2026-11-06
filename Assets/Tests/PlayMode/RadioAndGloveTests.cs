using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Audio;
using VRRange.Hands;
using VRRange.UI;

namespace VRRange.Tests
{
    /// <summary>0.4.1: the jazz radio on the counter, and the gloved hands.</summary>
    public class RadioAndGloveTests
    {
        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        [UnityTest]
        public IEnumerator JazzRadioPlaysCyclesStationsTurnsOffAndAnswersGripAndMenu()
        {
            var radio = JazzRadio.Instance;
            Assert.IsNotNull(radio, "radio in the scene");
            Assert.GreaterOrEqual(radio.StationCount, 4, "four jazz tracks");
            Assert.IsTrue(radio.IsOn, "the radio plays when the range opens"); Assert.AreEqual(0, radio.Station);
            Assert.AreEqual(1f, radio.Source.spatialBlend, "3D sound from the radio"); Assert.AreEqual(AudioClipLoadType.Streaming, radio.Source.clip.loadType, "music is streamed, not decompressed into memory");
            yield return XrTestRig.WaitSeconds(0.3f);
            Assert.IsTrue(radio.Source.isPlaying, "music playing");
            var display = radio.GetComponentInChildren<TMP_Text>();
            StringAssert.Contains("Bossa Antigua", display.text); StringAssert.Contains("Kevin MacLeod", display.text, "the CC BY credit is shown");
            // menu button: same as a grip
            var ui = Object.FindFirstObjectByType<RangeUI>();
            Assert.IsNotNull(ui.radioButton, "radio button on the target-controls panel");
            ui.radioButton.onClick.Invoke(); yield return null;
            Assert.AreEqual(1, radio.Station); StringAssert.Contains(radio.NowPlaying, ui.radioButton.GetComponentInChildren<TMP_Text>().text);
            // grip on the radio through the interaction manager (a hand or a ray select): next station, then the radio lets go
            var hand = XrTestRig.MakeHand("RadioHand", radio.transform.position, Quaternion.identity, InteractorHandedness.Right);
            var si = radio.GetComponent<XRSimpleInteractable>(); Assert.IsNotNull(si, "the radio is interactable");
            XrTestRig.Manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)si); yield return null; yield return null;
            Assert.AreEqual(2, radio.Station, "a grip switches station"); Assert.IsFalse(si.isSelected, "the radio is let go after the press");
            radio.Next(); Assert.AreEqual(3, radio.Station);
            radio.Next(); Assert.IsFalse(radio.IsOn, "after the last station it turns off"); yield return null;
            Assert.IsFalse(radio.Source.isPlaying); StringAssert.Contains("off", ui.radioButton.GetComponentInChildren<TMP_Text>().text);
            radio.Next(); Assert.IsTrue(radio.IsOn); Assert.AreEqual(0, radio.Station, "and back on from the first");
            Object.Destroy(hand.gameObject);
        }

        [UnityTest]
        public IEnumerator HandsWearTheTexturedGloves()
        {
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None); // the hands wake up with their controllers
            Assert.AreEqual(2, hands.Length);
            foreach (var h in hands)
            {
                var smr = h.HandRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                StringAssert.StartsWith("Glove", smr.sharedMesh.name, $"{(h.IsLeft ? "left" : "right")} hand wears the glove mesh");
                Assert.AreEqual(smr.bones.Length, smr.sharedMesh.bindposes.Length, "same rig: one bind pose per joint");
                var m = smr.sharedMaterial;
                StringAssert.StartsWith("HandGlove", m.name);
                foreach (var tex in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }) Assert.IsNotNull(m.GetTexture(tex), $"{m.name}: {tex}");
            }
            yield return null;
        }
    }
}
