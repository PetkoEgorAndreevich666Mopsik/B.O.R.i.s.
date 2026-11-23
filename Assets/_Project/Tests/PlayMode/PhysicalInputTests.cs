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

    IEnumerator Grab(RepairPart part)
    {
        yield return Pointer(Screen(part.transform.position), 1);
        Assert.That(interaction.HeldPart, Is.SameAs(part), interaction.Feedback);
    }

    [UnityTest]
    public IEnumerator HoldReleasedCoverStowsItOnTrayWithoutDragging()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        var cover = robot.Cover;
        var surfaces = cover.GetComponentsInChildren<DirtPatch>();
        yield return Pointer(Screen(cover.transform.position), 1);
        Assert.That(interaction.HeldPart, Is.SameAs(cover));
        yield return new WaitForSeconds(.85f);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(cover.IsInstalled || cover.IsDragging || cover.IsStowing, Is.False);
        Assert.That(Vector3.Distance(cover.transform.position, cover.TrayAnchor.position), Is.LessThan(.2f));
        Assert.That(cover.GetComponentsInChildren<DirtPatch>(), Is.EquivalentTo(surfaces));
        yield return Pointer(Screen(cover.transform.position), 0);
    }

    [UnityTest]
    public IEnumerator ActionFeedbackConfirmsWithoutBlockingMouseAndThenDisappears()
    {
        yield return Pointer(new Vector2(8f, 8f), 0);
        var feedback = player.GetComponent<RepairFeedback>();
        Assert.That(feedback, Is.Not.Null);
        feedback.Pulse(robot.Cover.transform, true);
        yield return new WaitForSeconds(.15f);
        var ring = Object.FindAnyObjectByType<InteractionRing>();
        var group = ring.GetComponent<CanvasGroup>();
        Assert.That(group.blocksRaycasts || ring.raycastTarget, Is.False);
        Assert.That(group.alpha, Is.GreaterThan(.9f));
        Assert.That(ring.Progress, Is.EqualTo(1f));
        Assert.That(ring.GetComponent<CanvasRenderer>(), Is.Not.Null);
        Assert.That(ring.canvasRenderer.GetMesh().vertexCount, Is.GreaterThan(0));
        yield return new WaitForSeconds(.7f);
        Assert.That(group.alpha, Is.Zero);
    }

    [UnityTest]
    public IEnumerator RightMouseRotatesHeldPartWithoutDraggingItsPosition()
    {
        spare.SetSupported(true);
        spare.transform.position += Vector3.up * .35f;
        spare.Body.position = spare.transform.position;
        Physics.SyncTransforms();
        yield return Grab(spare);
        yield return new WaitForSeconds(.4f);
        Vector3 source = spare.Body.position;
        Quaternion rotation = spare.Body.rotation;
        Vector2 pointer = mouse.position.ReadValue() + Vector2.left * 80f;
        yield return Pointer(pointer, 3, Vector2.left * 80f);
        yield return new WaitForSeconds(.5f);
        Assert.That(Vector3.Distance(spare.Body.position, source), Is.LessThan(.05f),
            "Mouse movement used to rotate the held module must not drag it across the table.");
        Assert.That(Quaternion.Angle(spare.Body.rotation, rotation), Is.GreaterThan(5f));
        yield return Pointer(pointer, 1);
        yield return new WaitForSeconds(.2f);
        Assert.That(Vector3.Distance(spare.Body.position, source), Is.LessThan(.05f),
            "Releasing RMB must resume translation without a jump to the displaced cursor.");
    }

    [UnityTest]
    public IEnumerator DistantPhysicalPartCannotSnapIntoSocketByCursorAlone()
    {
        foreach (ScrewFastener screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        robot.Cover.RecoverToTable();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        RepairPart original = robot.InstalledDrive;
        foreach (ScrewFastener screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(original, false, out _), Is.True);
        original.RecoverToTable();
        yield return new WaitForSeconds(.4f);
        yield return Grab(spare);
        spare.Body.constraints = RigidbodyConstraints.FreezeAll;
        Assert.That(Vector3.Distance(spare.Body.position, spare.InstallationAnchor.position), Is.GreaterThan(.5f));
        Vector2 socket = Screen(spare.InstallationAnchor.position);
        yield return Pointer(socket, 1);
        yield return new WaitForSeconds(.5f);
        Assert.That(interaction.DropAnchor, Is.Null, "A blocked or lagging body is not yet ready to install.");
        yield return Pointer(socket, 0);
        Assert.That(spare.IsInstalled, Is.False);
        Assert.That(Vector3.Distance(spare.Body.position, spare.InstallationAnchor.position), Is.GreaterThan(.5f),
            "Releasing over the socket must not teleport a distant body through physical obstacles.");
        spare.Body.constraints = RigidbodyConstraints.None;
    }

    [UnityTest]
    public IEnumerator OccludedStandDoesNotOfferOrAcceptPlacement()
    {
        yield return Grab(spare);
        Vector2 socket = Screen(bench.DriveStand.Socket.position);
        yield return Pointer(socket, 1);
        yield return new WaitForSeconds(.5f);
        Assert.That(interaction.DropAnchor, Is.SameAs(bench.DriveStand.Socket), "The unobstructed fixture must offer placement first.");
        Ray ray = player.View.ScreenPointToRay(socket);
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = ray.GetPoint(.35f);
        wall.transform.localScale = Vector3.one * .15f;
        Physics.SyncTransforms();
        yield return Pointer(socket, 1);
        Assert.That(interaction.DropAnchor, Is.Null, "An obstructed destination must not promise installation in the HUD.");
        yield return Pointer(socket, 0);
        Assert.That(bench.DriveStand.Part, Is.Null);
        Assert.That(spare.IsDragging, Is.False);
    }

    [UnityTest]
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
