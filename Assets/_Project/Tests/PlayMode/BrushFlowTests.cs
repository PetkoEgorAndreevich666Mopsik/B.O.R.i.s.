using System.Collections;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class BrushFlowTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    RobotRepair robot;
    BrushTool brush;
    DirtPatch patch;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        robot = Object.FindFirstObjectByType<RobotRepair>();
        brush = Object.FindFirstObjectByType<BrushTool>();
        patch = robot.GetComponentsInChildren<DirtPatch>().Single(p => !p.RequiresOpenCover);
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
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
    }

    Vector2 ScreenUV(DirtPatch target, Vector2 uv) => player.View.WorldToScreenPoint(
        target.transform.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0f)));

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null; yield return null;
    }

    [UnityTest]
    public IEnumerator RealMouseSelectsBrushAndLocallyCleans_StationaryHoldDoesNothing()
    {
        Assert.That(robot.GetComponentsInChildren<DirtPatch>().Length, Is.EqualTo(3));
        yield return Press(Key.Digit3);
        Assert.That(interaction.BrushSelected, Is.True);
        yield return Pointer(ScreenUV(patch, new Vector2(.2f, .5f)), true);
        Assert.That(brush.Target, Is.SameAs(patch));
        float initial = patch.Cleanliness;
        yield return new WaitForSeconds(.15f);
        Assert.That(patch.Cleanliness, Is.EqualTo(initial));
        for (int i = 1; i <= 12; i++)
            yield return Pointer(ScreenUV(patch, new Vector2(.2f + i * .05f, .5f)), true);
        Assert.That(patch.Cleanliness, Is.GreaterThan(.1f).And.LessThan(.9f));
        Assert.That(patch.Mask.GetPixel(32, 32).a, Is.Zero);
        Assert.That(patch.Mask.GetPixel(32, 52).a, Is.EqualTo(1f), "Unvisited dirt must remain visible.");
        float cleaned = patch.Cleanliness;
        yield return new WaitForSeconds(.15f);
        Assert.That(patch.Cleanliness, Is.EqualTo(cleaned));
        Assert.That(brush.GetComponent<AudioSource>().isPlaying, Is.False);
        yield return Pointer(ScreenUV(patch, new Vector2(.8f, .5f)), false);
        Assert.That(brush.IsActive, Is.False);
    }

    [UnityTest]
    public IEnumerator MasksAreIndependentAndStrokeSamplingDoesNotChangeCleanedArea()
    {
        var copy = Object.Instantiate(robot.gameObject);
        var other = copy.GetComponentsInChildren<DirtPatch>().Single(p => !p.RequiresOpenCover);
        Assert.That(other.Mask, Is.Not.SameAs(patch.Mask));
        Assert.That(patch.CleanStroke(new Vector2(.2f, .5f), new Vector2(.8f, .5f), .035f), Is.GreaterThan(0));
        float cleaned = patch.Cleanliness;
        Assert.That(other.Cleanliness, Is.Zero);
        for (int i = 0; i < 24; i++)
            other.CleanStroke(new Vector2(.2f + i * .025f, .5f), new Vector2(.2f + (i + 1) * .025f, .5f), .035f);
        Assert.That(other.Cleanliness, Is.EqualTo(cleaned));
        Assert.That(patch.CleanStroke(new Vector2(.2f, .5f), new Vector2(.8f, .5f), .035f), Is.Zero);
        Assert.That(patch.CleanStroke(Vector2.one * .5f, Vector2.one * .5f, .1f), Is.Zero);
        Assert.That(patch.CleanStroke(Vector2.zero, new Vector2(float.NaN, .5f), .1f), Is.Zero);
        Object.Destroy(copy);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DustEmitsOnlyWhileStrokeRemovesDirt()
    {
        Transform dustRoot = brush.transform.Find("DustCleaning");
        Assert.That(dustRoot, Is.Not.Null);
        Assert.That(dustRoot.GetComponentInChildren<ParticleSystem>(true), Is.Not.Null);
        Assert.That(brush.IsDustEmitting, Is.False);

        yield return Press(Key.Digit3);
        brush.Begin(patch, player.View, ScreenUV(patch, new Vector2(.2f, .5f)));
        Assert.That(brush.IsDustEmitting, Is.False, "Initial contact does not remove dirt.");

        Vector2 strokeEnd = ScreenUV(patch, new Vector2(.35f, .5f));
        brush.Drag(strokeEnd);
        Assert.That(brush.IsDustEmitting, Is.True);

        brush.Drag(strokeEnd);
        Assert.That(brush.IsDustEmitting, Is.True, "Brief empty samples must not make the VFX flicker.");
        yield return new WaitForSeconds(brush.DustStopDelay + .02f);
        brush.Drag(strokeEnd);
        Assert.That(brush.IsDustEmitting, Is.False, "Emission stops after the configurable grace period.");
        brush.End();
        Assert.That(brush.IsDustEmitting, Is.False);
    }

    [UnityTest]
    public IEnumerator LeavingSurfaceBreaksStrokeAndOccluderPreventsCleaning()
    {
        yield return Press(Key.Digit3);
        yield return Pointer(ScreenUV(patch, new Vector2(.2f, .5f)), true);
        yield return Pointer(ScreenUV(patch, new Vector2(-.5f, .5f)), true);
        Assert.That(brush.HasContact, Is.False);
        yield return Pointer(ScreenUV(patch, new Vector2(.8f, .5f)), true);
        Assert.That(brush.HasContact, Is.True);
        Assert.That(patch.Cleanliness, Is.Zero, "No line may be painted across a contact break.");
        yield return Pointer(ScreenUV(patch, new Vector2(.8f, .5f)), false);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = Vector3.Lerp(player.View.transform.position, patch.transform.position, .5f);
        obstacle.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        yield return Pointer(ScreenUV(patch, new Vector2(.5f, .5f)), true);
        Assert.That(brush.IsActive, Is.False);
        Assert.That(patch.Cleanliness, Is.Zero);
        Object.Destroy(obstacle);
    }

    [UnityTest]
    public IEnumerator ToolChangeFocusLossAndExitStopBrushWithoutResettingMask()
    {
        Vector2 handle = player.View.WorldToScreenPoint(brush.transform.Find("Handle").position);
        yield return Pointer(handle, true);
        Assert.That(interaction.BrushSelected, Is.True, "Brush on the table must be clickable.");
        yield return Pointer(handle, false);
        yield return Pointer(ScreenUV(patch, new Vector2(.3f, .5f)), true);
        yield return Pointer(ScreenUV(patch, new Vector2(.4f, .5f)), true);
        brush.Drag(ScreenUV(patch, new Vector2(.5f, .5f)));
        Assert.That(brush.GetComponent<AudioSource>().isPlaying, Is.True);
        float cleaned = patch.Cleanliness;
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(brush.IsActive, Is.False);
        Assert.That(brush.GetComponent<AudioSource>().isPlaying, Is.False);
        Assert.That(patch.Cleanliness, Is.EqualTo(cleaned));
        yield return Pointer(ScreenUV(patch, new Vector2(.4f, .5f)), false);
        yield return Pointer(ScreenUV(patch, new Vector2(.4f, .5f)), true);
        yield return Press(Key.Digit2);
        Assert.That(brush.IsActive, Is.False);
        Assert.That(interaction.ScrewdriverSelected, Is.True);
        yield return Press(Key.Escape);
        yield return Pointer(Vector2.zero, false);
        yield return Press(Key.E);
        Assert.That(patch.Cleanliness, Is.EqualTo(cleaned));
    }

    [UnityTest]
    public IEnumerator InnerCoverIsCleanableInTrayAndClosedRobotBlocksInnerPatches()
    {
        DirtPatch inner = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p => p.RequiresOpenCover);
        Assert.That(robot.CanClean(inner, out _), Is.False);
        Assert.That(brush.Begin(inner, player.View, ScreenUV(inner, Vector2.one * .5f)), Is.False);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Physics.SyncTransforms();
        yield return Press(Key.Digit3);
        yield return Pointer(ScreenUV(inner, new Vector2(.2f, .5f)), true);
        Assert.That(brush.Target, Is.SameAs(inner));
        yield return Pointer(ScreenUV(inner, new Vector2(.8f, .5f)), true);
        Assert.That(inner.Cleanliness, Is.GreaterThan(0f));
        float cleaned = inner.Cleanliness;
        yield return Press(Key.Escape);
        Assert.That(brush.IsActive, Is.False);
        yield return Pointer(Vector2.zero, false);
        yield return Press(Key.E);
        Assert.That(inner.Cleanliness, Is.EqualTo(cleaned));
        var frame = robot.GetComponentsInChildren<DirtPatch>().Single(p => p.name == "FrameDirt");
        yield return Press(Key.Digit3);
        yield return Pointer(ScreenUV(frame, new Vector2(.3f, .5f)), true);
        Assert.That(brush.Target, Is.SameAs(frame), "Third patch must be physically reachable inside the robot.");
        yield return Pointer(ScreenUV(frame, new Vector2(.7f, .5f)), true);
        Assert.That(frame.Cleanliness, Is.GreaterThan(0f));
    }

    [UnityTest]
    public IEnumerator FullSurfaceCanBeCleanedAndOverlayCancelsStroke()
    {
        for (int y = 0; y < 10; y++)
            patch.CleanStroke(new Vector2(.01f, .025f + y * .1f), new Vector2(.99f, .025f + y * .1f), .035f);
        Assert.That(patch.IsClean, Is.True);
        Assert.That(patch.Cleanliness, Is.InRange(.99f, 1f));
        yield return Press(Key.Digit3);
        yield return Pointer(ScreenUV(patch, Vector2.one * .5f), true);
        var canvas = new GameObject("BlockingBrushUI", typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        overlay.transform.SetParent(canvas.transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return null; yield return null;
        Assert.That(brush.IsActive, Is.False);
        Assert.That(patch.Cleanliness, Is.InRange(.99f, 1f));
        Object.Destroy(canvas);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("BrushCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
