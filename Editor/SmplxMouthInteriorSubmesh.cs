using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrChat.Editor
{
    /// <summary>
    /// Moves the SMPL-X mouth interior onto its own submesh at model import time so it
    /// renders dark regardless of which albedo texture an agent wears.
    ///
    /// The mouth bag authored in Blender carries no UV mapping: every one of its
    /// vertices — plus the inner-lip rim that was merged into it — is collapsed onto
    /// UV (0, 0), so the whole cavity samples a single texel, the bottom-left corner of
    /// the agent's albedo map. That corner is dark in the stock SMPL-X atlas but nearly
    /// white in the SMPLitex-generated ones, which is why the cavity read black on one
    /// agent and grey on another. Splitting it off makes the cavity texture-independent.
    ///
    /// Nothing outside the cavity uses that texel: the nearest mapped UV sits 0.029 away
    /// (15 px at 512², 120 px at 4096²), and every mouth triangle is fully unmapped —
    /// there are no partially-mapped seam triangles to disambiguate. So "all three
    /// vertices sit on UV (0, 0)" identifies the interior exactly, currently 288 of the
    /// mesh's 21196 triangles.
    /// </summary>
    public sealed class SmplxMouthInteriorSubmesh : AssetPostprocessor
    {
        const string ModelPath = "Assets/SMPLX/smplx-visemes-unity.fbx";
        const string MouthMaterialPath = "Assets/SMPLX/Materials/SMPLX-MouthInterior.mat";

        void OnPostprocessModel(GameObject root)
        {
            if (!string.Equals(assetPath, ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var mouthMaterial = AssetDatabase.LoadAssetAtPath<Material>(MouthMaterialPath);
            if (mouthMaterial == null)
            {
                Debug.LogWarning(
                    $"{nameof(SmplxMouthInteriorSubmesh)}: {MouthMaterialPath} is missing, so the mouth " +
                    "interior stays on the body material and will take the colour of the texture's corner texel.");
                return;
            }

            // Deliberately not declared as an import dependency (context.DependsOnSourceAsset):
            // the material is referenced by GUID and survives edits, so a dependency would only
            // force a full re-import of the 58 MB FBX every time its colour is nudged.
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (TrySplitMouthInterior(renderer.sharedMesh))
                {
                    AssignMouthMaterial(renderer, mouthMaterial);
                }
            }
        }

        static bool TrySplitMouthInterior(Mesh mesh)
        {
            // subMeshCount > 1 means the source model already separates its materials, in which
            // case the mouth is somebody else's problem and re-splitting would corrupt the slots.
            if (mesh == null || mesh.subMeshCount != 1)
            {
                return false;
            }

            var uv = mesh.uv;
            if (uv.Length != mesh.vertexCount)
            {
                Debug.LogWarning(
                    $"{nameof(SmplxMouthInteriorSubmesh)}: '{mesh.name}' has no usable UV0, so the mouth " +
                    "interior cannot be identified.");
                return false;
            }

            var triangles = mesh.triangles;
            var body = new List<int>(triangles.Length);
            var mouth = new List<int>();

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var isMouth = IsUvOrigin(uv[triangles[i]])
                              && IsUvOrigin(uv[triangles[i + 1]])
                              && IsUvOrigin(uv[triangles[i + 2]]);
                var target = isMouth ? mouth : body;
                target.Add(triangles[i]);
                target.Add(triangles[i + 1]);
                target.Add(triangles[i + 2]);
            }

            if (mouth.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(SmplxMouthInteriorSubmesh)}: found no unmapped triangles on '{mesh.name}'. " +
                    "The model was probably re-exported with the mouth bag UV-mapped; re-check the split rule.");
                return false;
            }

            mesh.subMeshCount = 2;
            // Bounds are derived from the vertex buffer, which the split leaves untouched.
            mesh.SetTriangles(body, 0, calculateBounds: false);
            mesh.SetTriangles(mouth, 1, calculateBounds: false);
            return true;
        }

        /// <summary>
        /// Exact equality rather than an epsilon: the exporter writes literal zeroes for the
        /// unmapped island, and the nearest genuinely mapped UV is 0.029 away, so a tolerance
        /// could only ever start swallowing real surface.
        /// </summary>
        static bool IsUvOrigin(Vector2 uv) => uv.x == 0f && uv.y == 0f;

        static void AssignMouthMaterial(SkinnedMeshRenderer renderer, Material mouthMaterial)
        {
            var materials = renderer.sharedMaterials;
            if (materials.Length >= 2)
            {
                return;
            }

            renderer.sharedMaterials = new[]
            {
                materials.Length > 0 ? materials[0] : null,
                mouthMaterial,
            };
        }
    }
}
