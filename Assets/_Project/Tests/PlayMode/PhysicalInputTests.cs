using System.Collections;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class PhysicalInputTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    Workbench bench;
    RobotRepair robot;
    RepairPart spare;
    GameObject wall;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindAnyObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        bench = Object.FindAnyObjectByType<Workbench>();
        robot = bench.Robot;
        spare = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = bench.transform.TransformPoint(new Vector3(0f, 0f, -2.2f));
        controller.enabled = true;
        yield return null;
        yield return WorkshopTestInput.Interact();
        Assert.That(interaction.IsAtWorkbench, Is.True);
    }

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;

    }

    Vector2 Screen(Vector3 position) => player.View.WorldToScreenPoint(position);

    IEnumerator Pointer(Vector2 position, ushort buttons, Vector2 delta = default)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = buttons, delta = delta });
        yield return null; yield return null;
    }

    IEnumerator Click(Vector2 position)
    {
        yield return Pointer(position, 0);
        yield return Pointer(position, 1);
        yield return Pointer(position, 0);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator ClickSelectsPartAndReleaseKeepsItForInspection()
    {
        yield return Click(Screen(spare.transform.position));
        Assert.That(interaction.HeldPart, Is.SameAs(spare));
        Assert.That(spare.IsDragging, Is.False);
        Assert.That(spare.Body.isKinematic, Is.True);
        yield return new WaitForSeconds(.7f);
        Vector3 position = spare.transform.position;
        yield return Pointer(Vector2.one * 10f, 0);
        yield return new WaitForSeconds(.2f);
        Assert.That(Vector3.Distance(position, spare.transform.position), Is.LessThan(.02f));
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator ClickRemovesUnlockedCoverAndPutsItOnTray()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        yield return Click(Screen(robot.Cover.transform.position));
        yield return new WaitForSeconds(.6f);
        Assert.That(robot.Cover.IsInstalled, Is.False);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(Vector3.Distance(robot.Cover.transform.position, robot.Cover.TrayAnchor.position), Is.LessThan(.2f));
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator SelectedPartRotatesFullyWithoutMovingCamera()
    {
        yield return Click(Screen(spare.transform.position));
        yield return new WaitForSeconds(.7f);
        Quaternion rotation = spare.transform.rotation;
        Vector3 camera = player.View.transform.position;
        for (int i = 0; i < 20; i++) bench.UpdateView(new Vector2(18f / .35f, 0f), 0f);
        Assert.That(Quaternion.Angle(rotation, spare.transform.rotation), Is.LessThan(.05f));
        Assert.That(Vector3.Distance(camera, player.View.transform.position), Is.LessThan(.001f));
        yield return Press(Key.Escape);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(interaction.IsAtWorkbench, Is.True);
    }

    IEnumerator DragTo(RepairPart part, Vector2 target)
    {
        yield return new WaitForSeconds(.7f);
        Vector2 source = Screen(part.Body.worldCenterOfMass);
        yield return Pointer(source, 0);
        yield return Pointer(source, 1);
        yield return Pointer(source + Vector2.right * 36f, 1);
        yield return Pointer(target, 1);
        Assert.That(interaction.IsPlacingPart, Is.True);
    }

    IEnumerator EmptyDriveSocket()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        robot.Cover.RecoverToTable();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        RepairPart original = robot.InstalledDrive;
        foreach (var screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(original, false, out _), Is.True);
        original.RecoverToTable();
        yield return new WaitForSeconds(.5f);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator DragLoadsDriveOnStandAndOnlyReleaseCommitsIt()
    {
        Vector3 position = spare.transform.position;
        yield return Click(Screen(position));
        Vector2 target = Screen(bench.DriveStand.Socket.position);
        yield return DragTo(spare, target);
        Assert.That(interaction.DropAnchor, Is.SameAs(bench.DriveStand.Socket));
        Assert.That(bench.DriveStand.Part, Is.Null);
        Assert.That(spare.IsInstalled, Is.False);
        yield return Pointer(target, 0);
        Assert.That(bench.DriveStand.Part, Is.SameAs(spare), interaction.Feedback);
        Assert.That(interaction.HeldPart, Is.Null);
        bench.DriveStand.Cancel();
        Assert.That(Vector3.Distance(position, spare.transform.position), Is.LessThan(.05f));
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator SelectedDriveCannotBypassBlockedInstallation()
    {
        yield return Click(Screen(spare.transform.position));
        Vector2 target = Screen(spare.InstallationAnchor.position);
        yield return DragTo(spare, target);
        Assert.That(interaction.DropAnchor, Is.Null);
        yield return Pointer(target, 0);
        Assert.That(spare.IsInstalled, Is.False);
        Assert.That(robot.Cover.IsInstalled, Is.True);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator RepairRegressionClickOverSocketOnlyPutsPartDown()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        robot.Cover.RecoverToTable();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        RepairPart original = robot.InstalledDrive;
        foreach (var screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(original, false, out _), Is.True);
        original.RecoverToTable();
        yield return new WaitForSeconds(.5f);
        yield return Click(Screen(spare.transform.position));
        Assert.That(interaction.HeldPart, Is.SameAs(spare));
        yield return Click(Screen(spare.InstallationAnchor.position));
        Assert.That(spare.IsInstalled, Is.False, "A click is not an installation gesture, even over a valid socket.");
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator DragInstallsReplacementOnlyAfterReleaseInsideSocket()
    {
        yield return EmptyDriveSocket();
        yield return Click(Screen(spare.transform.position));
        Vector2 target = Screen(spare.InstallationAnchor.position);
        yield return DragTo(spare, target);
        Assert.That(interaction.DropAnchor, Is.SameAs(spare.InstallationAnchor));
        Assert.That(spare.IsInstalled, Is.False, "Hovering inside the socket is a preview, not installation.");
        var zone = Object.FindObjectsByType<RepairHintView>(FindObjectsSortMode.None)
            .Single(z => z.name == "PlacementZonePreview" && z.Target == spare.InstallationAnchor);
        Assert.That(zone.Tint.g, Is.GreaterThan(zone.Tint.r));
        Assert.That(zone.raycastTarget, Is.False);
        yield return Pointer(target, 0);
        Assert.That(spare.IsInstalled, Is.True, interaction.Feedback);
        Assert.That(interaction.HeldPart, Is.Null);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator DragFromTableIntoZoneDoesNotRequireSeparateSelectionClick()
    {
        yield return EmptyDriveSocket();
        Vector2 source = Screen(spare.transform.position);
        Vector2 target = Screen(spare.InstallationAnchor.position);
        yield return Pointer(source, 0);
        yield return Pointer(source, 1);
        yield return Pointer(source + Vector2.right * 36f, 1);
        yield return Pointer(target, 1);
        Assert.That(interaction.IsPlacingPart, Is.True);
        Assert.That(spare.IsInstalled, Is.False);
        Assert.That(interaction.DropAnchor, Is.SameAs(spare.InstallationAnchor));
        yield return Pointer(target, 0);
        Assert.That(spare.IsInstalled, Is.True);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator LeavingSocketBeforeReleasePutsPartOnTableInsteadOfInstalling()
    {
        yield return EmptyDriveSocket();
        yield return Click(Screen(spare.transform.position));
        Vector2 socket = Screen(spare.InstallationAnchor.position);
        yield return DragTo(spare, socket);
        Assert.That(interaction.DropAnchor, Is.SameAs(spare.InstallationAnchor));
        var table = bench.transform.Find("TableTop").GetComponent<Collider>().bounds;
        Vector3 target = new(table.min.x + .35f, table.max.y + .015f, table.center.z);
        yield return Pointer(Screen(target), 1);
        Assert.That(interaction.DropAnchor, Is.Null);
        yield return Pointer(Screen(target), 0);
        Assert.That(spare.IsInstalled, Is.False);
        Assert.That(spare.IsSupported, Is.True);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(spare.transform.position.x, Is.EqualTo(target.x).Within(.04f));
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator NearSocketOutsideItsActualColliderCannotInstall()
    {
        yield return EmptyDriveSocket();
        yield return Click(Screen(spare.transform.position));
        var box = spare.InstallationAnchor.GetComponent<BoxCollider>();
        Vector3 point = box.transform.TransformPoint(box.center + Vector3.right * (box.size.x * .5f + .06f));
        Vector2 target = Screen(point);
        yield return DragTo(spare, target);
        Assert.That(interaction.DropAnchor, Is.Not.SameAs(spare.InstallationAnchor));
        yield return Pointer(target, 0);
        Assert.That(spare.IsInstalled, Is.False);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator EscapeDuringPlacementReturnsPartAndClearsZoneMarkers()
    {
        Vector3 position = spare.transform.position;
        yield return Click(Screen(position));
        yield return DragTo(spare, Screen(bench.DriveStand.Socket.position));
        yield return Press(Key.Escape);
        Assert.That(interaction.IsPlacingPart, Is.False);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(Vector3.Distance(position, spare.transform.position), Is.LessThan(.05f));
        Assert.That(Object.FindObjectsByType<RepairHintView>(FindObjectsSortMode.None).Any(z => z.name == "PlacementZonePreview"), Is.False);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator TableClickPlacesSelectedPartWithoutDroppingOrHoldingMouse()
    {
        yield return Click(Screen(spare.transform.position));
        var table = bench.transform.Find("TableTop").GetComponent<Collider>().bounds;
        Vector3 target = new(table.min.x + .35f, table.max.y + .015f, table.center.z);
        yield return Click(Screen(target));
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(spare.IsSupported, Is.True);
        Assert.That(spare.transform.position.x, Is.EqualTo(target.x).Within(.04f));
        Assert.That(spare.transform.position.z, Is.EqualTo(target.z).Within(.04f));
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator ActionFeedbackConfirmsWithoutBlockingMouseAndThenDisappears()
    {
        yield return Pointer(new Vector2(8f, 8f), 0);
        float deadline = Time.realtimeSinceStartup + 3f;
        while (interaction.IsViewMoving && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(interaction.IsViewMoving, Is.False);
        var feedback = player.GetComponent<RepairFeedback>();
        Assert.That(feedback, Is.Not.Null);
        var ring = Object.FindAnyObjectByType<InteractionRing>();
        var group = ring.GetComponent<CanvasGroup>();
        deadline = Time.realtimeSinceStartup + 2f;
        while (group.alpha < .9f && Time.realtimeSinceStartup < deadline)
        {
            feedback.Pulse(robot.Cover.transform, true);
            yield return null;
        }
        Assert.That(group.blocksRaycasts || ring.raycastTarget, Is.False);
        Assert.That(group.alpha, Is.GreaterThan(.9f));
        Assert.That(ring.Progress, Is.EqualTo(1f));
        Assert.That(ring.GetComponent<CanvasRenderer>(), Is.Not.Null);
        Assert.That(ring.enabled, Is.False, "Completion uses a check mark without the progress circle.");
        yield return new WaitForSeconds(.7f);
        Assert.That(group.alpha, Is.Zero);
    }

    [UnityTest, Category("WorkbenchRepair")]
    public IEnumerator RecoveryEndsBrushAndRestoresCameraInspection()
    {
        yield return Press(Key.Digit1);
        DirtPatch patch = robot.GetComponentsInChildren<DirtPatch>().Single(p => p.name == "OuterDirt");
        Vector2 point = Screen(patch.transform.TransformPoint(new Vector3(-.3f, 0f, 0f)));
        yield return Pointer(point, 1);
        Assert.That(interaction.Brush.IsActive, Is.True);
        yield return Press(Key.G);
        Assert.That(interaction.Brush.IsActive, Is.False);
        Assert.That(interaction.IsInspecting, Is.True, "Recovering parts must not leave the camera outside inspection mode.");
        Assert.That(bench.IsFocused, Is.True);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (interaction != null) interaction.ExitWorkbench();
        if (wall != null) Object.Destroy(wall);
        yield return null;
        fixture.TearDown();
    }
}
