using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Characters
{
    // Mirrors the source skeleton's bone transforms onto a target skeleton each
    // LateUpdate. Used by CharacterPartSwapper when a part's mesh.bindposes were
    // exported against a different skeleton than the body — RemapBones distorts
    // such meshes, but copying local bone transforms keeps the part rendering
    // with its own bindposes while still animating in sync with the body.
    //
    // Pairing is by bone NAME (case-sensitive). Pairs cached at Bind() time so
    // the per-frame loop stays a tight indexed copy.
    public sealed class BoneFollower : MonoBehaviour
    {
        private Transform[] _sourceBones;
        private Transform[] _targetBones;

        public void Bind(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
        {
            if (source == null || target == null)
            {
                _sourceBones = null;
                _targetBones = null;
                return;
            }

            var srcMap = new Dictionary<string, Transform>();
            foreach (var bone in source.bones)
            {
                if (bone != null) srcMap[bone.name] = bone;
            }

            var pairs = new List<(Transform src, Transform dst)>();
            foreach (var bone in target.bones)
            {
                if (bone != null && srcMap.TryGetValue(bone.name, out var s))
                {
                    pairs.Add((s, bone));
                }
            }

            _sourceBones = new Transform[pairs.Count];
            _targetBones = new Transform[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                _sourceBones[i] = pairs[i].src;
                _targetBones[i] = pairs[i].dst;
            }
        }

        private void LateUpdate()
        {
            if (_sourceBones == null) return;
            for (int i = 0; i < _sourceBones.Length; i++)
            {
                var src = _sourceBones[i];
                var dst = _targetBones[i];
                if (src == null || dst == null) continue;
                dst.localPosition = src.localPosition;
                dst.localRotation = src.localRotation;
                // localScale intentionally skipped — bone scales are typically 1
                // and copying them propagates float drift between rigs.
            }
        }
    }
}
