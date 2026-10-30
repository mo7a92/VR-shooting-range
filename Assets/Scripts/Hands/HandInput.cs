using UnityEngine.InputSystem;

namespace VRRange.Hands
{
    /// <summary>Runtime-created Input System actions for grip and trigger analog values per hand (Quest Touch).</summary>
    public static class HandInput
    {
        static InputAction s_GripL, s_GripR, s_TriggerL, s_TriggerR;
        static bool s_Init;

        static void Init()
        {
            if (s_Init) return; s_Init = true;
            s_GripL = Make("VRRange/GripL", "<XRController>{LeftHand}/grip");
            s_GripR = Make("VRRange/GripR", "<XRController>{RightHand}/grip");
            s_TriggerL = Make("VRRange/TriggerL", "<XRController>{LeftHand}/trigger");
            s_TriggerR = Make("VRRange/TriggerR", "<XRController>{RightHand}/trigger");
        }

        static InputAction Make(string name, string binding)
        {
            var a = new InputAction(name, InputActionType.Value, binding, expectedControlType: "Axis");
            a.Enable();
            return a;
        }

        public static float Grip(bool left) { Init(); return (left ? s_GripL : s_GripR).ReadValue<float>(); }
        public static float Trigger(bool left) { Init(); return (left ? s_TriggerL : s_TriggerR).ReadValue<float>(); }
    }
}
