using System;
using System.Collections.Generic;
using OpenDesk.Characters;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEngine;
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
    //   Start()  — equip parts, attach AnimatorOverrideController, snap to
    //              the first keyframe.
    //   Update() — scan timeline, fire CrossFadeInFixedTime whenever elapsed
    //              time crosses into a new entry.
    //
    // Crossfading: AnimatorOverrideController wraps a base controller with
    // two empty states (StateA, StateB). Each transition swaps the OTHER
    // state's clip to the new pose then calls Animator.CrossFadeInFixedTime
    // into it — Mecanim handles the actual bone interpolation via muscle
    // space (Humanoid) or shortest-arc quaternion slerp (Generic), avoiding
    // the leg-flip artifact that bare per-bone quaternion lerp produces in
    // a Playables AnimationMixerPlayable.
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

        [Header("Animator")]
        [Tooltip("Animator on the character. Its runtimeController is replaced at Start with an AnimatorOverrideController wrapping _baseController.")]
        [SerializeField] private Animator _animator;
        [Tooltip("Base AnimatorController with two empty states (StateA, StateB) and placeholder clips. SceneBuilder bakes this — leave wired.")]
        [SerializeField] private RuntimeAnimatorController _baseController;

        [Header("Timeline")]
        [SerializeField] private List<CinematicTimelineEntry> _timeline = new List<CinematicTimelineEntry>();
        [Min(0.1f)]
        [SerializeField] private float _totalDuration = 8f;

        [Header("Runtime")]
        [Tooltip("When the timeline finishes, exit play mode so Unity Recorder flushes and stops cleanly.")]
        [SerializeField] private bool _autoExitPlayMode = true;
        [SerializeField] private bool _logProgress = true;

        // Animator state. _overrideController wraps _baseController and we swap
        // the placeholder clip on each state to whatever pose the next keyframe
        // demands, then CrossFadeInFixedTime kicks in Mecanim's blending.
        private const string StateA = "StateA";
        private const string StateB = "StateB";
        private const string PlaceholderNameA = "_placeholderA";
        private const string PlaceholderNameB = "_placeholderB";

        private AnimatorOverrideController _overrideController;
        private List<KeyValuePair<AnimationClip, AnimationClip>> _overridePairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        private AnimationClip _placeholderA;
        private AnimationClip _placeholderB;
        private bool _animatorReady;
        private bool _onStateA = true;          // which state is currently displayed
        private AnimationClip _activeClip;      // last clip we transitioned to

        // MaterialPropertyBlocks for the face submaterials. Created lazily on
        // first use — field initialisers don't re-run when Unity adds new
        // private fields to an existing scripted component (the serialised
        // instance keeps null for the new field).
        private MaterialPropertyBlock _eyesMpb;
        private MaterialPropertyBlock _mouthMpb;

        // Index of the keyframe currently "active" (the largest i with
        // TimeSeconds <= elapsed). -1 means we haven't entered the first
        // keyframe yet — bumped to 0 immediately at Start().
        private int _currentIndex = -1;
        private bool _finished;

        private void Start()
        {
            EquipParts();
            SetupAnimator();
            ApplyFirstKeyframeImmediate();
        }

        private void Update()
        {
            if (_finished) return;

            float elapsed = Time.timeSinceLevelLoad;

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

            if (elapsed >= _totalDuration)
            {
                FinishSequence();
            }
        }

        // ─── Setup ──────────────────────────────────────────────────────────

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
                    _partSwapper.UnequipPart(slot);
                else
                    _partSwapper.EquipPart(slot, part.PartPrefab);
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

        private void SetupAnimator()
        {
            if (_animator == null)
            {
                Debug.LogWarning("[CinematicCaptureController] Animator not assigned — pose clips will be ignored.", this);
                return;
            }
            if (_baseController == null)
            {
                Debug.LogWarning("[CinematicCaptureController] BaseController not assigned — re-run 'Tools > OpenDesk > Create Cinematic Capture Scene' to wire it.", this);
                return;
            }

            _overrideController = new AnimatorOverrideController(_baseController);
            _animator.runtimeAnimatorController = _overrideController;

            _overridePairs.Clear();
            _overrideController.GetOverrides(_overridePairs);
            for (int i = 0; i < _overridePairs.Count; i++)
            {
                var key = _overridePairs[i].Key;
                if (key == null) continue;
                if (key.name == PlaceholderNameA) _placeholderA = key;
                else if (key.name == PlaceholderNameB) _placeholderB = key;
            }

            _animatorReady = _placeholderA != null && _placeholderB != null;
            if (!_animatorReady)
                Debug.LogWarning($"[CinematicCaptureController] Base controller is missing '{PlaceholderNameA}' or '{PlaceholderNameB}' placeholder clips — re-bake the controller via SceneBuilder.", this);
        }

        private void ApplyFirstKeyframeImmediate()
        {
            if (_timeline == null || _timeline.Count == 0) return;

            var first = _timeline[0];
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

        // ─── Pose transition (Mecanim CrossFade) ───────────────────────────

        // Snap = no fade. Resets to StateA so the next transition has a clean
        // "other state" to alternate to.
        private void SnapPose(AnimationClip clip)
        {
            if (!_animatorReady || clip == null) return;
            _onStateA = true;
            OverrideClipForCurrentState(clip);
            _animator.Play(StateA, 0, 0f);
            _activeClip = clip;
        }

        private void StartPoseTransition(AnimationClip clip, float duration)
        {
            if (!_animatorReady || clip == null) return;
            if (_activeClip == clip) return;

            if (duration <= 0f)
            {
                // Override current state's clip and replay from the start —
                // visually identical to a snap.
                OverrideClipForCurrentState(clip);
                _animator.Play(_onStateA ? StateA : StateB, 0, 0f);
                _activeClip = clip;
                return;
            }

            // Toggle to the OTHER state, drop the new clip onto it, and let
            // Mecanim crossfade. We always alternate StateA ↔ StateB so each
            // transition starts from a known steady source state.
            _onStateA = !_onStateA;
            OverrideClipForCurrentState(clip);
            _animator.CrossFadeInFixedTime(_onStateA ? StateA : StateB, duration, 0, 0f);
            _activeClip = clip;
        }

        private void OverrideClipForCurrentState(AnimationClip newClip)
        {
            if (_overrideController == null) return;
            AnimationClip placeholder = _onStateA ? _placeholderA : _placeholderB;
            if (placeholder == null) return;

            for (int i = 0; i < _overridePairs.Count; i++)
            {
                if (ReferenceEquals(_overridePairs[i].Key, placeholder))
                {
                    _overridePairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, newClip);
                    break;
                }
            }
            _overrideController.ApplyOverrides(_overridePairs);
        }

        // ─── Finish ────────────────────────────────────────────────────────

        private void FinishSequence()
        {
            _finished = true;
            if (_logProgress) Debug.Log($"[CinematicCaptureController] Sequence finished @ {_totalDuration:F2}s", this);

            if (_autoExitPlayMode)
            {
#if UNITY_EDITOR
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
