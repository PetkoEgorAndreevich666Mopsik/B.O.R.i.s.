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
    Vector2 Center => new(Screen.width * .5f, Screen.height * .5f);

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
        yield return null;
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
        Assert.That(workbench.FocusedPart, Is.SameAs(robot.Cover));
    }

    static void AssertPose(Transform target, Vector3 position, Quaternion rotation)
    {
        Assert.That(Vector3.Distance(target.position, position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(target.rotation, rotation), Is.LessThan(.01f));
    }

    [UnityTest]
    public IEnumerator RobotRotatesWithoutMovingTraysOrSpareAndRestoresAnchors()
    {
        Vector3 overview = player.View.transform.position;
        Quaternion overviewRotation = player.View.transform.rotation;
        Vector3 bodyPosition = robot.InspectionPivot.position;
        Quaternion bodyRotation = robot.InspectionPivot.rotation;
        Vector3 anchorPosition = robot.Cover.InstallationAnchor.position;
        Quaternion anchorRotation = robot.Cover.InstallationAnchor.rotation;
        var spare = robot.GetComponentsInChildren<RepairPart>(true).Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
        spare.gameObject.SetActive(true);
        Vector3 sparePosition = spare.transform.position;
        Quaternion spareRotation = spare.transform.rotation;
        Vector3 trayPosition = robot.Cover.TrayAnchor.position;
        Quaternion trayRotation = robot.Cover.TrayAnchor.rotation;
        var quest = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(i => i.name == "RepairQuest");
        float fullHeight = quest.rectTransform.rect.height;
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        Assert.That(interaction.IsInspecting, Is.True);
        Assert.That(workbench.FocusedPart, Is.Null);
        Assert.That(quest.rectTransform.rect.height, Is.LessThan(fullHeight));
        Vector3 cameraPosition = player.View.transform.position;
        Vector3 playerPosition = player.transform.position;
        yield return Pointer(Center, delta: new Vector2(100, -50));
        Assert.That(Quaternion.Angle(bodyRotation, robot.InspectionPivot.rotation), Is.LessThan(.01f));
        yield return Pointer(Center, 2, new Vector2(500, 1000));
        yield return Pointer(Center);
        Assert.That(Quaternion.Angle(bodyRotation, robot.InspectionPivot.rotation), Is.GreaterThan(90f));
        AssertPose(player.View.transform, cameraPosition, overviewRotation);
        Assert.That(Vector3.Distance(playerPosition, player.transform.position), Is.LessThan(.0001f));
        AssertPose(spare.transform, sparePosition, spareRotation);
        AssertPose(robot.Cover.TrayAnchor, trayPosition, trayRotation);
        Assert.That(robot.Cover.IsInstalled, Is.True);
        Assert.That(robot.Cover.Screws.All(s => s.IsTight), Is.True);
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        AssertPose(robot.InspectionPivot, bodyPosition, bodyRotation);
        AssertPose(robot.Cover.InstallationAnchor, anchorPosition, anchorRotation);
        AssertPose(robot.Cover.transform, anchorPosition, anchorRotation);
        AssertPose(player.View.transform, overview, overviewRotation);
        Assert.That(quest.rectTransform.rect.height, Is.EqualTo(fullHeight));
    }

    [UnityTest]
    public IEnumerator DetachedCoverTurnsOverAndZoomsWithFixedCameraThenRestoresPose()
    {
        Vector3 cameraPosition = player.View.transform.position;
        Quaternion cameraRotation = player.View.transform.rotation;
        yield return FocusCover();
        var cover = robot.Cover;
        Transform parent = cover.transform.parent;
        Quaternion initial = cover.transform.rotation;
        Vector3 scale = cover.transform.localScale;
        Assert.That(Vector3.Distance(cover.transform.position, cover.TrayAnchor.position), Is.GreaterThan(.2f));
        Assert.That(cover.GetComponentInParent<RobotRepair>(), Is.SameAs(robot));
        Assert.That(cover.GetComponentsInChildren<Collider>().All(c => !c.enabled), Is.True);
        yield return Pointer(Center, 2, new Vector2(0, 720));
        yield return Pointer(Center);
        Assert.That(Vector3.Dot(initial * Vector3.forward, cover.transform.forward), Is.LessThan(-.98f), "Reverse side is reachable.");
        float distance = Vector3.Distance(cameraPosition, cover.transform.position);
        yield return Pointer(Center, wheel: 120);
        Assert.That(Vector3.Distance(cameraPosition, cover.transform.position), Is.LessThan(distance - .03f));
        AssertPose(player.View.transform, cameraPosition, cameraRotation);
        Assert.That(cover.transform.localScale, Is.EqualTo(scale));
        yield return Press(Key.F);
        Assert.That(cover.transform.parent, Is.SameAs(parent));
        AssertPose(cover.transform, cover.TrayAnchor.position, cover.TrayAnchor.rotation);
        Assert.That(cover.IsInstalled, Is.False);
        Assert.That(cover.Screws.All(s => s.IsLoose), Is.True);
        Assert.That(cover.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
    }

    [UnityTest]
    public IEnumerator BothDetachedDrivesCanBeInspectedAndStillInstalledAfterwards()
    {
        yield return FocusCover();
        yield return Press(Key.F);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        var oldDrive = robot.InstalledDrive;
        foreach (var screw in oldDrive.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(oldDrive, false, out _), Is.True);
        foreach (var drive in robot.GetComponentsInChildren<RepairPart>().Where(p => p.Kind == RepairPartKind.Drive))
        {
            Physics.SyncTransforms();
            yield return Pointer(player.View.WorldToScreenPoint(drive.transform.position));
            yield return Press(Key.F);
            Assert.That(workbench.FocusedPart, Is.SameAs(drive));
            yield return Pointer(Center, 2, new Vector2(250, 120));
            yield return Pointer(Center);
            yield return Press(Key.F);
            AssertPose(drive.transform, drive.TrayAnchor.position, drive.TrayAnchor.rotation);
            Assert.That(robot.TryPlace(drive, true, out string reason), Is.True, reason);
            Assert.That(robot.TryPlace(drive, false, out reason), Is.True, reason);
        }
    }

    [UnityTest]
    public IEnumerator ReleasedCoverStaysInstalledAndDetachedCoverStaysOnTrayDuringRobotInspection()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Vector3 released = robot.Cover.transform.position;
        yield return Pointer(player.View.WorldToScreenPoint(released));
        yield return Press(Key.F);
        Assert.That(workbench.FocusedPart, Is.Null);
        Assert.That(robot.Cover.IsInstalled && robot.Cover.IsReleased, Is.True);
        yield return Press(Key.F);
        Assert.That(robot.Cover.transform.position, Is.EqualTo(released));
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Physics.SyncTransforms();
        Vector3 tray = robot.Cover.transform.position;
        yield return Pointer(player.View.WorldToScreenPoint(robot.InstalledDrive.transform.position));
        yield return Press(Key.F);
        Assert.That(interaction.IsInspecting, Is.True);
        Assert.That(workbench.FocusedPart, Is.Null);
        yield return Pointer(Center, 2, new Vector2(400, 0));
        Assert.That(robot.Cover.transform.position, Is.EqualTo(tray));
        yield return Pointer(Center);
        yield return Press(Key.F);
    }

    [UnityTest]
    public IEnumerator AlignKeyAndActualUIButtonRestorePresentation()
    {
        yield return FocusCover();
        Vector3 position = robot.Cover.transform.position;
        Quaternion rotation = robot.Cover.transform.rotation;
        yield return Pointer(Center, 2, new Vector2(100, 200), 120);
        yield return Pointer(Center);
        yield return Press(Key.R);
        AssertPose(robot.Cover.transform, position, rotation);
        yield return Pointer(Center, 2, new Vector2(-150, 100));
        yield return Pointer(Center);
        var align = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "AlignInspectionButton");
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, align.transform.position);
        yield return Pointer(screen, 1);
        yield return Pointer(screen);
        Assert.That(interaction.IsInspecting, Is.True);
        AssertPose(robot.Cover.transform, position, rotation);
        yield return Press(Key.Escape);
        Assert.That(align.gameObject.activeSelf, Is.False);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        yield return Press(Key.Escape);
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(player.View.transform.parent, Is.SameAs(player.transform));
    }

    [UnityTest]
    public IEnumerator InspectionBlocksRepairAndToolShortcutReturnsPartBeforeCleaning()
    {
        yield return FocusCover();
        var inner = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p => p.RequiresOpenCover);
        yield return Pointer(Center, 1);
        yield return Pointer(Center + Vector2.right * 80, 1);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(interaction.Brush.IsActive, Is.False);
        Assert.That(inner.Cleanliness, Is.Zero);
        Assert.That(robot.Cover.Screws.All(s => s.IsLoose), Is.True);
        yield return Pointer(Center);
        yield return Press(Key.Digit3);
        Assert.That(interaction.IsInspecting, Is.False);
        AssertPose(robot.Cover.transform, robot.Cover.TrayAnchor.position, robot.Cover.TrayAnchor.rotation);
        for (int y = 0; y < 8; y++)
        {
            float v = .06f + y * .125f;
            yield return Pointer(ScreenUV(inner, new Vector2(.04f, v)), 1);
            Assert.That(interaction.Brush.Target, Is.SameAs(inner));
            for (int x = 1; x <= 8; x++)
                yield return Pointer(ScreenUV(inner, new Vector2(.04f + x * .115f, v)), 1);
            yield return Pointer(ScreenUV(inner, new Vector2(.96f, v)));
        }
        Assert.That(inner.IsClean, Is.True, "Cleaning remains reachable in the working pose.");
    }

    [UnityTest]
    public IEnumerator CapturedScrewIgnoresFocusAndUsesWheelForRepair()
    {
        var screw = robot.Cover.Screws[0];
        yield return Press(Key.Digit2);
        Vector2 pointer = player.View.WorldToScreenPoint(screw.transform.position);
        yield return Pointer(pointer, 1);
        Assert.That(interaction.Screwdriver.IsTurning, Is.True);
        Vector3 position = player.View.transform.position;
        Quaternion rotation = player.View.transform.rotation;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
        yield return Pointer(pointer, 3, new Vector2(200, -200), -120);
        Assert.That(workbench.IsFocused, Is.False);
        Assert.That(screw.Tightness, Is.LessThan(1f));
        AssertPose(player.View.transform, position, rotation);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return Pointer(pointer);
    }

    [UnityTest]
    public IEnumerator HeldPartCannotStartInspectionAndToolChangeCancelsDrag()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Physics.SyncTransforms();
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position), 1);
        Assert.That(interaction.HeldPart, Is.SameAs(robot.Cover));
        yield return Press(Key.F);
        Assert.That(interaction.IsInspecting, Is.False);
        Assert.That(interaction.HeldPart, Is.SameAs(robot.Cover));
        yield return Press(Key.Digit2);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(robot.Cover.IsInstalled, Is.True);
        Assert.That(robot.Cover.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
        yield return Pointer(Center);
    }

    [UnityTest]
    public IEnumerator FocusLossAndResetRestorePoseAndAllowTheNextRobot()
    {
        yield return FocusCover();
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(interaction.IsInspecting, Is.False);
        AssertPose(robot.Cover.transform, robot.Cover.TrayAnchor.position, robot.Cover.TrayAnchor.rotation);
        Assert.That(robot.Cover.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
        interaction.SendMessage("OnApplicationFocus", true);
        Physics.SyncTransforms();
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        Assert.That(interaction.IsInspecting, Is.True);
        interaction.ResetRepair();
        yield return null;
        Assert.That(workbench.IsFocused, Is.False);
        Assert.That(workbench.Robot, Is.Not.SameAs(robot));
        robot = workbench.Robot;
        yield return FocusCover();
        workbench.enabled = false;
        yield return null; yield return null;
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(player.View.transform.parent, Is.SameAs(player.transform));
        AssertPose(robot.Cover.transform, robot.Cover.TrayAnchor.position, robot.Cover.TrayAnchor.rotation);
        Assert.That(robot.Cover.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
    }

    [UnityTest]
    public IEnumerator UICannotStartOrRotateInspectionAndCameraAvoidsSolids()
    {
        var overlay = new GameObject("InspectionBlockingUI", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        Assert.That(workbench.IsFocused, Is.False);
        overlay.SetActive(false);
        yield return FocusCover();
        overlay.SetActive(true);
        Quaternion rotation = robot.Cover.transform.rotation;
        yield return Pointer(Center, 2, new Vector2(1000, 1000), 120);
        Assert.That(Quaternion.Angle(rotation, robot.Cover.transform.rotation), Is.LessThan(.01f));
        Assert.That(Physics.CheckSphere(player.View.transform.position, .06f, ~(1 << player.gameObject.layer),
            QueryTriggerInteraction.Ignore), Is.False);
        Object.Destroy(overlay);
        yield return Pointer(Center);
        yield return Press(Key.F);
    }

    [UnityTest]
    public IEnumerator DisabledColliderStaysDisabledAndRobotTestBlocksInspection()
    {
        yield return FocusCover();
        yield return Press(Key.F);
        var colliders = robot.Cover.GetComponentsInChildren<Collider>();
        colliders[0].enabled = false;
        workbench.Focus(robot.Cover);
        Assert.That(workbench.IsFocused, Is.True);
        workbench.ReturnToOverview();
        Assert.That(colliders[0].enabled, Is.False);
        Assert.That(colliders.Skip(1).All(c => c.enabled), Is.True);
        interaction.ResetRepair();
        robot = workbench.Robot;
        Assert.That(robot.RunTest(out _), Is.True);
        workbench.Focus(robot.Cover);
        Assert.That(workbench.IsFocused, Is.False);
        yield return Press(Key.F);
        Assert.That(workbench.IsFocused, Is.False);
        robot.CancelTest();
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
