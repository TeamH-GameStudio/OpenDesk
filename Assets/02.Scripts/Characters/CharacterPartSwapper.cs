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
        private readonly Dictionary<string, Transform> _bodyBonesByName = new();
        private readonly Dictionary<string, Matrix4x4> _bodyBindWorld = new();

        private readonly Dictionary<string, SkinnedMeshRenderer> _equippedParts = new();

        private void Awake()
        {
            CaptureBodyBindPose();
        }

        private void CaptureBodyBindPose()
        {
            _bodyBonesByName.Clear();
            _bodyBindWorld.Clear();
            if (_bodyRenderer == null)
            {
                Debug.LogWarning("[CharacterPartSwapper] _bodyRenderer not assigned at Awake — Remap mode will fail.", this);
                return;
            }

            foreach (var bone in _bodyRenderer.bones)
            {
                if (bone == null) continue;
                _bodyBonesByName[bone.name] = bone;
                _bodyBindWorld[bone.name] = bone.localToWorldMatrix;
            }
        }

        public void EquipPart(string slotName, GameObject partPrefab)
        {
            if (_bodyRenderer == null)
            {
                Debug.LogWarning("[CharacterPartSwapper] _bodyRenderer가 할당되지 않음");
                return;
            }
            if (partPrefab == null) return;

            UnequipPart(slotName);

            var instance = Instantiate(partPrefab, transform);

            // Reset root transform — bones drive everything, transform offsets
            // would only confuse rendering.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale    = Vector3.one;

            var partRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            if (partRenderer == null)
            {
                Destroy(instance);
                return;
            }

            switch (_boneMode)
            {
                case PartBoneMode.Remap:
                    RemapWithRebake(partRenderer);
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

        // ─── Remap with bindpose rebake (recommended) ────────────────────
        //
        // Rendering math for skinned meshes:
        //   vertex_world = Σ w[i] · bones[i].localToWorld · bindposes[i] · vertex_local
        //
        // For the part to render at rest when body bones are at body bind:
        //   bones[i].localToWorld · bindposes[i] = partRenderer.localToWorld
        //
        // Solving for bindposes (using captured body bind world, not current pose):
        //   bindposes[i] = bodyBoneBindWorld[i].inverse · partRenderer.localToWorld
        //
        // Body animation then transforms bones such that the mesh deforms relative
        // to the rebaked bind — just as if the part had been originally rigged
        // against body's skeleton.
        private void RemapWithRebake(SkinnedMeshRenderer partRenderer)
        {
            var partBones = partRenderer.bones;
            int total = partBones.Length;
            var newBones = new Transform[total];
            var newBindposes = new Matrix4x4[total];
            var partRendererLocalToWorld = partRenderer.transform.localToWorldMatrix;

            int matched = 0;
            var unmatched = new List<string>();
            for (int i = 0; i < total; i++)
            {
                var partBone = partBones[i];
                if (partBone == null)
                {
                    newBindposes[i] = Matrix4x4.identity;
                    continue;
                }

                if (_bodyBonesByName.TryGetValue(partBone.name, out var bodyBone) &&
                    _bodyBindWorld.TryGetValue(partBone.name, out var bodyBindWorld))
                {
                    newBones[i] = bodyBone;
                    newBindposes[i] = bodyBindWorld.inverse * partRendererLocalToWorld;
                    matched++;
                }
                else
                {
                    unmatched.Add(partBone.name);
                    newBindposes[i] = Matrix4x4.identity;
                }
            }

            // Clone mesh so we don't mutate the shared FBX asset.
            var clonedMesh = Instantiate(partRenderer.sharedMesh);
            clonedMesh.bindposes = newBindposes;
            partRenderer.sharedMesh = clonedMesh;

            partRenderer.bones = newBones;
            partRenderer.rootBone = _bodyRenderer.rootBone;
            partRenderer.localBounds = _bodyRenderer.localBounds;

            var partArmature = partRenderer.transform.parent?.Find("Armature");
            if (partArmature != null) partArmature.gameObject.SetActive(false);

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

            var follower = instance.AddComponent<BoneFollower>();
            follower.Bind(_bodyRenderer, partRenderer);
        }
    }
}
