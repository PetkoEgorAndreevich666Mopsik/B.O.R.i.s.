using System.Collections;
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
        player.transform.position = new Vector3(0, 0, -.7f);
        body.enabled = true;
        yield return null;
        yield return Press(Key.E);
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
    }

    Vector2 Center => player.View.WorldToScreenPoint(screw.transform.position);

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator MouseGestureSelectsTool_ReleasesScrew_AndReverses()
    {
        yield return Press(Key.Digit2);
        Assert.That(interaction.ScrewdriverSelected, Is.True);
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.True, "Actual pointer press must hit the screw.");
        yield return Pointer(Center + Vector2.right * 60f, true);
        for (int i = 1; i <= 52; i++)
        {
            float angle = i * 15f * Mathf.Deg2Rad;
            yield return Pointer(Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 60f, true);
        }
        Assert.That(screw.Tightness, Is.Zero.Within(.001f));
        Assert.That(tool.transform.position, Is.EqualTo(screw.transform.position));
        yield return Pointer(Center, false);
        Assert.That(tool.IsTurning, Is.False);
        yield return Pointer(Center, true);
        yield return Pointer(Center + Vector2.right * 60f, true);
        for (int i = 1; i <= 52; i++)
        {
            float angle = -i * 15f * Mathf.Deg2Rad;
            yield return Pointer(Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 60f, true);
        }
        Assert.That(screw.Tightness, Is.EqualTo(1f).Within(.001f));
    }

    [UnityTest]
    public IEnumerator PartialTurnSurvivesReleaseAndWorkbenchVisits()
    {
        yield return Press(Key.Digit2);
        yield return Pointer(Center, true);
        yield return Pointer(Center + Vector2.right * 60f, true);
        for (int i = 1; i <= 12; i++)
        {
            float angle = i * 15f * Mathf.Deg2Rad;
            yield return Pointer(Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 60f, true);
        }
        Assert.That(screw.Tightness, Is.EqualTo(.75f).Within(.005f));
        float progress = screw.Tightness;
        Vector3 position = screw.transform.localPosition;
        yield return Pointer(Center, false);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(screw.Tightness, Is.EqualTo(progress));
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.True);
        yield return Press(Key.Escape);
        Assert.That(tool.IsTurning, Is.False);
        yield return Pointer(Vector2.zero, false);
        yield return Press(Key.E);
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
    public IEnumerator HandSelectionCancelsTool_AndOccluderBlocksScrew()
    {
        Vector2 handle = player.View.WorldToScreenPoint(tool.transform.Find("Handle").position);
        yield return Pointer(handle, true);
        Assert.That(interaction.ScrewdriverSelected, Is.True, "Tool on the table must be clickable.");
        yield return Pointer(handle, false);
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.True);
        yield return Press(Key.Digit1);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(interaction.ScrewdriverSelected, Is.False);
        yield return Pointer(Center, false);
        yield return Press(Key.Digit2);
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = Vector3.Lerp(player.View.transform.position, screw.transform.position, .5f);
        blocker.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        yield return Pointer(Center, true);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(screw.Tightness, Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator FocusLossCancelsGestureAndStopsSound_WithoutResettingScrew()
    {
        yield return Press(Key.Digit2);
        yield return Pointer(Center, true);
        yield return Pointer(Center + Vector2.right * 60f, true);
        yield return Pointer(Center + new Vector2(52f, 30f), true);
        Assert.That(screw.Tightness, Is.LessThan(1f));
        Assert.That(tool.GetComponent<AudioSource>().isPlaying, Is.True);
        float progress = screw.Tightness;
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(tool.IsTurning, Is.False);
        Assert.That(tool.GetComponent<AudioSource>().isPlaying, Is.False);
        Assert.That(screw.Tightness, Is.EqualTo(progress));
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
