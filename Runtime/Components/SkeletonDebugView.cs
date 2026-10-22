using System.Collections.Generic;
using MyUtils;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime debug visualization for the live pose pipeline: makes the avatar body
/// semi-transparent and draws the 22-joint SMPL skeleton as bone lines, plus RGB
/// (XYZ) axes on the three tracked points. Uses <see cref="LineRenderer"/>s (not
/// editor Gizmos) so it shows in the Game view on a PC screen, and refreshes in
/// <see cref="Application.onBeforeRender"/> so the lines match the final posed skeleton.
/// </summary>
public sealed class SkeletonDebugView : MonoBehaviour
{
    /// <summary>Root of the SMPL-X avatar (contains the named bones).</summary>
    [field: SerializeField] public Transform AvatarRoot { get; set; }

    /// <summary>Head tracked point (RGB axes drawn here).</summary>
    [field: SerializeField] public Transform Head { get; set; }

    /// <summary>Left controller tracked point.</summary>
    [field: SerializeField] public Transform LeftController { get; set; }

    /// <summary>Right controller tracked point.</summary>
    [field: SerializeField] public Transform RightController { get; set; }

    /// <summary>Body opacity (0 = invisible, 1 = solid).</summary>
    [field: SerializeField]
    [field: Range(0f, 1f)]
    [field: Tooltip("Body opacity.")]
    public float BodyAlpha { get; set; } = 0.7f;

    /// <summary>Colour of the drawn bones.</summary>
    [field: SerializeField] public Color BoneColor { get; set; } = new Color(1f, 0.85f, 0.2f);

    /// <summary>Bone line width in metres.</summary>
    [field: SerializeField] public float BoneWidth { get; set; } = 0.012f;

    /// <summary>Axis length in metres for the tracked-point RGB triads.</summary>
    [field: SerializeField] public float AxisLength { get; set; } = 0.15f;

    // SMPL 22-joint parent indices (index in AnimUtils.boneNames order); -1 == root.
    static readonly int[] Parent = { -1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 12, 13, 14, 16, 17, 18, 19 };

    Transform[] _bones;
    readonly List<(LineRenderer lr, Transform a, Transform b)> _boneLines = new();
    readonly List<(LineRenderer lr, Transform t, int axis)> _axisLines = new();

    void Start()
    {
        if (AvatarRoot == null) { Debug.LogError("[SkeletonDebugView] AvatarRoot unset."); enabled = false; return; }
        _bones = new Transform[AnimUtils.boneNum];
        AnimUtils.BindBones(AvatarRoot, ref _bones);
        MakeBodyTransparent();
        BuildLines();
    }

    void OnEnable() => Application.onBeforeRender += UpdateLines;
    void OnDisable() => Application.onBeforeRender -= UpdateLines;

#if UNITY_EDITOR
    // During Play the Scene view does not auto-repaint each frame, so the bone lines and
    // gizmos look frozen there even though they animate in the Game view. Force a repaint.
    // (Equivalent to enabling the Scene view's "Always Refresh" toggle, but automatic.)
    void Update()
    {
        if (Application.isPlaying)
            UnityEditor.SceneView.RepaintAll();
    }
#endif

    void MakeBodyTransparent()
    {
        // Configure a URP/Lit material for alpha blending (the runtime-equivalent of
        // switching Surface Type to Transparent in the Inspector).
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        {
            color = new Color(0.8f, 0.8f, 0.85f, BodyAlpha),
            renderQueue = (int)RenderQueue.Transparent,
        };
        mat.SetFloat("_Surface", 1f); // 0 opaque, 1 transparent
        mat.SetFloat("_Blend", 0f);   // alpha
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        foreach (var r in AvatarRoot.GetComponentsInChildren<Renderer>(true))
        {
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = mat;
            r.materials = arr;
        }
    }

    LineRenderer MakeLine(string name, Color c, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        // Unlit so colours read true regardless of lighting; draw over the body (x-ray).
        var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c, renderQueue = 5000 };
        lm.SetInt("_ZTest", (int)CompareFunction.Always);
        lr.material = lm;
        lr.startColor = lr.endColor = c;
        return lr;
    }

    void BuildLines()
    {
        for (int i = 0; i < AnimUtils.boneNum; i++)
            if (Parent[i] >= 0)
                _boneLines.Add((MakeLine($"bone_{i}", BoneColor, BoneWidth), _bones[i], _bones[Parent[i]]));

        var pts = new[] { Head, LeftController, RightController };
        var cols = new[] { Color.red, Color.green, Color.blue }; // X, Y, Z
        foreach (var p in pts)
            if (p != null)
                for (int ax = 0; ax < 3; ax++)
                    _axisLines.Add((MakeLine($"{p.name}_axis{ax}", cols[ax], BoneWidth * 0.7f), p, ax));
    }

    void UpdateLines()
    {
        foreach (var (lr, a, b) in _boneLines)
        {
            lr.SetPosition(0, a.position);
            lr.SetPosition(1, b.position);
        }
        foreach (var (lr, t, axis) in _axisLines)
        {
            Vector3 dir = axis == 0 ? t.right : axis == 1 ? t.up : t.forward;
            lr.SetPosition(0, t.position);
            lr.SetPosition(1, t.position + dir * AxisLength);
        }
    }
}
