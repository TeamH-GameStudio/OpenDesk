using System;
using UnityEngine;

namespace OpenDesk.Cinematic
{
    // Lightweight transform tween for one-shot cinematic moves — furniture
    // slide-in, character spin, camera dolly. Drives Target.localPosition
    // and/or Target.localRotation from a From → To pair over Duration,
    // shaped by an AnimationCurve.
    //
    // Rotation uses Vector3.Lerp on euler angles (not Quaternion slerp) so
    // a 0° → 360° spin actually rotates a full turn — slerp would treat
    // those as the same orientation and skip the animation entirely.
    [Serializable]
    public struct TimedTween
    {
        [Tooltip("Seconds from scene start to begin the tween.")]
        public float StartTime;

        [Min(0f)]
        [Tooltip("Tween length. Object snaps to To-values when this is 0.")]
        public float Duration;

        [Tooltip("Transform that gets animated. Local space — wire the actual scene object.")]
        public Transform Target;

        [Tooltip("If true, the controller sets Target's GameObject inactive at scene start and re-activates it when StartTime arrives. Use for objects that should pop into view mid-sequence.")]
        public bool ActivateOnStart;

        [Header("Position")]
        public bool TweenPosition;
        public Vector3 FromLocalPos;
        public Vector3 ToLocalPos;

        [Header("Rotation (euler degrees — supports >360 for spins)")]
        public bool TweenRotation;
        public Vector3 FromLocalEuler;
        public Vector3 ToLocalEuler;

        [Tooltip("Easing curve. (0,0) → (1,1) gives linear. Leave default for built-in EaseInOutCubic.")]
        public AnimationCurve Easing;

        [TextArea(1, 4)]
        public string Note;
    }
}
