using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrChat.Editor
{
    /// <summary>
    /// Drives a viseme blendshape from the menu so the mouth interior can be
    /// inspected in Edit Mode, where the mouth is otherwise always closed.
    ///
    /// What used to live here alongside these commands was a set of tuning aids for
    /// the MouthCavity/MouthBackdrop blockers — dark primitives parented to the head
    /// bone that stood in for SMPL-X's missing mouth interior. The model now carries
    /// real mouth-bag geometry and <see cref="SmplxMouthInteriorSubmesh"/> gives it
    /// its own material, so the blockers and their tuning commands are gone.
    /// </summary>
    public static class VisemeTools
    {
        // Viseme blendshapes occupy 506-519 (Oculus order minus 'sil'); 515 is
        // 'aa', the widest jaw opening and so the best look at the cavity.
        const int AaVisemeIndex = 515;

        [MenuItem("Tools/SMPL-X/Visemes/Open Jaw (aa viseme)")]
        public static void OpenJaw() => SetJaw(100f);

        [MenuItem("Tools/SMPL-X/Visemes/Close Jaw")]
        public static void CloseJaw() => SetJaw(0f);

        static void SetJaw(float weight)
        {
            var renderers = FindVisemeRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogError("Visemes: no SMPL-X mesh with viseme blendshapes found in the open scene.");
                return;
            }

            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Set Jaw Viseme");
                renderer.SetBlendShapeWeight(AaVisemeIndex, weight);
                EditorUtility.SetDirty(renderer);
            }

            var state = weight > 0f ? "opened" : "closed";
            Debug.Log($"Visemes: {state} the jaw on {renderers.Count} mesh(es) via blendshape {AaVisemeIndex} ('aa'). " +
                      "Edit-mode skinning stalls while the editor is unfocused — click into it to see the change.");
        }

        static List<SkinnedMeshRenderer> FindVisemeRenderers()
        {
            var found = new List<SkinnedMeshRenderer>();
            foreach (var renderer in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > AaVisemeIndex)
                    found.Add(renderer);
            }

            return found;
        }
    }
}
