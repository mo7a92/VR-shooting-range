using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRRange.Weapons
{
    /// <summary>
    /// Runtime-created Input System actions for weapon extras that the XRI default actions do not cover:
    /// A/X (primary button) = reload, B/Y (secondary button) = toggle fire mode. Trigger (fire) and grip (grab)
    /// come from XRI's own Activate / Select actions on the controllers.
    /// </summary>
    public static class WeaponInput
    {
        static InputAction s_ReloadLeft, s_ReloadRight, s_ModeLeft, s_ModeRight;
        static bool s_Initialized;

        static void EnsureInitialized()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            // Touch / Index / Reverb G2: A/X = reload, B/Y = fire mode. Vive wands have no A/B: trackpad click = reload, menu = fire mode.
            s_ReloadLeft = Make("VRRange/ReloadLeft", "<XRController>{LeftHand}/primaryButton", "<XRController>{LeftHand}/trackpadClicked");
            s_ReloadRight = Make("VRRange/ReloadRight", "<XRController>{RightHand}/primaryButton", "<XRController>{RightHand}/trackpadClicked");
            s_ModeLeft = Make("VRRange/FireModeLeft", "<XRController>{LeftHand}/secondaryButton", "<XRController>{LeftHand}/menu");
            s_ModeRight = Make("VRRange/FireModeRight", "<XRController>{RightHand}/secondaryButton", "<XRController>{RightHand}/menu");
        }

        static InputAction Make(string name, string binding, string fallbackBinding = null)
        {
            var a = new InputAction(name, InputActionType.Button, binding);
            if (!string.IsNullOrEmpty(fallbackBinding)) a.AddBinding(fallbackBinding);
            a.Enable();
            return a;
        }

        /// <summary>Binding paths used (for validation/tests).</summary>
        public static string[] BindingPaths()
        {
            EnsureInitialized();
            var list = new System.Collections.Generic.List<string>();
            foreach (var a in new[] { s_ReloadLeft, s_ReloadRight, s_ModeLeft, s_ModeRight }) foreach (var b in a.bindings) list.Add(b.path);
            return list.ToArray();
        }

        public static bool ReloadPressedThisFrame(InteractorHandedness hand)
        {
            EnsureInitialized();
            return hand switch
            {
                InteractorHandedness.Left => s_ReloadLeft.WasPressedThisFrame(),
                InteractorHandedness.Right => s_ReloadRight.WasPressedThisFrame(),
                _ => s_ReloadLeft.WasPressedThisFrame() || s_ReloadRight.WasPressedThisFrame(),
            };
        }

        public static bool FireModePressedThisFrame(InteractorHandedness hand)
        {
            EnsureInitialized();
            return hand switch
            {
                InteractorHandedness.Left => s_ModeLeft.WasPressedThisFrame(),
                InteractorHandedness.Right => s_ModeRight.WasPressedThisFrame(),
                _ => s_ModeLeft.WasPressedThisFrame() || s_ModeRight.WasPressedThisFrame(),
            };
        }
    }
}
