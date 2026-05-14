using System;
using System.Collections.Generic;
using OpenDesk.Characters;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OpenDesk.Cinematic
{
    // Maps 1:1 to the lowercase slot strings CharacterPartSwapper expects
    // ("hair"/"top"/"bottom"/"shoes"). Exposed as an enum so the inspector
    // never holds a typo'd string.
    public enum CharacterSlot
    {
        Hair,
        Top,
        Bottom,
        Shoes,
    }

    // One row of the inspector's "Character Parts" list — pairs a slot with
    // the prefab to equip. Null prefab means "unequip this slot".
    [Serializable]
    public struct PartEquip
    {
        public CharacterSlot Slot;
        public GameObject PartPrefab;
    }

    // Drives an 8-second cutscene from a single MonoBehaviour, no DI.
    //
    // Lifecycle:
    //   Start()  — equip parts, build Playable graph, snap to the first keyframe.
    //   Update() — scan timeline, fire transitions when the elapsed time crosses
    //              into a new entry, lerp lighting toward the active tone.
    //   OnDestroy() — tear down the Playable graph so Unity doesn't leak it.
    //
    // Crossfading: AnimationMixerPlayable with two inputs — sibling-style
    // weighted average (NOT AnimationLayerMixerPlayable, whose weights blend
    // each layer with the rest pose underneath and cause a visible "rise to
    // T-pose then settle into the new clip" artifact during crossfade). Each
    // keyframe change destroys the slot-0 input (old "previous"), promotes
    // the slot-1 input to slot-0, and parks the new clip at slot-1. Weights
    // tween from (1, 0) → (0, 1) over CrossfadeDuration.
    public sealed class CinematicCaptureController : MonoBehaviour
    {
        [Header("Mesh swap (CharacterPartSwapper)")]
        [Tooltip("Swapper on the character. Each entry in CharacterParts calls EquipPart(slotName, prefab) at Start.")]
        [SerializeField] private CharacterPartSwapper _partSwapper;
        [Tooltip("Parts to equip at Start. Leave a row's prefab null to unequip that slot.")]
        [SerializeField] private List<PartEquip> _characterParts = new List<PartEquip>();

        [Header("Face textures (eyes + mouth)")]
        [Tooltip("Body SkinnedMeshRenderer that exposes eyes/mouth as submaterials. Same renderer WardrobeApplier targets.")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private int _eyesMaterialIndex = 1;
        [SerializeField] private int _mouthMaterialIndex = 2;
        [SerializeField] private string _eyesTextureProperty = "_BaseMap";
        [SerializeField] private string _mouthTextureProperty = "_BaseMap";
        [Tooltip("Expression set whose textures are written to the eyes/mouth submaterials at each keyframe.")]
        [SerializeField] private EyeExpressionSetSO _eyeExpressionSet;

        [Header("Animation")]
        [Tooltip("Animator on the character. The Animator's runtimeController is replaced by a Playables graph at Start.")]
        [SerializeField] private Animator _animator;

        [Header("Timeline")]
        [SerializeField] private List<CinematicTimelineEntry> _timeline = new List<CinematicTimelineEntry>();
        [Min(0.1f)]
        [SerializeField] private float _totalDuration = 8f;

        [Header("Runtime")]
        [Tooltip("When the timeline finishes, exit play mode so Unity Recorder flushes and stops cleanly.")]
        [SerializeField] private bool _autoExitPlayMode = true;
        [SerializeField] private bool _logProgress = true;

        // Playable graph members. Built once at Start, torn down at OnDestroy.
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _previousClipPlayable; // input 0 — clip we're fading out of
        private AnimationClipPlayable _currentClipPlayable;  // input 1 — clip we're fading into
        private bool _graphBuilt;
        private AnimationClip _activeClip;                   // last clip we transitioned to (null until first PoseClip)

        // Crossfade state.
        private float _crossfadeElapsed;
        private float _crossfadeDuration;
        private bool _crossfadeActive;

        // MaterialPropertyBlocks for the face submaterials. One per slot so a
        // write to "eyes" can't stomp the "mouth" texture and vice versa.
        // Created lazily on first use — field initialisers don't re-run when
        // Unity adds new private fields to an existing scripted component (the
        // serialised instance keeps null for the new field), so we defensively
        // re-create when needed.
        private MaterialPropertyBlock _eyesMpb;
        private MaterialPropertyBlock _mouthMpb;

        // Index of the keyframe currently "active" (the largest i with TimeSeconds <= elapsed).
        // -1 means we haven't entered the first keyframe yet (the first keyframe is applied
        // synchronously at Start, so this is bumped to 0 immediately).
        private int _currentIndex = -1;
        private bool _finished;

        private void Start()
        {
            EquipParts();
            BuildGraph();
            ApplyFirstKeyframeImmediate();
        }

        private void Update()
        {
            if (_finished) return;

            float elapsed = Time.timeSinceLevelLoad;

            // Advance to the latest keyframe whose TimeSeconds has been crossed.
            // If a per-keyframe handler throws, log and keep advancing — otherwise
            // a single bad keyframe freezes the entire timeline.
            int newIndex = FindKeyframeIndex(elapsed);
            if (newIndex > _currentIndex && newIndex >= 0)
            {
                for (int i = _currentIndex + 1; i <= newIndex; i++)
                {
                    try
                    {
                        EnterKeyframe(i);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[CinematicCaptureController] Keyframe {i} threw: {ex.Message}\n{ex.StackTrace}", this);
                    }
                }
                _currentIndex = newIndex;
            }

            AdvanceCrossfade(Time.deltaTime);

            if (elapsed >= _totalDuration)
            {
                FinishSequence();
            }
        }

        private void OnDestroy()
        {
            if (_graphBuilt && _graph.IsValid())
            {
                _graph.Destroy();
                _graphBuilt = false;
            }
        }

        // ─── Setup ──────────────────────────────────────────────────────────

        // Equips each row of _characterParts through CharacterPartSwapper.
        // Slots map 1:1 to the lowercase strings WardrobeApplier uses internally
        // ("hair"/"top"/"bottom"/"shoes"), so the swapper's slot dictionary
        // stays consistent with anything else in the project that pokes at it.
        // A null prefab unequips the slot — handy for "no hat" / "no shoes".
        private void EquipParts()
        {
            if (_partSwapper == null)
            {
                Debug.LogWarning("[CinematicCaptureController] CharacterPartSwapper not assigned — mesh swaps will be skipped.", this);
                return;
            }
            if (_characterParts == null || _characterParts.Count == 0) return;

            foreach (var part in _characterParts)
            {
                string slot = SlotToName(part.Slot);
                if (part.PartPrefab == null)
                {
                    _partSwapper.UnequipPart(slot);
                }
                else
                {
                    _partSwapper.EquipPart(slot, part.PartPrefab);
                }
            }
        }

        private static string SlotToName(CharacterSlot slot)
        {
            switch (slot)
            {
                case CharacterSlot.Hair: return "hair";
                case CharacterSlot.Top: return "top";
                case CharacterSlot.Bottom: return "bottom";
                case CharacterSlot.Shoes: return "shoes";
                default: return slot.ToString().ToLowerInvariant();
            }
        }

        private void BuildGraph()
        {
            if (_animator == null)
            {
                Debug.LogWarning("[CinematicCaptureController] Animator not assigned — pose clips will be ignored.", this);
                return;
            }

            _graph = PlayableGraph.Create($"Cinematic-{gameObject.name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Two-input mixer: slot 0 = previous clip (fading out), slot 1 = current clip (fading in).
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _mixer.SetInputWeight(0, 0f);
            _mixer.SetInputWeight(1, 0f);

            var output = AnimationPlayableOutput.Create(_graph, "Anim", _animator);
            output.SetSourcePlayable(_mixer);

            _graph.Play();
            _graphBuilt = true;
        }

        private void ApplyFirstKeyframeImmediate()
        {
            if (_timeline == null || _timeline.Count == 0) return;

            var first = _timeline[0];
            // Treat the first keyframe as a synchronous snap regardless of its
            // authored CrossfadeDuration — there is no "previous" to fade from.
            ApplyExpression(first.Expression);
            SnapPose(first.PoseClip);
            _currentIndex = 0;

            if (_logProgress)
                Debug.Log($"[CinematicCaptureController] Snap to keyframe 0 @ {first.TimeSeconds:F2}s — {first.Note}", this);
        }

        // ─── Keyframe scanning ─────────────────────────────────────────────

        private int FindKeyframeIndex(float elapsed)
        {
            if (_timeline == null) return -1;
            int found = -1;
            for (int i = 0; i < _timeline.Count; i++)
            {
                if (_timeline[i].TimeSeconds <= elapsed) found = i;
                else break;
            }
            return found;
        }

        private void EnterKeyframe(int index)
        {
            if (index < 0 || index >= _timeline.Count) return;
            var entry = _timeline[index];

            ApplyExpression(entry.Expression);
            StartPoseTransition(entry.PoseClip, Mathf.Max(0f, entry.CrossfadeDuration));

            if (_logProgress)
                Debug.Log($"[CinematicCaptureController] Enter keyframe {index} @ {entry.TimeSeconds:F2}s — {entry.Note}", this);
        }

        // ─── Expression ────────────────────────────────────────────────────

        // Writes the eyes + (optional) mouth texture for `key` into the body
        // renderer's submaterial slots via MaterialPropertyBlock — same
        // technique WardrobeApplier uses, but without going through the
        // wardrobe catalog.
        //
        // GetMouth() returns null when the expression set ships eye textures
        // only — in that case the mouth submaterial keeps whatever the prefab
        // authored, so we leave it alone.
        private void ApplyExpression(AgentExpressionKey key)
        {
            if (_eyeExpressionSet == null || _bodyRenderer == null) return;

            Texture2D eyeTex = _eyeExpressionSet.Get(key);
            if (eyeTex != null)
                WriteFaceTexture(eyeTex, _eyesTextureProperty, ref _eyesMpb, _eyesMaterialIndex);

            Texture2D mouthTex = _eyeExpressionSet.GetMouth(key);
            if (mouthTex != null)
                WriteFaceTexture(mouthTex, _mouthTextureProperty, ref _mouthMpb, _mouthMaterialIndex);
        }

        // Takes the MPB field by ref so a null field (post-script-reload) is
        // lazily replaced with a fresh block AND written back to the field.
        private void WriteFaceTexture(Texture2D texture, string propertyName,
                                       ref MaterialPropertyBlock mpb, int materialIndex)
        {
            if (texture == null || _bodyRenderer == null || string.IsNullOrEmpty(propertyName)) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            if (materialIndex < 0 || materialIndex >= _bodyRenderer.sharedMaterials.Length)
            {
                Debug.LogWarning($"[CinematicCaptureController] BodyRenderer has {_bodyRenderer.sharedMaterials.Length} submaterials but materialIndex={materialIndex}. Adjust EyesMaterialIndex/MouthMaterialIndex.", this);
                return;
            }
            _bodyRenderer.GetPropertyBlock(mpb, materialIndex);
            mpb.SetTexture(propertyName, texture);
            _bodyRenderer.SetPropertyBlock(mpb, materialIndex);
        }

        // ─── Pose crossfade ────────────────────────────────────────────────

        private void SnapPose(AnimationClip clip)
        {
            if (!_graphBuilt || clip == null) return;

            DestroyPlayableSafe(ref _previousClipPlayable);
            DestroyPlayableSafe(ref _currentClipPlayable);

            _currentClipPlayable = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(_currentClipPlayable, 0, _mixer, 1);
            _mixer.SetInputWeight(0, 0f);
            _mixer.SetInputWeight(1, 1f);

            _activeClip = clip;
            _crossfadeActive = false;
        }

        private void StartPoseTransition(AnimationClip clip, float duration)
        {
            if (!_graphBuilt) return;
            if (clip == null) return;                         // null PoseClip → keep current pose
            if (_activeClip == clip) return;                  // identical clip → no-op

            if (duration <= 0f || !_currentClipPlayable.IsValid())
            {
                SnapPose(clip);
                return;
            }

            // Promote current → previous, park new clip on slot 1.
            DestroyPlayableSafe(ref _previousClipPlayable);
            _mixer.DisconnectInput(0);
            _previousClipPlayable = _currentClipPlayable;
            _graph.Connect(_previousClipPlayable, 0, _mixer, 0);
            _mixer.SetInputWeight(0, 1f);

            _mixer.DisconnectInput(1);
            _currentClipPlayable = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(_currentClipPlayable, 0, _mixer, 1);
            _mixer.SetInputWeight(1, 0f);

            _crossfadeElapsed = 0f;
            _crossfadeDuration = duration;
            _crossfadeActive = true;
            _activeClip = clip;
        }

        private void AdvanceCrossfade(float deltaTime)
        {
            if (!_crossfadeActive) return;

            _crossfadeElapsed += deltaTime;
            float t = _crossfadeDuration > 0f
                ? Mathf.Clamp01(_crossfadeElapsed / _crossfadeDuration)
                : 1f;
            _mixer.SetInputWeight(0, 1f - t);
            _mixer.SetInputWeight(1, t);

            if (t >= 1f)
            {
                _mixer.DisconnectInput(0);
                DestroyPlayableSafe(ref _previousClipPlayable);
                _crossfadeActive = false;
            }
        }

        private void DestroyPlayableSafe(ref AnimationClipPlayable playable)
        {
            if (playable.IsValid())
            {
                playable.Destroy();
            }
            playable = default;
        }

        // ─── Finish ────────────────────────────────────────────────────────

        private void FinishSequence()
        {
            _finished = true;
            if (_logProgress) Debug.Log($"[CinematicCaptureController] Sequence finished @ {_totalDuration:F2}s", this);

            if (_autoExitPlayMode)
            {
#if UNITY_EDITOR
                // Yield one frame so Unity Recorder can flush its final image
                // before play mode exits. We do this with a delayCall instead
                // of a coroutine to avoid keeping a MonoBehaviour alive.
                EditorApplication.delayCall += () =>
                {
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                };
#endif
            }
        }

        // ─── Editor support ────────────────────────────────────────────────

        public void Editor_ApplyOutfitPreview()
        {
            EquipParts();
            // Apply the first keyframe's expression (if any) so the preview
            // matches what the controller will snap to on Play.
            if (_timeline != null && _timeline.Count > 0)
            {
                ApplyExpression(_timeline[0].Expression);
            }
        }

        public void Editor_SortTimeline()
        {
            if (_timeline == null) return;
            _timeline.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
        }

        public void Editor_ClearTimeline()
        {
            _timeline?.Clear();
        }

        public int TimelineCount => _timeline?.Count ?? 0;

        private void OnValidate()
        {
            if (_timeline == null || _timeline.Count <= 1) return;
            for (int i = 1; i < _timeline.Count; i++)
            {
                if (_timeline[i].TimeSeconds < _timeline[i - 1].TimeSeconds)
                {
                    Debug.LogWarning(
                        $"[CinematicCaptureController] Timeline entry {i} (t={_timeline[i].TimeSeconds:F2}s) is earlier than entry {i - 1} (t={_timeline[i - 1].TimeSeconds:F2}s). Use the inspector's 'Sort Timeline By Time' button.",
                        this);
                    break;
                }
            }
        }
    }
}
