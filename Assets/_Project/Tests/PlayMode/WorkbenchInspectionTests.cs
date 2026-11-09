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
        Object.FindFirstObjectByType<Workbench>().PositionRobot(null);
        robot = workbench.Robot;
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        Vector3 standing = workbench.transform.TransformPoint(new Vector3(0f, 0f, -2.2f));
        Vector3 target = robot.InspectionPivot.GetComponentInChildren<Collider>().bounds.center;
        Vector3 direction = target - standing;
        direction.y = 0f;
        player.transform.SetPositionAndRotation(standing, Quaternion.LookRotation(direction));
        player.View.transform.rotation = Quaternion.LookRotation(target - player.View.transform.position);
        float pitch = player.View.transform.localEulerAngles.x;
        typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(player, pitch > 180f ? pitch - 360f : pitch);
        body.enabled = true;
        Physics.SyncTransforms();
        yield return null;
        yield return Press(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True, $"Ray at {target}. Workbench pointed: {interaction.PointedWorkbench != null}. {interaction.Feedback}");
    }

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
    }

    IEnumerator Pointer(Vector2 position, ushort buttons = 0, Vector2 delta = default, float wheel = 0)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = buttons,
            delta = delta, scroll = new Vector2(0, wheel) });
        yield return null; yield return null;
    }

    Vector2 ScreenUV(DirtPatch patch, Vector2 uv) => player.View.WorldToScreenPoint(
        patch.transform.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0f)));

    [UnityTest]
    public IEnumerator EntryUsesSceneAnchorAndNeverReparentsCamera()
    {
        Assert.That(workbench.IsFocused, Is.True);
        Assert.That(interaction.IsInspecting, Is.True);
        Assert.That(player.View.transform.parent, Is.SameAs(player.transform));
        Vector3 start = player.View.transform.position;
        yield return new WaitForSeconds(1.5f);
        Assert.That(Vector3.Distance(start, player.View.transform.position), Is.GreaterThan(.01f));
        Transform anchor = workbench.transform.Find("CameraAnchor");
        Assert.That(Vector3.Distance(player.View.transform.position, anchor.position), Is.LessThan(.002f));
        Assert.That(Quaternion.Angle(player.View.transform.rotation, anchor.rotation), Is.LessThan(.1f));
        yield return Press(Key.F);
        Assert.That(interaction.IsInspecting, Is.True, "There is no second camera mode.");
        yield return Press(Key.Escape);
        Assert.That(interaction.IsAtWorkbench, Is.False);
    }

    [UnityTest]
    public IEnumerator OrbitMovesCameraAndLeavesRobotAndSupportedPartsFixed()
    {
        yield return new WaitForSeconds(1.5f);
        Transform root = robot.InspectionPivot;
        Quaternion initial = root.rotation;
        Vector3 rootPosition = root.position;
        Vector3 cameraPosition = player.View.transform.position;
        Quaternion cameraRotation = player.View.transform.rotation;
        Vector3 trayPosition = robot.Cover.TrayAnchor.position;
        var spare = robot.GetComponentsInChildren<RepairPart>(true).Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
        spare.SetSupported(true);
        Vector3 sparePosition = spare.transform.position;
        workbench.UpdateView(new Vector2(200f, 100f), 0f);
        yield return new WaitForSeconds(1f);
        Assert.That(Vector3.Distance(cameraPosition, player.View.transform.position), Is.GreaterThan(.05f));
        Assert.That(Quaternion.Angle(cameraRotation, player.View.transform.rotation), Is.GreaterThan(10f));
        Assert.That(Vector3.Distance(rootPosition, root.position), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(initial, root.rotation), Is.LessThan(.01f));
        Assert.That(Vector3.Distance(trayPosition, robot.Cover.TrayAnchor.position), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(sparePosition, spare.transform.position), Is.LessThan(.001f));
        yield return Press(Key.R);
        yield return new WaitForSeconds(1.5f);
        Assert.That(Vector3.Distance(cameraPosition, player.View.transform.position), Is.LessThan(.01f));
        Assert.That(Quaternion.Angle(cameraRotation, player.View.transform.rotation), Is.LessThan(.1f));
        Assert.That(Quaternion.Angle(initial, root.rotation), Is.LessThan(.01f));
        spare.SetSupported(false);
        interaction.ResetRepair();
        Assert.That(workbench.IsFocused, Is.True);
        Assert.That(workbench.Robot, Is.Not.SameAs(robot));
    }

    [UnityTest]
    public IEnumerator ZoomMovesCameraAndKeepsRobotAndTraysFixed()
    {
        yield return new WaitForSeconds(1.5f);
        Vector3 cameraPosition = player.View.transform.position;
        Quaternion cameraRotation = player.View.transform.rotation;
        Vector3 original = robot.InspectionPivot.position;
        Vector3 tray = robot.Cover.TrayAnchor.position;
        yield return Pointer(Center, wheel: 120f);
        yield return Pointer(Center);
        yield return new WaitForSeconds(.5f);
        Assert.That(Vector3.Dot(player.View.transform.position - cameraPosition, player.View.transform.forward), Is.GreaterThan(.01f));
        Assert.That(Vector3.Distance(original, robot.InspectionPivot.position), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(cameraRotation, player.View.transform.rotation), Is.LessThan(.1f));
        Assert.That(Vector3.Distance(tray, robot.Cover.TrayAnchor.position), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(robot.InstalledDrive.transform.position, robot.InstalledDrive.InstallationAnchor.position), Is.LessThan(.001f));
        yield return Press(Key.R);
        yield return new WaitForSeconds(1f);
        Assert.That(Vector3.Distance(original, robot.InspectionPivot.position), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(cameraPosition, player.View.transform.position), Is.LessThan(.01f));
        yield return Pointer(Center, wheel: -120f);
        yield return Pointer(Center);
        yield return new WaitForSeconds(.5f);
        Assert.That(Vector3.Dot(player.View.transform.position - cameraPosition, -player.View.transform.forward), Is.GreaterThan(.01f));
        for (int step = 0; step < 20; step++) workbench.UpdateView(Vector2.zero, -1f);
        yield return new WaitForSeconds(1f);
        Assert.That(3f + workbench.CameraOffset,
            Is.GreaterThanOrEqualTo(Vector3.Distance(player.View.transform.position, robot.InspectionPivot.position)),
            "Zooming out must not put the robot beyond tool reach.");
        yield return Press(Key.Escape);
        Assert.That(Vector3.Distance(original, robot.InspectionPivot.position), Is.LessThan(.001f));
    }

    [UnityTest]
    public IEnumerator LoosePartInspectionFreezesPhysicsWithoutMovingItOrReturningToTray()
    {
        foreach (ScrewFastener screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        RepairPart cover = robot.Cover;
        workbench.Focus(cover);
        Vector3 position = cover.transform.position;
        Quaternion rotation = cover.transform.rotation;
        Assert.That(cover.Body.isKinematic, Is.True);
        workbench.UpdateView(new Vector2(180f, 80f), 120f);
        yield return new WaitForSeconds(.7f);
        Assert.That(Vector3.Distance(position, cover.transform.position), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(rotation, cover.transform.rotation), Is.LessThan(.01f));
        workbench.ReturnToOverview(true);
        Assert.That(Vector3.Distance(position, cover.transform.position), Is.LessThan(.001f));
        Assert.That(cover.Body.isKinematic, Is.False);
    }

    [UnityTest]
    public IEnumerator DisablingCameraAnchorDoesNotDisablePlayerOrReparentCamera()
    {
        workbench.transform.Find("CameraAnchor").gameObject.SetActive(false);
        yield return null;
        Assert.That(player.gameObject.activeInHierarchy, Is.True);
        Assert.That(player.View.transform.parent, Is.SameAs(player.transform));
        interaction.ExitWorkbench();
        yield return new WaitForSeconds(.6f);
        Assert.That(player.IsReturning, Is.False);
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator DisablingPlayerOrTableNeverReparentsCamera()
    {
        Transform camera = player.View.transform;
        workbench.gameObject.SetActive(false);
        yield return null;
        Assert.That(camera.parent, Is.SameAs(player.transform));
        Assert.That(interaction.IsAtWorkbench, Is.False);
        workbench.gameObject.SetActive(true);
        Assert.That(workbench.Enter(player.View), Is.True);
        player.gameObject.SetActive(false);
        var temporaryListener = new GameObject("Test audio listener", typeof(AudioListener));
        yield return null;
        workbench.Exit();
        Assert.That(camera.parent, Is.SameAs(player.transform));
        temporaryListener.SetActive(false);
        Object.Destroy(temporaryListener);
        player.gameObject.SetActive(true);
        LogAssert.NoUnexpectedReceived();
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
