using System.Collections;
using System.Linq;
using System.Reflection;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class DirtGeometryMaskTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator DirtIsProjectedOnRobotMeshAndWorldStrokesEraseIt()
    {
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        yield return null;

        RobotRepair robot = Object.FindAnyObjectByType<RobotRepair>();
        Assert.That(robot, Is.Not.Null);
        DirtPatch[] patches = robot.GetComponentsInChildren<DirtPatch>(true);
        Assert.That(patches, Has.Length.EqualTo(3));

        DirtPatch outer = patches.Single(patch => patch.name == "OuterDirt");
        DirtPatch inner = patches.Single(patch => patch.name == "InnerDirt");
        DirtPatch frame = patches.Single(patch => patch.name == "FrameDirt");
        Assert.That(frame.ContainsSurfacePoint(frame.transform.TransformPoint(new Vector3(0f, 0f, .025f))), Is.True);
        Vector3 backFrom = frame.transform.TransformPoint(new Vector3(-.25f, 0f, .699f));
        Vector3 backTo = frame.transform.TransformPoint(new Vector3(.25f, 0f, .699f));
        Assert.That(frame.ContainsSurfacePoint(backFrom), Is.False,
            "The same projected patch must not reach the distant back wall of the body.");
        float frameBefore = frame.Cleanliness;
        Assert.That(frame.CleanStroke(backFrom, backTo, .1f), Is.Zero);
        Assert.That(frame.Cleanliness, Is.EqualTo(frameBefore));
        Vector3 frameNormal = frame.transform.localToWorldMatrix.inverse.transpose
            .MultiplyVector(Vector3.back).normalized;
        Vector3 bendAxis = Vector3.Cross(frameNormal, Vector3.up);
        if (bendAxis.sqrMagnitude < .0001f) bendAxis = Vector3.Cross(frameNormal, Vector3.right);
        Vector3 curvedNormal = Quaternion.AngleAxis(70f, bendAxis.normalized) * frameNormal;
        Assert.That(frame.AcceptsContact(curvedNormal), Is.True,
            "Frame dirt must remain cleanable across the rounded normals of the replacement body.");
        foreach (DirtPatch patch in new[] { outer, inner })
        {
            MeshRenderer proxy = patch.GetComponent<MeshRenderer>();
            Assert.That(proxy == null || !proxy.enabled, Is.True,
                $"{patch.name} must be an invisible brush collider, not a rectangular dirt renderer.");

            MeshFilter overlay = robot.GetComponentsInChildren<MeshFilter>(true)
                .Single(filter => filter.gameObject.name == patch.name + " Dirt Surface");
            MeshFilter visibleCover = overlay.transform.parent.GetComponent<MeshFilter>();
            Assert.That(visibleCover, Is.Not.Null);
            Assert.That(overlay.sharedMesh, Is.SameAs(visibleCover.sharedMesh),
                $"{patch.name} must be projected on the real cover mesh.");

            Vector3 from = patch.transform.TransformPoint(new Vector3(-.35f, 0f, 0f));
            Vector3 to = patch.transform.TransformPoint(new Vector3(.35f, 0f, 0f));
            int cleaned = patch.CleanStroke(from, to, .05f);
            Assert.That(cleaned, Is.GreaterThan(0), $"World-space brush stroke did not erase {patch.name}.");
            Assert.That(patch.Cleanliness, Is.GreaterThan(0f));
        }

        BrushTool brush = Object.FindAnyObjectByType<BrushTool>();
        Assert.That(brush, Is.Not.Null);
        var cameraObject = new GameObject("Dirt Brush Test Camera", typeof(Camera));
        Camera view = cameraObject.GetComponent<Camera>();
        Vector3 worldFrom = outer.transform.TransformPoint(new Vector3(-.25f, 0f, 0f));
        Vector3 worldTo = outer.transform.TransformPoint(new Vector3(.25f, 0f, 0f));
        Vector3 outward = outer.transform.TransformDirection(Vector3.back).normalized;
        cameraObject.transform.SetPositionAndRotation(
            Vector3.Lerp(worldFrom, worldTo, .5f) + outward,
            Quaternion.LookRotation(-outward, outer.transform.up));
        Physics.SyncTransforms();

        float beforeBrush = outer.Cleanliness;
        Assert.That(brush.Begin(robot, view, view.WorldToScreenPoint(worldFrom)), Is.True);
        brush.Drag(view.WorldToScreenPoint(worldTo));
        Assert.That(brush.Target, Is.SameAs(outer));
        Assert.That(outer.Cleanliness, Is.GreaterThan(beforeBrush),
            "BrushTool must hit and erase OuterDirt through its invisible collider.");
        brush.End();
        Object.Destroy(cameraObject);
    }

    [UnityTest]
    public IEnumerator ProjectorSupportsArbitraryRotationAndTexturePatterns()
    {
        Texture2D emptyPattern = CreatePattern("Empty Dirt Pattern", Color.black);
        Texture2D fullPattern = CreatePattern("Full Dirt Pattern", Color.white);
        Texture2D[] patterns = { emptyPattern, fullPattern };

        GameObject fixedObject = CreatePatchObject("Rotated Fixed Dirt", false);
        DirtPatch fixedPatch = fixedObject.GetComponent<DirtPatch>();
        SetPrivate(fixedPatch, "projectionRotation", new Vector3(0f, 45f, 0f));
        SetPrivate(fixedPatch, "patternMode", DirtPatternMode.FixedTexture);
        SetPrivate(fixedPatch, "dirtPatterns", patterns);
        SetPrivate(fixedPatch, "fixedPatternIndex", 1);
        SetPrivate(fixedPatch, "patternChannel", DirtPatternChannel.Luminance);
        fixedObject.SetActive(true);
        yield return null;

        Assert.That(fixedPatch.ActivePattern, Is.SameAs(fullPattern));
        Vector3 rotatedNormal = Quaternion.Euler(0f, 45f, 0f) * Vector3.back;
        Assert.That(fixedPatch.AcceptsContact(rotatedNormal), Is.True);
        Assert.That(fixedPatch.AcceptsContact(Vector3.back), Is.False,
            "Contact filtering must follow Projection Rotation, not the original world axis.");

        Matrix4x4 projectorToWorld = fixedObject.transform.localToWorldMatrix
            * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 45f, 0f), Vector3.one);
        Vector3 from = projectorToWorld.MultiplyPoint3x4(new Vector3(-.25f, 0f, 0f));
        Vector3 to = projectorToWorld.MultiplyPoint3x4(new Vector3(.25f, 0f, 0f));
        Assert.That(fixedPatch.CleanStroke(from, to, .1f), Is.GreaterThan(0),
            "World-space cleaning must use the same rotated projector as rendering.");

        GameObject randomObjectA = CreatePatchObject("Random Dirt A", false);
        GameObject randomObjectB = CreatePatchObject("Random Dirt B", false);
        DirtPatch randomPatchA = ConfigureRandomPatch(randomObjectA, patterns, 1729);
        DirtPatch randomPatchB = ConfigureRandomPatch(randomObjectB, patterns, 1729);
        randomObjectA.SetActive(true);
        randomObjectB.SetActive(true);
        yield return null;

        Assert.That(randomPatchA.ActivePattern, Is.SameAs(randomPatchB.ActivePattern),
            "The same non-zero Pattern Seed must reproduce the same texture choice.");

        Object.Destroy(fixedObject);
        Object.Destroy(randomObjectA);
        Object.Destroy(randomObjectB);
        Object.Destroy(emptyPattern);
        Object.Destroy(fullPattern);
    }

    static GameObject CreatePatchObject(string objectName, bool active)
    {
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        instance.name = objectName;
        instance.SetActive(false);
        instance.AddComponent<DirtPatch>();
        instance.SetActive(active);
        return instance;
    }

    static DirtPatch ConfigureRandomPatch(GameObject instance, Texture2D[] patterns, int seed)
    {
        DirtPatch patch = instance.GetComponent<DirtPatch>();
        SetPrivate(patch, "patternMode", DirtPatternMode.RandomTexture);
        SetPrivate(patch, "dirtPatterns", patterns);
        SetPrivate(patch, "patternChannel", DirtPatternChannel.Luminance);
        SetPrivate(patch, "patternSeed", seed);
        return patch;
    }

    static Texture2D CreatePattern(string textureName, Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = textureName };
        texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
        texture.Apply(false, false);
        return texture;
    }

    static void SetPrivate<T>(DirtPatch patch, string fieldName, T value)
    {
        FieldInfo field = typeof(DirtPatch).GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"DirtPatch.{fieldName} was not found.");
        field.SetValue(patch, value);
    }

}
