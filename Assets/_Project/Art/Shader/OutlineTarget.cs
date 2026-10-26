using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Outline Target")]
public sealed class OutlineTarget : MonoBehaviour
{
    private static readonly List<OutlineTarget> activeTargets = new List<OutlineTarget>();
    private static OutlineTarget[] activeTargetsSnapshot = System.Array.Empty<OutlineTarget>();
    private static bool snapshotIsDirty = true;

    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();

    internal static OutlineTarget[] ActiveTargets
    {
        get
        {
            if (!snapshotIsDirty)
                return activeTargetsSnapshot;

            activeTargets.RemoveAll(target => target == null);
            activeTargetsSnapshot = activeTargets.ToArray();
            snapshotIsDirty = false;
            return activeTargetsSnapshot;
        }
    }

    internal Renderer[] Renderers => cachedRenderers;

    internal bool Owns(Renderer candidate)
    {
        if (candidate == null)
            return false;

        return candidate.GetComponentInParent<OutlineTarget>() == this;
    }

    private void Awake()
    {
        RefreshRenderers();
    }

    private void OnEnable()
    {
        RefreshRenderers();

        if (!activeTargets.Contains(this))
        {
            activeTargets.Add(this);
            snapshotIsDirty = true;
        }
    }

    private void OnDisable()
    {
        if (activeTargets.Remove(this))
            snapshotIsDirty = true;
    }

    private void OnTransformChildrenChanged()
    {
        RefreshRenderers();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshRenderers();
    }
#endif

    public void RefreshRenderers()
    {
        Renderer[] foundRenderers = GetComponentsInChildren<Renderer>(true);

        var ownedRenderers = new List<Renderer>(foundRenderers.Length);
        foreach (Renderer candidate in foundRenderers)
        {
            if (candidate == null)
                continue;

            // A nested OutlineTarget is a separate outlined object, even when it is
            // physically touching this one.
            OutlineTarget nearestTarget = candidate.GetComponentInParent<OutlineTarget>();
            if (nearestTarget == this)
                ownedRenderers.Add(candidate);
        }

        cachedRenderers = ownedRenderers.ToArray();
    }
}
