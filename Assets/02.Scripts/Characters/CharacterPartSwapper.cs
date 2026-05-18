using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Characters
{
    public enum PartBoneMode
    {
        // Re-target each part: clone its mesh, rebake bindposes against the
        // captured body bind world, then point bones at body's skeleton.
        // Works even when part's source skeleton differs from body's.
        // Body animation drives the part naturally.
        Remap,

        // Keep part's own Armature; copy body bone localPos/Rot to part bones
        // each LateUpdate. Cheap. Only renders correctly when body and part
        // skeletons share the same bind pose.
        FollowBody,

        // Leave part untouched. Stays in T-pose regardless of body animation.
        Standalone,
    }

    public class CharacterPartSwapper : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer _bodyRenderer; // 마네킹 본체

        [Tooltip("How part skeletons interact with the body skeleton.\n" +
                 "Remap (default): rebake part bindposes against body's bind pose, then drive\n" +
                 "  by body bones. Handles parts exported from a different rig.\n" +
                 "FollowBody: copy body bone localPos/Rot onto part bones. Requires identical bind.\n" +
                 "Standalone: do nothing; part stays in T-pose.")]
        [SerializeField] private PartBoneMode _boneMode = PartBoneMode.Remap;

        // Body bone bind-pose snapshot, captured before any Animator update so
        // the body bones are still at the fbx-imported bind. Used by the Remap
        // path to rebake part bindposes consistently regardless of when EquipPart
        // gets called during gameplay.
        //
        // Stored in SWAPPER-LOCAL space (transform.worldToLocalMatrix · bone.l2w)
        // so that any later rotation/translation of the swapper or its parents
        // does NOT bleed into the rebake formula. If we stored world matrices
        // and equipped a new part after rotating the rig, the rebake would bake
        // the swapper's rotation into the new bindposes a second time → double
        // rotation visible only on the freshly equipped part.
        private readonly Dictionary<string, Transform> _bodyBonesByName = new();
        private readonly Dictionary<string, Matrix4x4> _bodyBindLocal = new();

        private readonly Dictionary<string, SkinnedMeshRenderer> _equippedParts = new();

        private void Awake()
        {
            CaptureBodyBindPose();
        }

        private void CaptureBodyBindPose()
        {
            _bodyBonesByName.Clear();
            _bodyBindLocal.Clear();
            if (_bodyRenderer == null)
            {
                Debug.LogWarning("[CharacterPartSwapper] _bodyRenderer not assigned at Awake — Remap mode will fail.", this);
                return;
            }

            // Capture in swapper-local space so the rebake stays valid even if
            // the rig (or any ancestor) is rotated/translated after Awake.
            var swapperWorldToLocal = transform.worldToLocalMatrix;
            foreach (var bone in _bodyRenderer.bones)
            {
                if (bone == null) continue;
                _bodyBonesByName[bone.name] = bone;
                _bodyBindLocal[bone.name] = swapperWorldToLocal * bone.localToWorldMatrix;
            }
        }

        public void EquipPart(string slotName, GameObject partPrefab)
        {
            if (_bodyRenderer == null)
            {
                Debug.LogWarning("[CharacterPartSwapper] _bodyRenderer가 할당되지 않음");
                return;
            }
            if (partPrefab == null)
            {
                Debug.LogWarning($"[CharacterPartSwapper] {slotName}: partPrefab is null", this);
                return;
            }

            Debug.Log($"[CharacterPartSwapper] EquipPart slot='{slotName}' prefab='{partPrefab.name}' mode={_boneMode}", this);

            UnequipPart(slotName);

            var instance = Instantiate(partPrefab, transform);

            // Reset root transform — bones drive everything, transform offsets
            // would only confuse rendering.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale    = Vector3.one;

            // Inherit swapper's layer recursively. The agent preview camera
            // culls by layer (AgentPreview), so freshly instantiated parts
            // must match or they vanish from the preview RenderTexture.
            SetLayerRecursive(instance, gameObject.layer);

            var partRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            if (partRenderer == null)
            {
                Debug.LogWarning($"[CharacterPartSwapper] {slotName}: prefab '{partPrefab.name}' has no SkinnedMeshRenderer in children — destroying.", this);
                Destroy(instance);
                return;
            }

            switch (_boneMode)
            {
                case PartBoneMode.Remap:
                    RemapWithRebake(instance, partRenderer);
                    break;
                case PartBoneMode.FollowBody:
                    AttachBoneFollower(instance, partRenderer);
                    break;
                case PartBoneMode.Standalone:
                    // intentionally do nothing
                    break;
            }

            _equippedParts[slotName] = partRenderer;
        }

        // Lookup for the renderer currently equipped in `slotName`. Returns
        // false when no part is mounted or the renderer was destroyed since
        // EquipPart ran. Callers (e.g. WardrobeApplier.ApplyHairColor) need
        // this so they can write a MaterialPropertyBlock onto the live part
        // after a mesh swap.
        public bool TryGetEquippedRenderer(string slotName, out SkinnedMeshRenderer renderer)
        {
            if (_equippedParts.TryGetValue(slotName, out var existing) && existing != null)
            {
                renderer = existing;
                return true;
            }
            renderer = null;
            return false;
        }

        public void UnequipPart(string slotName)
        {
            if (_equippedParts.TryGetValue(slotName, out var existing) && existing != null)
            {
                Destroy(existing.transform.root == transform
                    ? existing.gameObject
                    : existing.transform.parent.gameObject);
                _equippedParts.Remove(slotName);
            }
        }

        // ─── Remap with bindpose rebake ──────────────────────────────────
        //
        // Skinning math:
        //   vertex_world = Σ w[i] · bones[i].localToWorld · bindposes[i] · vertex_local
        //
        // Goal: replace part bones with body bones, and adjust bindposes so the
        // part renders correctly at rest AND deforms naturally during body's
        // animation — as if the part had been rigged against body's skeleton.
        //
        // Derivation: at body's bind we want vertex to land at its original rest
        // position (where it would be if part bone i was driving with its own
        // original bindpose):
        //
        //   bodyBone.l2w_atBind · newBindpose[i] · v
        //     = partBone.l2w_atBind · originalBindpose[i] · v
        //
        // Solving:
        //   newBindpose[i] = bodyBoneBind.inverse · partBoneBind · originalBindpose[i]
        //
        // CRITICAL: bodyBoneBind and partBoneBind MUST be expressed in the same
        // frame. Storing world matrices fails when the swapper rotates between
        // capture and equip — the rotation difference gets baked into newBindpose
        // and renders as double rotation on the new part. We side-step this by
        // expressing both terms in SWAPPER-LOCAL space (multiply by
        // transform.worldToLocalMatrix). Rig motion happens in the world frame
        // and is therefore canceled out before the formula sees it.
        //
        // When body's bone animates: vertex_world =
        //   bodyBone.l2w_animated · newBindpose[i] · v
        //     = (bodyBone.l2w_animated · bodyBoneBind.inverse) · partBoneBind · originalBindpose · v
        // — vertex follows body bone like any properly rigged vertex.
        //
        // If part and body share the same bind pose (partBoneBind ≈ bodyBoneBind),
        // newBindpose simplifies to the original — degenerate to a pure bone swap.
        // Otherwise the formula compensates for the bind-pose mismatch.
        private void RemapWithRebake(GameObject instance, SkinnedMeshRenderer partRenderer)
        {
            // Disable any animator on the part so part bones stay at FBX bind pose
            // when we sample partBone.localToWorldMatrix below.
            var partAnimator = instance.GetComponentInChildren<Animator>();
            if (partAnimator != null) partAnimator.enabled = false;

            var partBones = partRenderer.bones;
            var originalBindposes = partRenderer.sharedMesh.bindposes;
            int total = partBones.Length;
            var newBones = new Transform[total];
            var newBindposes = new Matrix4x4[total];

            // Same swapper-local frame the body bind was captured in. Cancels
            // out any rig rotation that happened between Awake and now.
            var swapperWorldToLocal = transform.worldToLocalMatrix;

            int matched = 0;
            var unmatched = new List<string>();
            for (int i = 0; i < total; i++)
            {
                var partBone = partBones[i];
                var originalBindpose = (i < originalBindposes.Length)
                    ? originalBindposes[i]
                    : Matrix4x4.identity;

                if (partBone == null)
                {
                    newBindposes[i] = originalBindpose;
                    continue;
                }

                if (_bodyBonesByName.TryGetValue(partBone.name, out var bodyBone) &&
                    _bodyBindLocal.TryGetValue(partBone.name, out var bodyBoneBindLocal))
                {
                    newBones[i] = bodyBone;
                    var partBoneBindLocal = swapperWorldToLocal * partBone.localToWorldMatrix;
                    newBindposes[i] = bodyBoneBindLocal.inverse * partBoneBindLocal * originalBindpose;
                    matched++;
                }
                else
                {
                    // Fall back to part's own bone + original bindpose so the vertex
                    // at least stays put rather than collapsing to identity.
                    unmatched.Add(partBone.name);
                    newBones[i] = partBone;
                    newBindposes[i] = originalBindpose;
                }
            }

            // Clone mesh so we don't mutate the shared FBX asset.
            var clonedMesh = Instantiate(partRenderer.sharedMesh);
            clonedMesh.bindposes = newBindposes;
            partRenderer.sharedMesh = clonedMesh;

            partRenderer.bones = newBones;
            partRenderer.rootBone = _bodyRenderer.rootBone;
            partRenderer.localBounds = _bodyRenderer.localBounds;

            // Only deactivate part's Armature when ALL bones successfully remapped.
            // Unmatched bones still need part's Armature to drive them.
            if (unmatched.Count == 0)
            {
                var partArmature = partRenderer.transform.parent?.Find("Armature");
                if (partArmature != null) partArmature.gameObject.SetActive(false);
            }

            Debug.Log(
                $"[CharacterPartSwapper] {partRenderer.name}: bones {matched}/{total} matched (rebake)" +
                (unmatched.Count > 0 ? $", unmatched=[{string.Join(", ", unmatched)}]" : ""),
                partRenderer);
        }

        // ─── FollowBody (simple local copy) ─────────────────────────────

        private void AttachBoneFollower(GameObject instance, SkinnedMeshRenderer partRenderer)
        {
            var partAnimator = instance.GetComponentInChildren<Animator>();
            if (partAnimator != null) partAnimator.enabled = false;

            DiagnoseSkeletons(partRenderer);

            var follower = instance.AddComponent<BoneFollower>();
            follower.Bind(_bodyRenderer, partRenderer);
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            var children = root.transform;
            for (int i = 0; i < children.childCount; i++)
            {
                SetLayerRecursive(children.GetChild(i).gameObject, layer);
            }
        }

        // ─── Diagnostics ────────────────────────────────────────────────
        //
        // Compares body skeleton ↔ part skeleton at the moment of EquipPart.
        // Run BEFORE BoneFollower binds so part bones still reflect the
        // FBX-imported bind pose. If world positions disagree, the bindposes
        // baked into the part's mesh were calibrated against a different rig
        // than the body — and FollowBody alone cannot compensate (Remap mode
        // with bindpose rebake is required).
        private void DiagnoseSkeletons(SkinnedMeshRenderer partRenderer)
        {
            var partBones = partRenderer.bones;
            int sampleCount = Mathf.Min(5, partBones.Length);
            int totalMatched = 0;
            float totalDelta = 0f;
            float maxDelta = 0f;
            string maxDeltaBone = "";

            for (int i = 0; i < partBones.Length; i++)
            {
                var pb = partBones[i];
                if (pb == null) continue;
                if (!_bodyBonesByName.TryGetValue(pb.name, out var bb)) continue;
                totalMatched++;
                float delta = Vector3.Distance(pb.position, bb.position);
                totalDelta += delta;
                if (delta > maxDelta) { maxDelta = delta; maxDeltaBone = pb.name; }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[CharacterPartSwapper] DIAG '{partRenderer.name}': partBones={partBones.Length} matched={totalMatched}");
            if (totalMatched > 0)
                sb.Append($" avgDelta={totalDelta/totalMatched:F4}m maxDelta={maxDelta:F4}m@'{maxDeltaBone}'");
            sb.Append($"\n  partRenderer.transform world={partRenderer.transform.position}");
            sb.Append($"\n  partRenderer.rootBone={(partRenderer.rootBone != null ? partRenderer.rootBone.name : "<null>")}");
            sb.Append($"\n  bodyRenderer.rootBone={(_bodyRenderer.rootBone != null ? _bodyRenderer.rootBone.name : "<null>")}");
            for (int i = 0; i < sampleCount; i++)
            {
                var pb = partBones[i];
                if (pb == null) continue;
                _bodyBonesByName.TryGetValue(pb.name, out var bb);
                sb.Append($"\n  [{i}] {pb.name}: part={pb.position} body={(bb != null ? bb.position.ToString() : "<missing>")}");
            }
            Debug.Log(sb.ToString(), partRenderer);
        }
    }
}
