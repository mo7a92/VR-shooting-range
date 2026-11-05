using System.Collections.Generic;
using UnityEngine;
using VRRange.Weapons;

namespace VRRange.Hands
{
    /// <summary>
    /// Grasp solve used while a weapon is held (round 3).
    /// - Palm placement: the palm skin is brought onto the contact surfaces along the palm normal, from either side.
    ///   The exact ray exit of every palm vertex against every contact shape gives the offset at which the palm just touches.
    ///   The old solve only ever pushed out, so a palm whose anchor sat off the surface kept hovering.
    /// - Fingers: a coupled grasp closure. Every joint of a finger closes together; when a segment touches, the joints behind
    ///   it lock and the joints beyond it keep closing, so the finger wraps around the grip instead of stopping as a whole at
    ///   the first touch (which left fingers sticking straight out in front of the grip).
    /// - Thumb: the same closure, searched over the opposition angle, with this hand's own fingers as obstacles. The old
    ///   search required the shooting thumb to stay on the palm's side of the grip, but a real high grip puts the thumb on
    ///   the far side of the frame, so it rejected every natural pose.
    /// </summary>
    public partial class HandPresence
    {
        // per-joint closure angles (degrees about each joint's curl axis, from the rest pose): metacarpal, proximal, intermediate, distal
        readonly float[,] m_GraspDeg = new float[5, 4], m_GraspTargetDeg = new float[5, 4];
        readonly bool[] m_GraspOn = new bool[5];
        float m_GraspOpposition, m_GraspTargetOpposition;
        /// <summary>True when the finger is posed by the grasp closure (holding a weapon) instead of the pose curl.</summary>
        public bool GraspActive(Finger f) => m_GraspOn[(int)f];
        /// <summary>Solved closure angle (degrees) of a finger joint: 0 metacarpal, 1 proximal, 2 intermediate, 3 distal.</summary>
        public float GraspAngle(Finger f, int joint) => m_GraspTargetDeg[(int)f, joint];
        public string GraspInfo { get; private set; } = "";

        // anatomical flexion limits for the closure (degrees): MCP ~90, PIP ~100, DIP ~80; thumb CMC / MCP / IP
        static readonly float[] kFingerMax = { 0f, 88f, 100f, 75f };
        static readonly float[] kThumbMax = { 25f, 50f, 50f, 70f };
        const float kTouchSlack = 0.001f;      // a segment within flesh radius + 1 mm of a surface touches it
        const float kPenetrateDepth = 0.0015f; // deeper than 1.5 mm of flesh overlap is a penetration
        const int kClosureSteps = 30;

        readonly List<ContactShape> m_ThumbObstacles = new List<ContactShape>();
        readonly Transform[] m_ChainJoint = new Transform[4];
        readonly Vector3[] m_SegA = new Vector3[4], m_SegB = new Vector3[4];

        // trigger finger: rest pose along the frame (shooting hand), blended toward the trigger curl as the trigger is pulled
        readonly float[] m_IndexRestDeg = new float[4];
        bool m_IndexRestOn;
        const bool kIndexRestPose = true;
        float m_TriggerForIndex;

