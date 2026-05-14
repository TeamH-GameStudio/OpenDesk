using System;
using UnityEngine;

namespace OpenDesk.Cinematic
{
    // Orbital camera move that keeps a LookAt target framed throughout the
    // interpolation. Authors think in cinematography terms — yaw (left/right),
    // pitch (above/below), distance — instead of raw position + rotation pairs.
    //
    // Pitch convention: positive = camera ABOVE subject (high angle, looking
    // down on them). Negative = camera BELOW subject (low angle, looking up
    // at them). Matches the "low angle vs high angle" framing the user is
    // already thinking in.
    //
    // Camera position formula:
    //   pivot = LookAt.position + PivotOffset
    //   offset = Quaternion.Euler(-pitch, yaw, 0) * (0, 0, -distance)
    //   camera.position = pivot + offset
    //   camera.LookAt(pivot)
    [Serializable]
    public struct CameraMove
    {
        [Tooltip("Seconds from scene start to begin the move.")]
        public float StartTime;

        [Min(0f)]
        [Tooltip("Move length. Camera snaps to To-values when this is 0.")]
        public float Duration;

        [Tooltip("Camera being moved. Leave null to use Camera.main at runtime.")]
        public Camera Camera;

        [Tooltip("Pivot the camera orbits and always looks at. Wire the character root (or anything else you want framed).")]
        public Transform LookAt;

        [Tooltip("World offset added to LookAt.position before the camera looks at it. (0, 1.5, 0) frames the character's head instead of feet.")]
        public Vector3 PivotOffset;

        [Header("Yaw — horizontal angle (degrees, 0 = front, 90 = side)")]
        public float FromYaw;
        public float ToYaw;

        [Header("Pitch — vertical angle (degrees, +above / -below subject)")]
        public float FromPitch;
        public float ToPitch;

        [Header("Distance from pivot")]
        [Min(0.01f)] public float FromDistance;
        [Min(0.01f)] public float ToDistance;

        [Tooltip("Easing curve. Leave empty for built-in EaseInOutCubic.")]
        public AnimationCurve Easing;

        [TextArea(1, 4)]
        public string Note;
    }
}
