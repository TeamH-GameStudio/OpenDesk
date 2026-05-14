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
    // only want to change expression at this beat.
    [Serializable]
    public struct CinematicTimelineEntry
    {
        [Tooltip("Seconds from scene start. Entries MUST be authored in ascending order.")]
        public float TimeSeconds;

        [Tooltip("Face texture applied via the body renderer's eyes/mouth submaterials at this beat.")]
        public AgentExpressionKey Expression;

        [Tooltip("Animator clip played at this beat. Leave null to keep the previous pose.")]
        public AnimationClip PoseClip;

        [Tooltip("Seconds to crossfade from the previous pose. 0 = instant snap.")]
        public float CrossfadeDuration;

        [TextArea(1, 4)]
        [Tooltip("Authoring note — ignored at runtime.")]
        public string Note;
    }
}