        /// <summary>Poses the trigger finger: its rest pose along the frame blended into the trigger curl (fully on the trigger at 30 % pull).</summary>
        bool ApplyIndexRest(float curl, float spread)
        {
            if (!m_IndexRestOn) return false;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_TriggerForIndex / 0.3f));
            float a1 = Mathf.Lerp(m_IndexRestDeg[1], curl * m_ProximalMaxDegrees, t);
            float a2 = Mathf.Lerp(m_IndexRestDeg[2], curl * m_IntermediateMaxDegrees, t);
            float a3 = Mathf.Lerp(m_IndexRestDeg[3], curl * m_DistalMaxDegrees, t);
            ApplyJointAngles(1, 0f, a1, a2, a3, spread, 0f);
            return true;
        }

        void ResetGrasp()
        {
            m_IndexRestOn = false;
            for (int f = 0; f < 5; f++) m_GraspOn[f] = false;
            m_GraspTargetOpposition = 0f;
        }

        /// <summary>Poses one finger from explicit joint angles (degrees), with the thumb's spread and opposition at the metacarpal.</summary>
        void ApplyJointAngles(int f, float a0, float a1, float a2, float a3, float spread, float opposition)
        {
            var c = m_Fingers[f]; if (c == null) return;
            m_ChainJoint[0] = c.metacarpal; m_ChainJoint[1] = c.proximal; m_ChainJoint[2] = c.intermediate; m_ChainJoint[3] = c.distal;
            for (int j = 0; j < 4; j++)
            {
                var t = m_ChainJoint[j]; if (t == null) continue;
                float a = j == 0 ? a0 : j == 1 ? a1 : j == 2 ? a2 : a3;
                if (f != 0 && j == 0) a = 0f; // finger metacarpals do not flex
                var rot = c.rest[j] * Quaternion.AngleAxis(a, c.curlAxisLocal[j]);
                if (f == 0 && j == 0 && c.spreadAxisLocal.sqrMagnitude > 0f)
                    rot = rot * Quaternion.AngleAxis((spread - 0.5f) * 2f * m_ThumbSpreadMaxDegrees * 0.5f, c.spreadAxisLocal);
                if (f == 0 && j == 0 && opposition != 0f && c.oppositionAxisLocal.sqrMagnitude > 0f)
                    rot = rot * Quaternion.AngleAxis(opposition, c.oppositionAxisLocal);
                t.localRotation = rot;
            }
        }

        void ApplyGraspPose(int f)
        {
            ApplyJointAngles(f, m_GraspDeg[f, 0], m_GraspDeg[f, 1], m_GraspDeg[f, 2], m_GraspDeg[f, 3], f == 0 && m_ThumbSpreadIk >= 0f ? m_ThumbSpreadIk : m_Spread, f == 0 ? m_GraspOpposition : 0f);
        }

        void SmoothGrasp(float k)
        {
            for (int f = 0; f < 5; f++)
                for (int j = 0; j < 4; j++) m_GraspDeg[f, j] = Mathf.Lerp(m_GraspDeg[f, j], m_GraspTargetDeg[f, j], k);
            m_GraspOpposition = Mathf.Lerp(m_GraspOpposition, m_GraspTargetOpposition, k);
        }

        /// <summary>Segments of a finger as (start joint index, world start, world end): only joints the rig has, the last one ends at the tip.</summary>
        int BuildSegments(JointChain c, int[] segJoint)
        {
            m_ChainJoint[0] = c.metacarpal; m_ChainJoint[1] = c.proximal; m_ChainJoint[2] = c.intermediate; m_ChainJoint[3] = c.distal;
            int n = 0;
            for (int j = 0; j < 4; j++)
            {
                var t = m_ChainJoint[j]; if (t == null) continue;
                Transform next = null;
                for (int k = j + 1; k < 4 && next == null; k++) next = m_ChainJoint[k];
                if (next == null) next = c.tip;
                if (next == null) continue;
                segJoint[n] = j; m_SegA[n] = t.position; m_SegB[n] = next.position; n++;
            }
            return n;
        }

        float MinGap(Vector3 p, List<ContactShape> extra)
        {
            float d = float.MaxValue;
            for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], p); if (sd < d) d = sd; }
            if (extra != null) for (int i = 0; i < extra.Count; i++) { float sd = SignedDistance(extra[i], p); if (sd < d) d = sd; }
            return d;
        }

        /// <summary>Smallest surface gap minus flesh radius over a segment (samples along it, the far end included).</summary>
        float SegmentGap(Vector3 a, Vector3 b, float radius, List<ContactShape> extra)
        {
            float g = float.MaxValue;
            for (int s = 1; s <= 4; s++) { float gg = MinGap(Vector3.Lerp(a, b, s * 0.25f), extra) - radius; if (gg < g) g = gg; }
            return g;
        }

        readonly int[] m_SegJoint = new int[4];
        readonly float[] m_SegGap = new float[4], m_SegGapPrev = new float[4];

        /// <summary>
        /// Coupled grasp closure of one finger from the start angles: all unlocked joints close together in small steps;
        /// a segment that touches locks its joint and every joint before it; a step that would push a segment deeper
        /// into a surface is undone for those joints. Returns the number of segments in contact.
        /// </summary>
        int CloseFinger(int f, float[] start, float[] maxDeg, float spread, float opposition, List<ContactShape> extra, float[] outDeg)
        {
            var c = m_Fingers[f];
            for (int j = 0; j < 4; j++) outDeg[j] = start[j];
            if (c == null || c.tip == null) return 0;
            float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            bool[] locked = { false, false, false, false };
            if (f != 0) locked[0] = true;
            float[] step = new float[4];
            for (int j = 0; j < 4; j++) step[j] = maxDeg[j] / kClosureSteps * 1.4f;
            ApplyJointAngles(f, outDeg[0], outDeg[1], outDeg[2], outDeg[3], spread, opposition);
            int n = BuildSegments(c, m_SegJoint);
            for (int s = 0; s < n; s++) m_SegGap[s] = SegmentGap(m_SegA[s], m_SegB[s], radius, extra);
            // a segment already resting on a surface at the start locks its joints (a proximal segment lying on the panel stays
            // there); one that starts inside a volume (a straight finger poking into the guard) does not: it may close as long as
            // it does not go deeper
            for (int s = 0; s < n; s++)
                if (m_SegGap[s] < kTouchSlack && m_SegGap[s] > -kPenetrateDepth && !(f == 0 && m_SegJoint[s] == 0)) for (int j = 0; j <= m_SegJoint[s]; j++) locked[j] = true;
            var prev = new float[4];
            for (int it = 0; it < kClosureSteps * 3; it++)
            {
                bool any = false;
                for (int j = 0; j < 4; j++) { prev[j] = outDeg[j]; if (!locked[j] && outDeg[j] < maxDeg[j]) { outDeg[j] = Mathf.Min(maxDeg[j], outDeg[j] + step[j]); any = true; } }
                if (!any) break;
                ApplyJointAngles(f, outDeg[0], outDeg[1], outDeg[2], outDeg[3], spread, opposition);
                n = BuildSegments(c, m_SegJoint);
                for (int s = 0; s < n; s++) { m_SegGapPrev[s] = m_SegGap[s]; m_SegGap[s] = SegmentGap(m_SegA[s], m_SegB[s], radius, extra); }
                // a step that pushes a segment deeper than the flesh allows is undone for the joints that move that segment and
                // retried with half the step (three times), then those joints lock: the finger ends on the surface, not short of it
                int worst = -1;
                for (int s = n - 1; s >= 0; s--)
                {
                    if (f == 0 && m_SegJoint[s] == 0) continue; // the thumb's metacarpal segment lies in the (compressible) thenar
                    if (m_SegGap[s] < -kPenetrateDepth && m_SegGap[s] < m_SegGapPrev[s] - 1e-5f) { worst = s; break; }
                }
                if (worst >= 0)
                {
                    int js = m_SegJoint[worst];
                    for (int j = 0; j < 4; j++) outDeg[j] = prev[j];
                    for (int s = 0; s < n; s++) m_SegGap[s] = m_SegGapPrev[s];
                    for (int j = 0; j <= js; j++) { if (locked[j]) continue; if (step[j] > maxDeg[j] / kClosureSteps * 0.2f) step[j] *= 0.5f; else locked[j] = true; }
                    continue;
                }
                for (int s = 0; s < n; s++)
                {
                    if (f == 0 && m_SegJoint[s] == 0) continue;
                    if (m_SegGap[s] < kTouchSlack && m_SegGap[s] > -kPenetrateDepth) for (int j = 0; j <= m_SegJoint[s]; j++) locked[j] = true;
                }
            }
            // refine: the joints undone above take half a step back toward closure if that does not penetrate
            ApplyJointAngles(f, outDeg[0], outDeg[1], outDeg[2], outDeg[3], spread, opposition);
            n = BuildSegments(c, m_SegJoint);
            int touching = 0;
            for (int s = 0; s < n; s++) if (SegmentGap(m_SegA[s], m_SegB[s], radius, extra) < kTouchSlack + 0.001f) touching++;
            return touching;
        }

        readonly float[] m_TmpDeg = new float[4], m_BestDeg = new float[4], m_Zero = new float[4];
        readonly float[] m_SeqDeg = new float[4], m_CoupledDeg = new float[4];

        /// <summary>
        /// Sequential wrap: the knuckle closes alone until a segment touches (the first bone lies on the grip's side), then the
        /// middle joint (the next bone wraps the front), then the end joint (the fingertip lands on the far side). A step that
        /// would push any segment deeper is halved and finally stops that joint.
        /// </summary>
        void CloseFingerSequential(int f, float[] maxDeg, float spread, float opposition, List<ContactShape> extra, float[] outDeg)
        {
            var c = m_Fingers[f];
            for (int j = 0; j < 4; j++) outDeg[j] = 0f;
            if (c == null || c.tip == null) return;
            float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            for (int j = f == 0 ? 0 : 1; j < 4; j++)
            {
                if (maxDeg[j] <= 0f) continue;
                if (j == 2 && c.intermediate == null) continue;
                float step = maxDeg[j] / 12f;
                ApplyJointAngles(f, outDeg[0], outDeg[1], outDeg[2], outDeg[3], spread, opposition);
                int n = BuildSegments(c, m_SegJoint);
                for (int s = 0; s < n; s++) m_SegGap[s] = SegmentGap(m_SegA[s], m_SegB[s], radius, extra);
                for (int it = 0; it < 60 && outDeg[j] < maxDeg[j]; it++)
                {
                    float before = outDeg[j];
                    outDeg[j] = Mathf.Min(maxDeg[j], outDeg[j] + step);
                    if (f == 0 && j == 1 && c.intermediate == null) { } // thumb rigs without an intermediate: proximal drives alone
                    ApplyJointAngles(f, outDeg[0], outDeg[1], outDeg[2], outDeg[3], spread, opposition);
                    n = BuildSegments(c, m_SegJoint);
                    bool deeper = false, touch = false;
                    for (int s = 0; s < n; s++)
                    {
                        float prevGap = m_SegGap[s];
                        m_SegGapPrev[s] = prevGap; m_SegGap[s] = SegmentGap(m_SegA[s], m_SegB[s], radius, extra);
                        if (m_SegJoint[s] < j || (f == 0 && m_SegJoint[s] == 0)) continue; // segments this joint does not move / the thenar
                        if (m_SegGap[s] < -kPenetrateDepth && m_SegGap[s] < prevGap - 1e-5f) deeper = true;
                        else if (m_SegGap[s] < kTouchSlack) touch = true;
                    }
                    if (deeper)
                    {
                        outDeg[j] = before; for (int s = 0; s < n; s++) m_SegGap[s] = m_SegGapPrev[s];
                        if (step > maxDeg[j] / 12f * 0.13f) { step *= 0.5f; continue; }
                        break;
                    }
                    if (touch) break;
                }
            }
        }

        /// <summary>How far the finger as posed hugs the surfaces: the summed gap of its segments (metres, 0 = every segment touching).</summary>
        float WrapGap(int f, List<ContactShape> extra)
        {
            var c = m_Fingers[f]; if (c == null) return float.MaxValue;
            float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            int n = BuildSegments(c, m_SegJoint); float sum = 0f;
            for (int s = 0; s < n; s++) { if (f == 0 && m_SegJoint[s] == 0) continue; sum += Mathf.Clamp(SegmentGap(m_SegA[s], m_SegB[s], radius, extra), 0f, 0.03f); }
            return sum;
        }

        /// <summary>Both closures (coupled and sequential); keeps the one whose segments hug the surfaces closest.</summary>
        int BestClosure(int f, float[] maxDeg, float spread, float opposition, List<ContactShape> extra, float[] outDeg, out float wrapGap)
        {
            int touch = CloseFinger(f, m_Zero, maxDeg, spread, opposition, extra, m_CoupledDeg);
            ApplyJointAngles(f, m_CoupledDeg[0], m_CoupledDeg[1], m_CoupledDeg[2], m_CoupledDeg[3], spread, opposition);
            float gC = WrapGap(f, extra);
            CloseFingerSequential(f, maxDeg, spread, opposition, extra, m_SeqDeg);
            ApplyJointAngles(f, m_SeqDeg[0], m_SeqDeg[1], m_SeqDeg[2], m_SeqDeg[3], spread, opposition);
            float gS = WrapGap(f, extra);
            var pick = gS < gC ? m_SeqDeg : m_CoupledDeg;
            for (int j = 0; j < 4; j++) outDeg[j] = pick[j];
            wrapGap = Mathf.Min(gS, gC);
            return touch;
        }

        readonly string[] m_GraspInfoParts = new string[5];

        /// <summary>
        /// One stage of the grasp for the held weapon: stage 1-4 solves that finger, stage 0 the thumb (it needs the fingers'
        /// final pose as obstacles, so it runs last). The results are pose targets for the smoothing in LateUpdate.
        /// </summary>
        void SolveGraspStage(bool indexFree, int stage)
        {
            var sb = new System.Text.StringBuilder();
            if (stage == 0) m_ThumbSpreadIk = -1f;
            if (m_Anchor != null && m_Anchor.role == GripRole.Primary && SolveShootingHandIk(indexFree, sb, stage)) { FinishInfo(stage, sb); return; }
            float spread = m_TargetSpread;
            // fingers first: their final pose is an obstacle for the thumb
            for (int f = 1; f < 5; f++)
            {
                if (f != stage) continue;
                if (m_Fingers[f] == null || (f == 1 && indexFree)) { m_GraspOn[f] = false; continue; }
                int touch = BestClosure(f, kFingerMax, spread, 0f, null, m_TmpDeg, out float wrapGap);
                // a fingertip the closure left short of every surface settles onto the nearest one
                { var tp = new float[6]; for (int j = 0; j < 4; j++) tp[j] = m_TmpDeg[j]; float[] flo = { 0f, -5f, 0f, 0f, 0f, 0f }, fhi = { 0f, kFingerMax[1], kFingerMax[2], kFingerMax[3], 0f, 0f };
                  TouchPass(f, tp, null, flo, fhi); for (int j = 0; j < 4; j++) m_TmpDeg[j] = tp[j]; }
                for (int j = 0; j < 4; j++) m_GraspTargetDeg[f, j] = m_TmpDeg[j];
                m_GraspOn[f] = true;
                sb.Append($"{(Finger)f}: {m_TmpDeg[1]:0}/{m_TmpDeg[2]:0}/{m_TmpDeg[3]:0} deg, wrap gap {wrapGap * 1000f:0} mm [{DescribeSegments(f, null)}]; ");
            }
            if (stage != 0) { FinishInfo(stage, sb); return; }
            // pose the solved fingers (and the trigger finger at its curl) so the thumb sees them where they will be
            for (int f = 1; f < 5; f++)
            {
                if (m_GraspOn[f]) ApplyJointAngles(f, 0f, m_GraspTargetDeg[f, 1], m_GraspTargetDeg[f, 2], m_GraspTargetDeg[f, 3], spread, 0f);
                else ApplyFingerCurl(f, m_TargetCurl[f], spread, 0f, 0f);
            }
            m_ThumbObstacles.Clear();
            RefreshOwnShapes();
            for (int k = 0; k < m_OwnShapes.Count; k++)
            {
                var nm = m_OwnShapeNames[k];
                if (nm.Length >= 4 && (nm.StartsWith("Seg") || nm.StartsWith("Knu")) && nm[3] != '0') m_ThumbObstacles.Add(m_OwnShapes[k]);
            }
            var th = m_Fingers[0];
            if (th != null && th.tip != null)
            {
                float bestScore = float.MaxValue, bestOpp = 0f; int bestTouch = 0; float bestGap = 0f;
                var thumbDbg = new System.Text.StringBuilder();
                for (float o = -45f; o <= 75f + 1e-3f; o += 7.5f)
                {
                    // the opposition alone must not start the thumb inside the grip or inside the fingers
                    ApplyJointAngles(0, 0f, 0f, 0f, 0f, spread, o);
                    var c = th; int n = BuildSegments(c, m_SegJoint); bool bad = false;
                    for (int s = 0; s < n && !bad; s++) if (m_SegJoint[s] != 0 && SegmentGap(m_SegA[s], m_SegB[s], m_FingerRadius * 1.1f, m_ThumbObstacles) < -0.004f) bad = true;
                    if (bad) { thumbDbg.Append($"o{o:0}:start-inside "); continue; }
                    int touch = BestClosure(0, kThumbMax, spread, o, m_ThumbObstacles, m_TmpDeg, out float thumbWrap);
                    ApplyJointAngles(0, m_TmpDeg[0], m_TmpDeg[1], m_TmpDeg[2], m_TmpDeg[3], spread, o);
                    float weaponGap = float.MaxValue; for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], th.tip.position); if (sd < weaponGap) weaponGap = sd; }
                    float tipGap = Mathf.Abs(weaponGap - m_FingerRadius * 1.1f);
                    // touching the weapon with the tip first, then more segments on the weapon, then the least opposition
                    // a high grip puts the thumb on the far side of the grip from the palm (the right thumb on the frame's left side)
                    // weapon-local x: the anchors are authored for a right hand (palm on +x), mirrored for a left one
                    bool farSide = m_HeldWeapon != null && (m_HeldWeapon.transform.InverseTransformPoint(th.tip.position).x < 0f) == (m_Anchor == null || (m_Anchor.authoredForRightHand != m_IsLeft));
                    float score = tipGap - touch * 0.002f + Mathf.Abs(o) * 0.00002f + (farSide ? 0f : 0.015f);
                    { var tl = m_HeldWeapon != null ? m_HeldWeapon.transform.InverseTransformPoint(th.tip.position) * 1000f : Vector3.zero; thumbDbg.Append($"o{o:0}:{(farSide ? "far" : "near")} tip({tl.x:0},{tl.y:0},{tl.z:0}) gap {weaponGap * 1000f:0}; "); }
                    if (score < bestScore) { bestScore = score; bestOpp = o; bestTouch = touch; bestGap = weaponGap; for (int j = 0; j < 4; j++) m_BestDeg[j] = m_TmpDeg[j]; }
                }
                if (bestScore < float.MaxValue)
                {
                    for (int j = 0; j < 4; j++) m_GraspTargetDeg[0, j] = m_BestDeg[j];
                    m_GraspTargetOpposition = bestOpp; m_GraspOn[0] = true;
                    ApplyJointAngles(0, m_BestDeg[0], m_BestDeg[1], m_BestDeg[2], m_BestDeg[3], spread, bestOpp);
                    sb.Append($" THUMB CANDIDATES {thumbDbg} ");
                    sb.Append($"Thumb [{DescribeSegments(0, m_ThumbObstacles)}]: opp {bestOpp:0}, {m_BestDeg[0]:0}/{m_BestDeg[1]:0}/{m_BestDeg[3]:0} deg, {bestTouch} seg, tip {bestGap * 1000f:0.0} mm from the weapon");
                }
                else m_GraspOn[0] = false;
            }
            FinishInfo(stage, sb);
        }

        void FinishInfo(int stage, System.Text.StringBuilder sb)
        {
            m_GraspInfoParts[stage] = sb.ToString();
            var all = new System.Text.StringBuilder();
            for (int i = 1; i <= 5; i++) { var part = m_GraspInfoParts[i % 5]; if (!string.IsNullOrEmpty(part)) all.Append(part); }
            GraspInfo = all.ToString();
        }

        /// <summary>Per segment of the finger as currently posed: gap (mm, flesh radius subtracted) and the nearest shape's name.</summary>
        string DescribeSegments(int f, List<ContactShape> extra)
        {
            var c = m_Fingers[f]; if (c == null) return "";
            float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            int n = BuildSegments(c, m_SegJoint); var sb = new System.Text.StringBuilder();
            for (int s = 0; s < n; s++)
            {
                float best = float.MaxValue; string name = "-";
                for (int u = 1; u <= 4; u++)
                {
                    var p = Vector3.Lerp(m_SegA[s], m_SegB[s], u * 0.25f);
                    for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], p) - radius; if (sd < best) { best = sd; name = i < m_ShapeNames.Count ? m_ShapeNames[i] : i.ToString(); } }
                    if (extra != null) for (int i = 0; i < extra.Count; i++) { float sd = SignedDistance(extra[i], p) - radius; if (sd < best) { best = sd; name = "ownFinger"; } }
                }
                if (s > 0) sb.Append(' ');
                sb.Append($"j{m_SegJoint[s]}:{best * 1000f:0}@{name.Replace("Contact_", "")}");
            }
            return sb.ToString();
        }

        // ---------- target-seeking IK (shooting hand) ----------
        /// <summary>
        /// Penalised distance of a finger's tip to a target: |tip - target| plus a heavy penalty for any segment sample deeper
        /// than the allowed flesh overlap in the weapon (and, for the thumb, in this hand's own fingers).
        /// </summary>
        float IkCost(int f, Vector3 target, List<ContactShape> extra, float radius)
        {
            var c = m_Fingers[f];
            int n = BuildSegments(c, m_SegJoint);
            float pen = 0f;
            for (int s = 0; s < n; s++)
            {
                if (f == 0 && m_SegJoint[s] == 0) continue; // thenar
                for (int u = 1; u <= 4; u++)
                {
                    float g = MinGap(Vector3.Lerp(m_SegA[s], m_SegB[s], u * 0.25f), extra) - radius;
                    if (g < -kPenetrateDepth) pen += (-g - kPenetrateDepth);
                }
            }
            // a slight preference for straighter joints keeps the thumb from hooking (about 5 mm of tip distance for 60 deg)
            float bend = f == 0 ? (Mathf.Abs(m_IkP[1]) + Mathf.Abs(m_IkP[3])) * 0.00008f : 0f;
            return Vector3.Distance(c.tip.position, target) + pen * 6f + bend;
        }

        // parameter vector: 0 metacarpal (thumb), 1 proximal, 2 intermediate, 3 distal, 4 opposition (thumb), 5 spread (thumb)
        readonly float[] m_IkP = new float[6];
        void IkApply(int f, float[] p) => ApplyJointAngles(f, p[0], p[1], p[2], p[3], f == 0 ? p[5] : m_TargetSpread, f == 0 ? p[4] : 0f);

        /// <summary>Coordinate-descent IK from a few seeds; returns the final cost and leaves the best parameters in outP.</summary>
        float SolveIk(int f, Vector3 target, List<ContactShape> extra, float[] lo, float[] hi, float[][] seeds, float[] outP)
        {
            var c = m_Fingers[f];
            float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            float best = float.MaxValue;
            float[] stepInit = { 6f, 8f, 8f, 8f, 12f, 0.15f };
            var step = new float[6];
            // evaluate every seed, then descend from the best one only (one descent instead of one per seed)
            float[] bestSeed = seeds[0]; float bestSeedCost = float.MaxValue;
            foreach (var sd0 in seeds)
            {
                for (int i = 0; i < 6; i++) m_IkP[i] = Mathf.Clamp(sd0[i], lo[i], hi[i]);
                IkApply(f, m_IkP);
                float c0 = IkCost(f, target, extra, radius);
                if (c0 < bestSeedCost) { bestSeedCost = c0; bestSeed = sd0; }
            }
            foreach (var seed in seeds)
            {
                for (int i = 0; i < 6; i++) { m_IkP[i] = Mathf.Clamp(seed[i], lo[i], hi[i]); step[i] = stepInit[i]; }
                IkApply(f, m_IkP);
                float cost = IkCost(f, target, extra, radius);
                for (int sweep = 0; sweep < 40; sweep++)
                {
                    bool improved = false;
                    for (int i = 0; i < 6; i++)
                    {
                        if (hi[i] - lo[i] < 1e-5f) continue;
                        if (f != 0 && (i == 0 || i == 4 || i == 5)) continue;
                        if (i == 2 && c.intermediate == null) continue;
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            float old = m_IkP[i];
                            m_IkP[i] = Mathf.Clamp(old + dir * step[i], lo[i], hi[i]);
                            if (Mathf.Approximately(m_IkP[i], old)) continue;
                            IkApply(f, m_IkP);
                            float cc = IkCost(f, target, extra, radius);
                            if (cc < cost - 1e-6f) { cost = cc; improved = true; break; }
                            m_IkP[i] = old;
                        }
                    }
                    if (!improved)
                    {
                        bool any = false;
                        for (int i = 0; i < 6; i++) { step[i] *= 0.5f; if (step[i] > stepInit[i] * 0.06f) any = true; }
                        if (!any) break;
                    }
                }
                if (cost < best) { best = cost; for (int i = 0; i < 6; i++) outP[i] = m_IkP[i]; }
            }
            return best;
        }

        /// <summary>
        /// Shooting hand: every wrapping fingertip aims at the far side of the grip at its own knuckle's height (so the finger
        /// wraps across the front strap and lies on the far panel); the thumb aims at the far side just under the frame.
        /// Grip-local frame of the anchor's grip box: x across (palm on +x for a right hand), y up the grip, z forward.
        /// </summary>
        bool SolveShootingHandIk(bool indexFree, System.Text.StringBuilder sb, int stage)
        {
            if (!(m_Anchor.gripCollider is BoxCollider box)) return false;
            var gt = box.transform; var half = box.size * 0.5f; var ctr = box.center;
            float side = (m_Anchor.authoredForRightHand != m_IsLeft) ? 1f : -1f; // +1: palm on the grip's +x side
            float lossy = Mathf.Max(1e-5f, gt.lossyScale.x);
            float r = m_FingerRadius / lossy;
            float[] lo = { 0f, -5f, 0f, 0f, 0f, 0f }, hi = { 0f, kFingerMax[1], kFingerMax[2], kFingerMax[3], 0f, 0f };
            float[][] seeds = { new[] { 0f, 30f, 30f, 20f, 0f, 0f }, new[] { 0f, 60f, 70f, 40f, 0f, 0f }, new[] { 0f, 80f, 90f, 60f, 0f, 0f } };
            var p = new float[6];
            for (int f = 1; f < 5; f++)
            {
                if (f != stage) continue;
                if (m_Fingers[f] == null || (f == 1 && indexFree)) { m_GraspOn[f] = false; continue; }
                var knuckle = gt.InverseTransformPoint(m_Fingers[f].proximal.position);
                var tl = new Vector3(ctr.x - side * (half.x + r), Mathf.Clamp(knuckle.y, ctr.y - half.y + r, ctr.y + half.y - r), ctr.z + half.z * 0.5f);
                var target = gt.TransformPoint(tl);
                float cost = SolveIk(f, target, null, lo, hi, seeds, p);
                TouchPass(f, p, null, lo, hi);
                for (int j = 0; j < 4; j++) m_GraspTargetDeg[f, j] = p[j];
                m_GraspOn[f] = true;
                IkApply(f, p);
                { var wt = m_HeldWeapon.transform; var T = wt.InverseTransformPoint(target) * 1000f; var K = wt.InverseTransformPoint(m_Fingers[f].proximal.position) * 1000f; var P = wt.InverseTransformPoint(m_Fingers[f].tip.position) * 1000f; sb.Append($"{(Finger)f}: IK {p[1]:0}/{p[2]:0}/{p[3]:0} deg, tip {cost * 1000f:0} mm from its far-side target T({T.x:0},{T.y:0},{T.z:0}) knuckle({K.x:0},{K.y:0},{K.z:0}) tip({P.x:0},{P.y:0},{P.z:0}) [{DescribeSegments(f, null)}]; "); }
            }
            if (stage == 1) m_IndexRestOn = false;
            if (stage == 1 && kIndexRestPose && indexFree && m_Fingers[1] != null && m_Fingers[1].tip != null)
            {
                // the trigger finger rests with its pad on the trigger, through the guard's opening (the player's real finger rests on
                // the controller trigger); pulling the trigger blends it into the trigger curl. Without a trigger part: along the frame.
                var anim = m_HeldWeapon != null ? m_HeldWeapon.GetComponentInChildren<WeaponAnimator>() : null;
                var trig = anim != null ? anim.TriggerTransform : null;
                Vector3 targetI = trig != null ? trig.position : gt.TransformPoint(new Vector3(ctr.x + side * (half.x + r), ctr.y + half.y + r * 1.2f, ctr.z + half.z + 0.05f / lossy));
                float costI = SolveIk(1, targetI, null, lo, hi, new[] { new[] { 0f, 0f, 0f, 0f, 0f, 0f }, new[] { 0f, 30f, 30f, 20f, 0f, 0f }, new[] { 0f, 50f, 50f, 30f, 0f, 0f } }, p);
                for (int j = 0; j < 4; j++) m_IndexRestDeg[j] = p[j];
                m_IndexRestOn = true;
                sb.Append($"Index rest: IK {p[1]:0}/{p[2]:0}/{p[3]:0} deg, tip {costI * 1000f:0} mm from {(trig != null ? "the trigger" : "the frame line")} [{DescribeSegments(1, null)}]; ");
            }
            if (stage != 0) return true;
            for (int f = 1; f < 5; f++) { if (m_GraspOn[f]) ApplyJointAngles(f, 0f, m_GraspTargetDeg[f, 1], m_GraspTargetDeg[f, 2], m_GraspTargetDeg[f, 3], m_TargetSpread, 0f); else if (f == 1 && m_IndexRestOn) ApplyJointAngles(1, 0f, m_IndexRestDeg[1], m_IndexRestDeg[2], m_IndexRestDeg[3], m_TargetSpread, 0f); else ApplyFingerCurl(f, m_TargetCurl[f], m_TargetSpread, 0f, 0f); }
            m_ThumbObstacles.Clear(); RefreshOwnShapes();
            for (int k = 0; k < m_OwnShapes.Count; k++) { var nm = m_OwnShapeNames[k]; if (nm.Length >= 4 && (nm.StartsWith("Seg") || nm.StartsWith("Knu")) && nm[3] != '0') m_ThumbObstacles.Add(m_OwnShapes[k]); }
            var th = m_Fingers[0];
            if (th != null && th.tip != null)
            {
                float rt = m_FingerRadius * 1.1f / lossy;
                // this rig's thumb base sits beside the slide when the fingers wrap the grip, so the far side of the frame is out of reach
                // without passing through it: the thumb lies forward along the near side of the frame, just above the grip, resting on it
                var tl = new Vector3(ctr.x + side * (half.x + rt), ctr.y + half.y, ctr.z + half.z * 1.5f);
                var target = gt.TransformPoint(tl);
                float[] tlo = { -20f, -20f, -20f, -20f, -70f, 0f }, thi = { kThumbMax[0], kThumbMax[1], kThumbMax[2], kThumbMax[3], 90f, 1f };
                float[][] tseeds = { new[] { 0f, 0f, 0f, 0f, -30f, 0.5f }, new[] { 10f, 20f, 20f, 20f, 0f, 0.5f }, new[] { 10f, 20f, 20f, 30f, 30f, 0.3f }, new[] { 0f, 10f, 10f, 10f, 60f, 0.5f } };
                float cost = SolveIk(0, target, m_ThumbObstacles, tlo, thi, tseeds, p);
                TouchPass(0, p, m_ThumbObstacles, tlo, thi);
                for (int j = 0; j < 4; j++) m_GraspTargetDeg[0, j] = p[j];
                m_GraspTargetOpposition = p[4]; m_GraspOn[0] = true; m_ThumbSpreadIk = p[5];
                IkApply(0, p);
                { var wt = m_HeldWeapon.transform; var T = wt.InverseTransformPoint(target) * 1000f; var B = wt.InverseTransformPoint(th.proximal.position) * 1000f; var P = wt.InverseTransformPoint(th.tip.position) * 1000f; sb.Append($" thumbT({T.x:0},{T.y:0},{T.z:0}) base({B.x:0},{B.y:0},{B.z:0}) tip({P.x:0},{P.y:0},{P.z:0}) "); }
                sb.Append($"Thumb: IK opp {p[4]:0} spread {p[5]:0.00} {p[0]:0}/{p[1]:0}/{p[3]:0} deg, tip {cost * 1000f:0} mm from its target [{DescribeSegments(0, m_ThumbObstacles)}]");
            }
            return true;
        }
        float m_ThumbSpreadIk = -1f;

        /// <summary>
        /// After an IK solve whose tip ended short of every surface: aim again at the nearest surface point (the tip moved down the
        /// signed-distance gradient by its gap), starting from the solved pose, so the tip rests on the weapon instead of near it.
        /// </summary>
        float TouchPass(int f, float[] p, List<ContactShape> extra, float[] lo, float[] hi)
        {
            var c = m_Fingers[f]; float radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius;
            float gap = float.MaxValue;
            for (int pass = 0; pass < 6; pass++)
            {
                gap = TouchStep(f, p, extra, lo, hi, radius);
                if (gap < 0.001f) break;
            }
            return gap;
        }

        float TouchStep(int f, float[] p, List<ContactShape> extra, float[] lo, float[] hi, float radius)
        {
            var c = m_Fingers[f];
            IkApply(f, p);
            var tip = c.tip.position;
            float gap = MinGap(tip, null) - radius;
            if (gap < 0.001f) return gap;
            const float e = 0.001f;
            var grad = new Vector3(MinGap(tip + Vector3.right * e, null) - MinGap(tip - Vector3.right * e, null),
                                   MinGap(tip + Vector3.up * e, null) - MinGap(tip - Vector3.up * e, null),
                                   MinGap(tip + Vector3.forward * e, null) - MinGap(tip - Vector3.forward * e, null));
            if (grad.sqrMagnitude < 1e-10f) return gap;
            var target = tip - grad.normalized * (gap + 0.0012f); // a light press: the pad flattens a little on the surface
            var seed = (float[])p.Clone();
            // 0.4.0: a finger whose closure locked with the middle segment on the surface keeps its last joints nearly straight and
            // its tip points away (a slim forend or handguard); a second seed with the outer joints flexed lets the descent find the
            // curled pose. The descent keeps whichever seed ends with the lower cost (which includes the penetration penalty).
            float[][] seeds = { seed };
            if (f != 0)
            {
                var curled = (float[])p.Clone();
                curled[2] = Mathf.Min(hi[2], curled[2] + 20f); curled[3] = Mathf.Min(hi[3], curled[3] + 30f);
                seeds = new[] { seed, curled };
            }
            SolveIk(f, target, extra, lo, hi, seeds, p);
            IkApply(f, p);
            return MinGap(c.tip.position, null) - radius;
        }

        /// <summary>
        /// Shooting hand only: the offset in the palm plane that puts the middle knuckle (bone line) a set distance behind the
        /// grip box's front face, measured along the grip's forward axis, so the fingers wrap the front strap. The anchors' own
        /// positions only have to be roughly right. currentOffset is the de-penetration offset already applied to the hand.
        /// </summary>
        Vector3 KnuckleSeatingOffset(Vector3 currentOffset)
        {
            if (m_Anchor == null || m_Anchor.role != GripRole.Primary || m_MiddleProx == null) return Vector3.zero;
            if (!(m_Anchor.gripCollider is BoxCollider box)) return Vector3.zero;
            var gt = box.transform; float lossy = Mathf.Max(1e-5f, gt.lossyScale.z);
            var k = gt.InverseTransformPoint(m_MiddleProx.position - currentOffset);
            float frontZ = box.center.z + box.size.z * 0.5f;
            float desired = frontZ - kKnuckleBehindFront / lossy;
            var along = gt.TransformVector(new Vector3(0f, 0f, desired - k.z));
            // stay in the palm plane (the contact solve owns the normal direction), and never more than 4 cm
            along = Vector3.ProjectOnPlane(along, PalmInwardWorld);
            return Vector3.ClampMagnitude(along, 0.04f);
        }
        const float kKnuckleBehindFront = 0.006f;

        // ---------- palm placement ----------
        // per palm-cloud vertex: rigid with the hand root (palm, thenar, knuckle row, wrist) or part of a digit that the
        // grasp moves (proximal finger segments beyond the knuckle, the whole thumb beyond its metacarpal)
        readonly List<bool> m_PalmCloudRigid = new List<bool>();
        // per palm-cloud vertex: in the thenar / hypothenar compression zone (palm proper, more than 30 % of the hand length off
        // the palm centre line), the same definition the acceptance test uses
        readonly List<bool> m_PalmCloudZone = new List<bool>();

        /// <summary>Called by CachePalmCloud in the rest pose, with the own shapes refreshed.</summary>
        void BuildPalmCloudRigidMask()
        {
            m_PalmCloudRigid.Clear(); m_PalmCloudZone.Clear();
            var l2w = m_HandRoot.localToWorldMatrix;
            for (int i = 0; i < m_PalmCloudLocal.Count; i++)
            {
                var v = l2w.MultiplyPoint3x4(m_PalmCloudLocal[i]);
                bool rigid = true;
                for (int k = 0; k < m_OwnShapes.Count && rigid; k++)
                {
                    var nm = m_OwnShapeNames[k]; var sh = m_OwnShapes[k];
                    if (!nm.StartsWith("Seg") || sh.kind != 1) continue;
                    if (SignedDistance(sh, v) > 0.004f) continue;
                    var ab = sh.b - sh.a; float u = ab.sqrMagnitude > 1e-10f ? Vector3.Dot(v - sh.a, ab) / ab.sqrMagnitude : 0f;
                    bool thumb = nm[3] == '0';
                    if (thumb || u > 0.2f) rigid = false; // the knuckle itself stays rigid
                }
                m_PalmCloudRigid.Add(rigid);
                bool zone = false;
                if (m_MiddleProx != null)
                {
                    var palm = PalmWorldPosition; var fwd = (m_MiddleProx.position - palm).normalized; var across = Vector3.Cross(PalmInwardWorld, fwd);
                    float handLen = Vector3.Distance(WristWorldPosition, m_MiddleProx.position);
                    var d = v - palm; float fa = Vector3.Dot(d, fwd), aa = Mathf.Abs(Vector3.Dot(d, across));
                    zone = fa <= handLen * 0.55f && fa >= -handLen * 0.45f && aa > handLen * 0.3f;
                }
                m_PalmCloudZone.Add(zone);
            }
        }
        /// <summary>
        /// Offset along the palm normal (metres, + = toward the back of the hand) at which the palm skin just touches the contact
        /// shapes: for every cached palm vertex and every shape, the exit parameter of the ray v + n s through the shape
        /// (shrunk by the vertex's flesh-compression allowance). The palm then rests where the first surface stops it, whether
        /// the anchor put it inside the weapon or in front of it. Returns false when no shape lies under the palm.
        /// </summary>
        bool SolvePalmContactOffset(Vector3 outward, out float offset, out int shapeIndex)
        {
            offset = float.NegativeInfinity; shapeIndex = -1;
            const float kReach = 0.03f; // how far the palm may travel toward a surface it does not yet touch
            bool primary = m_Anchor != null && m_Anchor.role == GripRole.Primary;
            float palmCompression = m_Anchor != null ? m_Anchor.palmCompression : 0.005f;
            for (int i = 0; i < m_BakeBuffer.Count; i++)
            {
                if (i < m_PalmCloudRigid.Count && !m_PalmCloudRigid[i]) continue;
                var v = m_BakeBuffer[i];
                float excess = m_PalmCloudExcess[i];
                for (int si = 0; si < m_Shapes.Count; si++)
                {
                    bool crossHand = m_OtherHandShapeStart >= 0 && si >= m_OtherHandShapeStart;
                    if (crossHand && primary) continue; // the shooting hand never moves for the support hand
                    // only the thenar / hypothenar bulges squash by their full excess; the rest of the palm by the anchor's allowance
                    // (5 mm; 3.5 mm on a narrow forend), and
                    // skin on skin (the other hand) by at most 3 mm
                    bool zone = i < m_PalmCloudZone.Count && m_PalmCloudZone[i];
                    float shrink = crossHand ? Mathf.Min(excess, kMaxCrossHandCompression) : zone ? excess : Mathf.Min(excess, palmCompression);
                    if (!RayInterval(m_Shapes[si], v, outward, shrink, out float t0, out float t1)) continue;
                    // the palm comes in from the back-of-hand side (large s) and stops where its first vertex reaches a surface;
                    // a shape entirely on the back-of-hand side of the vertex is not under the palm
                    if (t1 < -kReach || t0 > 0f) continue;
                    if (t1 > offset) { offset = t1; shapeIndex = si; }
                }
            }
            return shapeIndex >= 0;
        }

        /// <summary>Parameter interval [t0, t1] where p + d t is inside the shape shrunk by 'shrink' (boxes, capsules, spheres).</summary>
        static bool RayInterval(in ContactShape s, Vector3 p, Vector3 d, float shrink, out float t0, out float t1)
        {
            t0 = float.NegativeInfinity; t1 = float.PositiveInfinity;
            switch (s.kind)
            {
                case 0:
                {
                    var o = s.invRot * (p - s.center) / s.scale; var dl = s.invRot * d / s.scale;
                    float sh = shrink / s.scale;
                    for (int i = 0; i < 3; i++)
                    {
                        float h = Mathf.Max(0.0005f / s.scale, s.half[i] - sh);
                        if (Mathf.Abs(dl[i]) < 1e-8f) { if (Mathf.Abs(o[i]) > h) return false; continue; }
                        float a = (-h - o[i]) / dl[i], b = (h - o[i]) / dl[i];
                        if (a > b) { var tmp = a; a = b; b = tmp; }
                        if (a > t0) t0 = a; if (b < t1) t1 = b;
                        if (t0 > t1) return false;
                    }
                    return true;
                }
                case 2: return RaySphere(p, d, s.a, Mathf.Max(0.0005f, s.radius - shrink), out t0, out t1);
                case 4:
                {
                    var o = s.invRot * (p - s.center) / s.scale; var dl = s.invRot * d / s.scale; float sh = shrink / s.scale;
                    for (int i = 0; i < s.hullN.Length; i++)
                    {
                        float lim = s.hullD[i] - sh; float no = Vector3.Dot(s.hullN[i], o), nd = Vector3.Dot(s.hullN[i], dl);
                        if (Mathf.Abs(nd) < 1e-8f) { if (no > lim) return false; continue; }
                        float t = (lim - no) / nd;
                        if (nd > 0f) { if (t < t1) t1 = t; } else { if (t > t0) t0 = t; }
                        if (t0 > t1) return false;
                    }
                    return true;
                }
                case 1:
                {
                    float r = Mathf.Max(0.0005f, s.radius - shrink);
                    bool hit = false; t0 = float.PositiveInfinity; t1 = float.NegativeInfinity;
                    if (RaySphere(p, d, s.a, r, out float a0, out float a1)) { hit = true; t0 = Mathf.Min(t0, a0); t1 = Mathf.Max(t1, a1); }
                    if (RaySphere(p, d, s.b, r, out float b0, out float b1)) { hit = true; t0 = Mathf.Min(t0, b0); t1 = Mathf.Max(t1, b1); }
                    var ab = s.b - s.a; float len = ab.magnitude;
                    if (len > 1e-6f)
                    {
                        var u = ab / len; var w = p - s.a;
                        var dp = d - u * Vector3.Dot(d, u); var wp = w - u * Vector3.Dot(w, u);
                        float A = dp.sqrMagnitude, B = 2f * Vector3.Dot(dp, wp), C = wp.sqrMagnitude - r * r;
                        if (A > 1e-10f)
                        {
                            float disc = B * B - 4f * A * C;
                            if (disc >= 0f)
                            {
                                float sq = Mathf.Sqrt(disc);
                                float[] roots = { (-B - sq) / (2f * A), (-B + sq) / (2f * A) };
                                foreach (var t in roots)
                                {
                                    float ax = Vector3.Dot(w + d * t, u);
                                    if (ax >= 0f && ax <= len) { hit = true; t0 = Mathf.Min(t0, t); t1 = Mathf.Max(t1, t); }
                                }
                            }
                        }
                    }
                    return hit && t0 <= t1;
                }
                default: return false;
            }
        }

        static bool RaySphere(Vector3 p, Vector3 d, Vector3 c, float r, out float t0, out float t1)
        {
            var w = p - c; float b = Vector3.Dot(w, d); float cc = w.sqrMagnitude - r * r;
            float disc = b * b - cc; // d is unit
            if (disc < 0f) { t0 = t1 = 0f; return false; }
            float sq = Mathf.Sqrt(disc); t0 = -b - sq; t1 = -b + sq; return true;
        }
    }
}
