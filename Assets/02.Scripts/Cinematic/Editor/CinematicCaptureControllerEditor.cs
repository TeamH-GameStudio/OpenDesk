using UnityEditor;
using UnityEngine;

namespace OpenDesk.Cinematic.Editor
{
    [CustomEditor(typeof(CinematicCaptureController))]
    public sealed class CinematicCaptureControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (CinematicCaptureController)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Authoring tools", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Outfit Now", GUILayout.Height(24)))
                {
                    controller.Editor_ApplyOutfitPreview();
                }

                if (GUILayout.Button("Sort Timeline By Time", GUILayout.Height(24)))
                {
                    Undo.RecordObject(controller, "Sort cinematic timeline");
                    controller.Editor_SortTimeline();
                    EditorUtility.SetDirty(controller);
                }
            }

            using (new EditorGUI.DisabledScope(controller.TimelineCount == 0))
            {
                if (GUILayout.Button($"Reset Timeline  ({controller.TimelineCount} entries)"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Reset cinematic timeline",
                            $"Remove all {controller.TimelineCount} keyframes? This cannot be undone via the inspector buttons.",
                            "Reset",
                            "Cancel"))
                    {
                        Undo.RecordObject(controller, "Reset cinematic timeline");
                        controller.Editor_ClearTimeline();
                        EditorUtility.SetDirty(controller);
                    }
                }
            }
        }
    }
}
