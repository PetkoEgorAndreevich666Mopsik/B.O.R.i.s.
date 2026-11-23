using System.Collections;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class ScrewdriverFlowTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    ScrewdriverTool tool;
    ScrewFastener screw;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        tool = Object.FindFirstObjectByType<ScrewdriverTool>();
        screw = Object.FindFirstObjectByType<RobotRepair>().Cover.Screws[0];
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.position = Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f));
        body.enabled = true;
        yield return null;
        yield return WorkshopTestInput.Interact();
        Assert.That(interaction.IsAtWorkbench, Is.True);
    }

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);

    }

    Vector2 Center => player.View.WorldToScreenPoint(screw.transform.position);

    [UnityTest]
    public IEnumerator HighlightDoesNotOverwriteRemovedScrewTransparency()
    {
        var robot = screw.GetComponentInParent<RobotRepair>();
        var feedback = interaction.GetComponent<RepairFeedback>();
        var highlight = typeof(RepairFeedback).GetMethod("Highlight",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        highlight.Invoke(feedback, new object[] { screw.transform });
        robot.Turn(screw, -10000f);
        highlight.Invoke(feedback, new object[] { null });
        var renderer = screw.GetComponentInChildren<MeshRenderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(.4f).Within(.001f));
        highlight.Invoke(feedback, new object[] { robot.Cover.transform });
        screw.SetHovered(true);
        highlight.Invoke(feedback, new object[] { null });
        renderer.GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(.85f).Within(.001f));
        screw.SetHovered(false);
        renderer.GetPropertyBlock(properties);
        Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(.4f).Within(.001f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator HoldUsesElapsedTimeIndependentlyOfFrameRate()
    {
        var robot = screw.GetComponentInParent<RobotRepair>();
        Assert.That(tool.Begin(screw, player.View, Center), Is.True);
        tool.Hold(.12f);
        float singleFrame = screw.Tightness;
        tool.End();
        robot.Turn(screw, 10000f);
        Assert.That(tool.Begin(screw, player.View, Center), Is.True);
        for (int i = 0; i < 3; i++) tool.Hold(.04f);
        Assert.That(screw.Tightness, Is.EqualTo(singleFrame).Within(.001f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator HoldCompletesUnscrewingAndTighteningWithoutWheel()
    {
        yield return Pointer(Center, true);
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsRemoved, Is.True);
        Assert.That(tool.IsTurning, Is.False);
        yield return new WaitForSeconds(.2f);
        Assert.That(screw.IsRemoved, Is.True, "Continuing to hold must not reinsert the screw.");
        yield return Pointer(Center, false);
        yield return Pointer(Center, true);
        Assert.That(screw.IsRemoved, Is.False);
        Assert.That(tool.Target, Is.SameAs(screw));
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsTight, Is.True);
        Assert.That(tool.IsTurning, Is.False);
    }

    [UnityTest]
    public IEnumerator ScrewWorksWithoutSelectingToolAndBrushTogglesOff()
    {
        Assert.That(tool.IsSelected, Is.False);
        yield return Pointer(Center, true);
        Assert.That(tool.Target, Is.SameAs(screw));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = Center, buttons = 1, scroll = new Vector2(0, -120) });
        yield return null; yield return null;
        Assert.That(screw.Tightness, Is.LessThan(1f));
        yield return Pointer(Center, false);
        Assert.That(tool.IsTurning, Is.False);
        yield return Press(Key.Digit1);
        Assert.That(interaction.BrushSelected, Is.True);
        yield return Press(Key.Digit1);
        Assert.That(interaction.BrushSelected, Is.False);
        yield return Pointer(Center, true);
        Assert.That(tool.Target, Is.SameAs(screw));
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(tool.IsTurning, Is.False);
    }

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator RemovedToolKeysDoNothingAndHoldingScrewNeedsNoWheel()
    {
        Vector3 home = tool.transform.position;
        foreach (var key in new[] { Key.B, Key.Digit2, Key.Digit3, Key.Digit4 }) yield return Press(key);
        Assert.That(tool.IsSelected, Is.False);
        Assert.That(interaction.BrushSelected, Is.False);
        Assert.That(tool.transform.position, Is.EqualTo(home));
        yield return Pointer(Center, true);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = Center, buttons = 1, scroll = new Vector2(0, 120) });
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsRemoved, Is.True);
        Assert.That(screw.GetComponentInParent<RobotRepair>().ScrewTray.Count, Is.EqualTo(1));
        Assert.That(tool.IsSelected, Is.False);
        yield return Pointer(Center, false);
        yield return Pointer(Center, true);
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsTight, Is.True);
        Assert.That(tool.Target, Is.Null);
        Assert.That(tool.transform.position, Is.EqualTo(home));
        Assert.That(tool.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
    }

    [UnityTest]
    public IEnumerator MovingPointerWhileHoldingKeepsCapturedScrew()
    {
        yield return Pointer(Center, true);
        Assert.That(tool.Target, Is.SameAs(screw));
        yield return Pointer(Center + new Vector2(100f, 80f), true);
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsRemoved, Is.True);
        yield return Pointer(Center, false);
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.True);
        yield return Pointer(Center - new Vector2(100f, 80f), true);
        yield return new WaitForSeconds(1.1f);
        Assert.That(screw.IsTight, Is.True);
    }

    [UnityTest]
    public IEnumerator PartialTurnSurvivesReleaseAndWorkbenchVisits()
    {
        yield return Pointer(Center, true);
        yield return new WaitForSeconds(.1f);
        yield return Pointer(Center, false);
        float progress = screw.Tightness;
        Vector3 position = screw.transform.localPosition;
        Assert.That(progress, Is.InRange(.1f, .95f));
        Assert.That(tool.IsTurning, Is.False);
        yield return Press(Key.Escape);
        yield return WorkshopTestInput.Interact();
        Assert.That(screw.Tightness, Is.EqualTo(progress));
        Assert.That(screw.transform.localPosition, Is.EqualTo(position));
    }

    [UnityTest]
    public IEnumerator CenterAndPointerWarpDoNotTurn_AndSamplingDoesNotChangePath()
    {
        Assert.That(tool.Begin(screw, player.View, Center), Is.True);
        tool.Drag(Center);
        tool.Drag(Center + Vector2.right * 60f);
        tool.Drag(Center + Vector2.left * 60f);
        Assert.That(screw.Tightness, Is.EqualTo(1f));
        tool.End();
        foreach (int samples in new[] { 24, 48, 96 })
        {
            screw.GetComponentInParent<RobotRepair>().Turn(screw, 10000f);
            tool.Begin(screw, player.View, Center + Vector2.right * 60f);
            for (int i = 1; i <= samples; i++)
            {
                float angle = i * 360f / samples * Mathf.Deg2Rad;
                tool.Drag(Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 60f);
            }
            Assert.That(screw.Tightness, Is.EqualTo(.5f).Within(.005f), "Samples: " + samples);
            tool.End();
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator BrushToggleCancelsScrewAndOccluderBlocksCapture()
    {
        Vector2 handle = player.View.WorldToScreenPoint(tool.transform.Find("Handle").position);
        yield return Pointer(handle, true);
        Assert.That(tool.IsSelected, Is.False);
        yield return Pointer(handle, false);
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.True);
        yield return Press(Key.Digit1);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(interaction.BrushSelected, Is.True);
        yield return Pointer(Center, false);
        yield return Press(Key.Digit1);
        float progress = screw.Tightness;
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = Vector3.Lerp(player.View.transform.position, screw.transform.position, .5f);
        blocker.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(screw.Tightness, Is.EqualTo(progress));
        Object.Destroy(blocker);
    }

    [UnityTest]
    public IEnumerator FocusLossCancelsGestureAndStopsSound_WithoutResettingScrew()
    {
        yield return Press(Key.Digit2);
        yield return Pointer(Center, true);
        yield return Pointer(Center + Vector2.right * 60f, true);
        yield return Pointer(Center + new Vector2(52f, 30f), true);
        Assert.That(screw.Tightness, Is.LessThan(1f));
        float progress = screw.Tightness;
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(tool.GetComponent<AudioSource>().isPlaying, Is.False);
        Assert.That(screw.Tightness, Is.EqualTo(progress));
    }

    [UnityTest]
    public IEnumerator OccludedGhostCannotConsumeTrayScrew_AndClearGhostCanBeClicked()
    {
        var robot = screw.GetComponentInParent<RobotRepair>();
        robot.Turn(screw, -10000f);
        yield return Press(Key.Digit2);
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = Vector3.Lerp(player.View.transform.position, screw.transform.position, .5f);
        blocker.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        yield return Pointer(Center, true);
        Assert.That(screw.IsRemoved, Is.True);
        Assert.That(robot.ScrewTray.Count, Is.EqualTo(1));
        Assert.That(tool.IsTurning, Is.False);
        yield return Pointer(Center, false);
        Object.Destroy(blocker);
        yield return null;
        Physics.SyncTransforms();
        yield return Pointer(Center, true);
        Assert.That(screw.IsRemoved, Is.False);
        Assert.That(screw.IsTight, Is.False);
        Assert.That(robot.ScrewTray.Count, Is.Zero);
        Assert.That(tool.IsTurning, Is.True);
    }

    [UnityTest]
    public IEnumerator FallingScrewRemainsAvailable_AndImmediateInsertionDoesNotDuplicateIt()
    {
        var robot = screw.GetComponentInParent<RobotRepair>();
        var tray = robot.ScrewTray;
        robot.Turn(screw, -10000f);
        Assert.That(tray.Count, Is.EqualTo(1), "Collection is credited before the fall finishes.");
        Assert.That(tray.GetComponentsInChildren<Rigidbody>().Count(body => !body.isKinematic), Is.EqualTo(1));
        Assert.That(robot.TryInsertScrew(screw, out string reason), Is.True, reason);
        Assert.That(tray.Count, Is.Zero);
        Assert.That(robot.TryInsertScrew(screw, out _), Is.False);
        yield return new WaitForSeconds(.5f);
        Assert.That(tray.Count, Is.Zero);
        Assert.That(tray.GetComponentsInChildren<Rigidbody>().Count(body => !body.isKinematic), Is.Zero);
        robot.Turn(screw, -10000f);
        Assert.That(tray.Count, Is.EqualTo(1));
        var body = tray.GetComponentsInChildren<Rigidbody>().Single(candidate => !candidate.isKinematic);
        body.position = tray.transform.position + Vector3.down * 10f;
        yield return new WaitForSeconds(.5f);
        Assert.That(tray.Count, Is.EqualTo(1), "Physical movement must not lose the available fastener.");
        Assert.That(body.position.y, Is.GreaterThan(tray.transform.position.y - 1f));
        Assert.That(robot.TryInsertScrew(screw, out reason), Is.True, reason);
        Assert.That(tray.Count, Is.Zero);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("ScrewTestCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
