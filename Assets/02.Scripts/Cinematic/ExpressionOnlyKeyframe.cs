using System;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEngine;

namespace OpenDesk.Cinematic
{
    // Expression-only keyframe. Unlike CinematicTimelineEntry this one never
    // touches the Animator — the active pose clip keeps playing uninterrupted
    // while only the eyes/mouth submaterials swap. Use when you want a single
    // animation to loop through multiple emotional beats (e.g. an idle pose
    // that goes Default -> Happy -> Surprised without restarting the clip).
    [Serializable]
    public struct ExpressionOnlyKeyframe
    {
        [Tooltip("Seconds from scene start. Entries SHOULD be authored in ascending order.")]
        public float TimeSeconds;

        [Tooltip("Face expression applied via the body renderer's eyes/mouth submaterials. Animation is untouched.")]
        public AgentExpressionKey Expression;

        [TextArea(1, 4)]
        [Tooltip("Authoring note — ignored at runtime.")]
        public string Note;
    }
}
