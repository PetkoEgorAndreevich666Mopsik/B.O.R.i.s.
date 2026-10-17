using System.Collections;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class WorkbenchInspectionTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    Workbench workbench;
    RobotRepair robot;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        workbench = Object.FindFirstObjectByType<Workbench>();
        var session = Object.FindFirstObjectByType<WorkshopSession>();
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        robot = workbench.Robot;
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

    IEnumerator Pointer(Vector2 position, ushort buttons = 0, Vector2 delta = default, float wheel = 0)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = buttons,
            delta = delta, scroll = new Vector2(0, wheel) });
        yield return null; yield return null;
    }

    Vector2 ScreenUV(DirtPatch patch, Vector2 uv) => player.View.WorldToScreenPoint(
        patch.transform.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0f)));

    IEnumerator FocusCover()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Physics.SyncTransforms();
        var inner = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p => p.RequiresOpenCover);
        yield return Pointer(ScreenUV(inner, Vector2.one * .5f));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        Assert.That(workbench.FocusedPart, Is.SameAs(robot.Cover));
    }

    [UnityTest]
    public IEnumerator FocusOrbitZoomAndReturnKeepWalkingViewAndRestoreQuest()
    {
        Vector3 overview = player.View.transform.position;
        Quaternion overviewRotation = player.View.transform.rotation;
        var quest = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(i => i.name == "RepairQuest");
        float fullHeight = quest.rectTransform.rect.height;
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        Assert.That(interaction.IsInspecting, Is.True);
        Assert.That(workbench.FocusedPart, Is.Null, "Installed parts focus the robot.");
        Assert.That(quest.rectTransform.rect.height, Is.LessThan(fullHeight));
        Vector3 playerPosition = player.transform.position;
        Quaternion before = player.View.transform.rotation;
        yield return Pointer(new Vector2(Screen.width * .5f, Screen.height * .5f), 0, new Vector2(100, -50));
        Assert.That(Quaternion.Angle(before, player.View.transform.rotation), Is.LessThan(.2f), "Mouse alone must not orbit.");
        yield return Pointer(new Vector2(Screen.width * .5f, Screen.height * .5f), 2, new Vector2(100, -50));
        yield return Pointer(new Vector2(Screen.width * .5f, Screen.height * .5f));
        yield return new WaitForSeconds(.7f);
        Assert.That(Quaternion.Angle(before, player.View.transform.rotation), Is.GreaterThan(10f));
        Assert.That(Vector3.Distance(playerPosition, player.transform.position), Is.LessThan(.001f));
        Vector3 unzoomed = player.View.transform.position;
        yield return Pointer(new Vector2(Screen.width * .5f, Screen.height * .5f), wheel: 120);
        yield return new WaitForSeconds(.7f);
        Assert.That(Vector3.Distance(unzoomed, player.View.transform.position), Is.GreaterThan(.05f));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        Assert.That(interaction.IsInspecting, Is.False);
        Assert.That(Vector3.Distance(overview, player.View.transform.position), Is.LessThan(.003f));
        Assert.That(Quaternion.Angle(overviewRotation, player.View.transform.rotation), Is.LessThan(.2f));
        Assert.That(quest.rectTransform.rect.height, Is.EqualTo(fullHeight));
        yield return Press(Key.Escape);
        Assert.That(player.View.transform.parent, Is.EqualTo(player.transform));
    }

    [UnityTest]
    public IEnumerator InnerCoverIsVisibleAndCanBeFullyBrushedInFocus()
    {
        yield return FocusCover();
        var inner = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p => p.RequiresOpenCover);
        Vector3 coverPosition = robot.Cover.transform.position;
        Quaternion coverRotation = robot.Cover.transform.rotation;
        foreach (Vector2 uv in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
        {
            Vector2 screen = ScreenUV(inner, uv);
            Assert.That(screen.x, Is.InRange(0f, Screen.width));
            Assert.That(screen.y, Is.InRange(0f, Screen.height));
        }
        yield return Press(Key.Digit3);
        for (int y = 0; y < 8; y++)
        {
            float v = .06f + y * .125f;
            yield return Pointer(ScreenUV(inner, new Vector2(.04f, v)), 1);
            Assert.That(interaction.Brush.Target, Is.SameAs(inner));
            for (int x = 1; x <= 8; x++)
                yield return Pointer(ScreenUV(inner, new Vector2(.04f + x * .115f, v)), 1);
            yield return Pointer(ScreenUV(inner, new Vector2(.96f, v)));
        }
        Assert.That(inner.IsClean, Is.True, "Normal brush strokes must reach 90% without increasing the radius.");
        Assert.That(robot.Cover.transform.position, Is.EqualTo(coverPosition));
        Assert.That(Quaternion.Angle(robot.Cover.transform.rotation, coverRotation), Is.LessThan(.001f));
        float clean = inner.Cleanliness;
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(inner.Cleanliness, Is.EqualTo(clean));
        Assert.That(interaction.IsInspecting, Is.False);
    }

    [UnityTest]
    public IEnumerator BrushGestureIgnoresFocusOrbitAndZoomWithoutArtificialCleaning()
    {
        yield return FocusCover();
        var inner = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p => p.RequiresOpenCover);
        yield return Press(Key.Digit3);
        Vector2 pointer = ScreenUV(inner, new Vector2(.3f, .5f));
        yield return Pointer(pointer, 1);
        Assert.That(interaction.Brush.IsActive, Is.True);
        Vector3 cameraPosition = player.View.transform.position;
        Quaternion cameraRotation = player.View.transform.rotation;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
        yield return Pointer(pointer, 3, new Vector2(300, 200), 120);
        yield return new WaitForSeconds(.2f);
        Assert.That(workbench.IsFocused, Is.True);
        Assert.That(Vector3.Distance(cameraPosition, player.View.transform.position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(cameraRotation, player.View.transform.rotation), Is.LessThan(.001f));
        Assert.That(inner.Cleanliness, Is.Zero);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return Pointer(pointer);
    }

    [UnityTest]
    public IEnumerator CapturedScrewUsesWheelAndLocksView_ResetClearsFocus()
    {
        ScrewFastener screw = robot.Cover.Screws[0];
        yield return Pointer(player.View.WorldToScreenPoint(screw.transform.position));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        yield return Press(Key.Digit2);
        Vector2 pointer = player.View.WorldToScreenPoint(screw.transform.position);
        yield return Pointer(pointer, 1);
        Assert.That(interaction.Screwdriver.IsTurning, Is.True);
        Vector3 cameraPosition = player.View.transform.position;
        Quaternion cameraRotation = player.View.transform.rotation;
        yield return Pointer(pointer, 3, new Vector2(200, -200), -120);
        Assert.That(screw.Tightness, Is.LessThan(1f));
        Assert.That(Vector3.Distance(cameraPosition, player.View.transform.position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(cameraRotation, player.View.transform.rotation), Is.LessThan(.001f));
        yield return Pointer(pointer);
        interaction.ResetRepair();
        yield return new WaitForSeconds(.7f);
        Assert.That(workbench.IsFocused, Is.False);
        Assert.That(workbench.FocusedPart, Is.Null);
        Assert.That(interaction.Screwdriver.IsActive, Is.False);
        Assert.That(workbench.Robot, Is.Not.SameAs(robot));
    }

    [UnityTest]
    public IEnumerator OrbitLimitsAvoidTableAndUiCannotStartFocus()
    {
        var overlay = new GameObject("InspectionBlockingUI", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        Assert.That(workbench.IsFocused, Is.False);
        Object.Destroy(overlay);
        yield return null;
        yield return FocusCover();
        Vector2 center = new(Screen.width * .5f, Screen.height * .5f);
        for (int i = 0; i < 8; i++)
            yield return Pointer(center, 2, new Vector2(1000, 1000), 120);
        yield return Pointer(center);
        yield return new WaitForSeconds(.7f);
        Assert.That(player.View.transform.eulerAngles.x, Is.InRange(9.9f, 80.1f));
        Assert.That(player.View.transform.position.y, Is.GreaterThan(robot.Cover.transform.position.y + .08f));
        Assert.That(Physics.CheckSphere(player.View.transform.position, .06f, ~(1 << player.gameObject.layer),
            QueryTriggerInteraction.Ignore), Is.False, "Camera must not enter a solid object.");
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(workbench.IsFocused, Is.False);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("InspectionCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
