using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Chains clips, in order, into an AnimatorController that plays them as one loop: each state hands over
/// to the next at its end and the last loops back to the first. Used for the reference-clip cycles that
/// <c>RetargetTest</c> plays (<c>gorp_ref_cycle</c>).
/// </summary>
public static class ClipCycle
{
    /// <summary>
    /// Rebuilds the controller at <paramref name="path"/> around <paramref name="clips"/>. An existing
    /// controller is edited in place, not recreated, so scenes that reference it keep the reference.
    /// </summary>
    /// <param name="stateNamePrefix">Stripped from each clip name to name its state.</param>
    public static AnimatorController Rebuild(string path, AnimationClip[] clips, string stateNamePrefix = "")
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
            ?? AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = controller.layers[0].stateMachine;
        foreach (var child in sm.states) sm.RemoveState(child.state);

        var states = new AnimatorState[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            var name = clips[i].name.StartsWith(stateNamePrefix) ? clips[i].name.Substring(stateNamePrefix.Length) : clips[i].name;
            states[i] = sm.AddState(name);
            states[i].motion = clips[i];
        }
        for (int i = 0; i < states.Length; i++)
        {
            var transition = states[i].AddTransition(states[(i + 1) % states.Length]);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0f;
        }
        if (states.Length > 0) sm.defaultState = states[0];
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
