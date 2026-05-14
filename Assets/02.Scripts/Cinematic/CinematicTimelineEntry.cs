using System;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEngine;

namespace OpenDesk.Cinematic
{
    // A single keyframe on the cinematic timeline. The controller scans the
    // list each Update() and triggers a transition whenever the elapsed time
    // crosses into a new entry.
    //
    // PoseClip = null means "keep the previous pose" — convenient when you
    // only want to change expression or lighting at this beat.
    [Serializable]
    public struct CinematicTimelineEntry
    {
        [Tooltip("Seconds from scene start. Entries MUST be authored in ascending order.")]
        public float TimeSeconds;

        [Tooltip("Face texture applied via WardrobeApplier.SetEyeExpression at this beat.")]
        public AgentExpressionKey Expression;

        [Tooltip("Animator clip played at this beat. Leave null to keep the previous pose.")]
        public AnimationClip PoseClip;

        [Tooltip("Seconds to crossfade from the previous pose. 0 = instant snap.")]
        public float CrossfadeDuration;

        public LightingTone Lighting;

        [TextArea(1, 4)]
        [Tooltip("Authoring note — ignored at runtime.")]
        public string Note;
    }

    // Lighting state at a keyframe. The controller lerps every channel from
    // the previously-applied tone toward this one over TransitionSeconds.
    // TransitionSeconds == 0 means snap immediately.
    [Serializable]
    public struct LightingTone
    {
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color KeyLightColor;

        [Min(0f)]
        public float KeyLightIntensity;

        [ColorUsage(showAlpha: false, hdr: false)]
        public Color AmbientColor;

        [Range(0f, 1f)]
        [Tooltip("Optional vignette weight (0..1). Applied to the assigned Post-Processing Volume's Vignette override. Ignored when no volume is wired.")]
        public float Vignette;

        [Min(0f)]
        [Tooltip("Seconds to lerp from the previous tone to this one. 0 = snap.")]
        public float TransitionSeconds;

        public static LightingTone Neutral => new LightingTone
        {
            KeyLightColor = Color.white,
            KeyLightIntensity = 1f,
            AmbientColor = new Color(0.5f, 0.5f, 0.5f, 1f),
            Vignette = 0f,
            TransitionSeconds = 0f,
        };
    }
}
