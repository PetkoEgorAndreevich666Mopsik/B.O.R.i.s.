using System.Collections;
using System.Linq;
using System.Reflection;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class SolderingFlowTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    InteractionController interaction;
    RobotRepair robot;
    SolderJoint joint;
    SolderingTool tool;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindAnyObjectByType<WorkshopSession>();
        interaction = Object.FindAnyObjectByType<InteractionController>();
        yield return null; yield return null;
    }

    static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
        if (key == Key.E) yield return new WaitForSeconds(1.2f);
    }

    IEnumerator Pointer(Vector2 position, bool down = false)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null; yield return null;
    }

    Vector2 ScreenPosition(float position) => interaction.View.WorldToScreenPoint(joint.PointOnSeam(position));

    void Face(Vector3 position)
    {
        var body = interaction.GetComponent<CharacterController>();
        body.enabled = false;
        interaction.transform.SetPositionAndRotation(position, Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
    }

    IEnumerator EnterC(bool seedEconomy = true)
    {
        if (seedEconomy)
        {
            SetField(session, "<Balance>k__BackingField", 180);
            Assert.That(session.TryBuySolderingStation(out _), Is.True);
            SetField(session, "nextOrder", 2);
        }
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("C"));
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
        Object.FindFirstObjectByType<Workbench>().PositionRobot(null);
        robot = session.ActiveRobot;
        joint = robot.SolderJoint;
        tool = Object.FindObjectsByType<SolderingTool>(FindObjectsInactive.Include).Single();
        Face(Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f)));
        yield return null;
        yield return Press(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(joint, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator SolderingAndConnectorRemainAvailableDuringInspection()
    {
        yield return EnterC();
        OpenCover();
        yield return Pointer(ScreenPosition(.5f));
        yield return Press(Key.F);
        Assert.That(interaction.IsInspecting, Is.True);
        yield return new WaitForSeconds(1.3f);
        yield return Pointer(interaction.View.WorldToScreenPoint(robot.Connector.transform.position), true);
        yield return Pointer(interaction.View.WorldToScreenPoint(robot.Connector.transform.position));
        Assert.That(robot.Connector.IsConnected, Is.False);
        Assert.That(interaction.IsInspecting, Is.True);
        yield return Press(Key.Digit4);
        yield return Pointer(ScreenPosition(.01f), true);
        Assert.That(tool.IsActive, Is.True);
        for (int i = 1; i <= 30; i++)
        {
            Vector2 pointer = ScreenPosition(.01f + .98f * i / 30);
            yield return Pointer(pointer, true);
            Physics.Raycast(interaction.View.ScreenPointToRay(pointer), out var hit, 10f, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(tool.HasContact || joint.IsRepaired, Is.True,
                $"Seam sample {i}: {pointer}, hit {hit.collider?.name}, progress {joint.Progress}");
        }
        yield return Pointer(ScreenPosition(.99f));
        Assert.That(joint.IsRepaired, Is.True);
        Assert.That(interaction.IsInspecting, Is.True);
        yield return Press(Key.F);
        Assert.That(joint.IsRepaired, Is.True);
    }

    [UnityTest]
    public IEnumerator InspectionRotatesBoardWithAssemblyAndReturnsBeforeSoldering()
    {
        yield return EnterC();
        var workbench = Object.FindFirstObjectByType<Workbench>();
        Vector3 position = joint.transform.position;
        Quaternion rotation = joint.transform.rotation;
        yield return Pointer(interaction.View.WorldToScreenPoint(robot.Cover.transform.position));
        yield return Press(Key.F);
        Assert.That(workbench.IsFocused, Is.True);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f),
            buttons = 2, delta = new Vector2(300, 80) });
        yield return null; yield return null;
        Assert.That(Quaternion.Angle(rotation, joint.transform.rotation), Is.GreaterThan(20f));
        Assert.That(joint.Progress, Is.Zero);
        yield return Pointer(new Vector2(Screen.width * .5f, Screen.height * .5f));
        yield return Press(Key.R);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(interaction.IsInspecting, Is.True);
        yield return new WaitForSeconds(.6f);
        Assert.That(Vector3.Distance(joint.transform.position, position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(joint.transform.rotation, rotation), Is.LessThan(.01f));
        PrepareSeam();
        Assert.That(robot.CanSolder(joint, session, out string reason), Is.True, reason);
    }

    void OpenCover()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Physics.SyncTransforms();
    }

    void PrepareSeam()
    {
        OpenCover();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Physics.SyncTransforms();
    }

    IEnumerator FinishAndDeliver()
    {
        if (!robot.Connector.IsConnected) Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.RunTest(out _), Is.True);
        Assert.That(session.CanDeliverOrder(out _), Is.False);
        float deadline = Time.realtimeSinceStartup + 4f;
        while (robot.IsTesting && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed));
        yield return Press(Key.Escape);
        string id = session.ActiveOrderId;
        RobotTransportTestFlow.Deliver(session);
        Assert.That(session.TryDeliverOrder(id, out string reason), Is.True, reason);
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
    }

    IEnumerator CompleteBasicOrder()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Object.FindFirstObjectByType<Workbench>().PositionRobot(null);
        robot = session.ActiveRobot;
        OpenCover();
        if (robot.InstalledDrive.Faulty)
        {
            robot.ToggleConnection(robot.Connector, out _);
            var old = robot.InstalledDrive;
            foreach (var screw in old.Screws) robot.Turn(screw, -10000f);
            Assert.That(robot.TryPlace(old, false, out _), Is.True);
            var replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
            Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
            foreach (var screw in replacement.Screws) robot.Turn(screw, 10000f);
        }
        if (session.ActiveOrder.RequiresCleaning)
            foreach (var patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        yield return FinishAndDeliver();
        yield return null;
    }

    [UnityTest]
    public IEnumerator EarnBuySolderTestDeliverAndContinueWithoutUnlockCheats()
    {
        yield return CompleteBasicOrder();
        Assert.That(session.Balance, Is.EqualTo(100));
        yield return CompleteBasicOrder();
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.OfferedOrder.OrderId, Does.StartWith("A"), "C stays locked before the purchase.");
        Face(new Vector3(2.6f, 0, -1.9f));
        yield return Press(Key.E);
        Assert.That(interaction.IsAtEquipmentShop, Is.True);
        var buy = Object.FindObjectsByType<Button>().Single(b => b.name == "BuyStationButton");
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, buy.transform.position);
        yield return Pointer(point, true); yield return Pointer(point);
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(session.CanOfferSolderingOrders, Is.True);
        yield return Press(Key.Escape);
        yield return EnterC(false);
        Assert.That(robot.InstalledDrive.Faulty, Is.False);
        Assert.That(robot.IsFunctional, Is.False);
        PrepareSeam();
        yield return Pointer(ScreenPosition(.5f));
        yield return new WaitForSeconds(.65f);
        Assert.That(robot.JointFaultFound, Is.True, "An exposed seam can be diagnosed by observation.");
        yield return Press(Key.Digit4);
        Assert.That(interaction.SolderingSelected, Is.True);
        yield return Pointer(ScreenPosition(.01f), true);
        Assert.That(tool.Target, Is.SameAs(joint), "The seam must be reachable through the real camera ray.");
        for (int i = 1; i <= 20; i++) yield return Pointer(ScreenPosition(.01f + i * .049f), true);
        yield return Pointer(ScreenPosition(.99f));
        Assert.That(joint.IsRepaired, Is.True);
        Assert.That(robot.IsFunctional, Is.True);
        yield return FinishAndDeliver();
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.CompletedOrders, Is.EqualTo(3));
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("D-001"));
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Object.FindFirstObjectByType<Workbench>().PositionRobot(null);
    }

    [UnityTest]
    public IEnumerator StationaryHoldCannotRepairAndCancelPreservesVisiblePartialWork()
    {
        yield return EnterC(); PrepareSeam();
        Vector2 handle = interaction.View.WorldToScreenPoint(tool.transform.Find("Handle").position);
        yield return Pointer(handle, true); yield return Pointer(handle);
        Assert.That(interaction.SolderingSelected, Is.True, "The physical iron must be visible and selectable on its station.");
        yield return Pointer(ScreenPosition(.05f), true);
        yield return new WaitForSeconds(.2f);
        Assert.That(joint.Progress, Is.Zero);
        yield return Pointer(ScreenPosition(.35f), true);
        float progress = joint.Progress;
        Assert.That(progress, Is.InRange(.2f, .5f));
        var segments = joint.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Trace")).ToArray();
        Assert.That(segments.Count(r => r.sharedMaterial.name == "SolderRestored"), Is.GreaterThan(0).And.LessThan(24));
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(tool.IsActive, Is.False);
        Assert.That(tool.GetComponent<AudioSource>().isPlaying, Is.False);
        yield return Pointer(ScreenPosition(.35f));
        yield return Pointer(ScreenPosition(.4f), true);
        yield return Press(Key.Digit2);
        Assert.That(tool.IsActive, Is.False);
        Assert.That(joint.Progress, Is.EqualTo(progress));
        yield return Press(Key.Escape);
        yield return Pointer(Vector2.zero);
        yield return Press(Key.E);
        Assert.That(joint.Progress, Is.EqualTo(progress));
        interaction.SelectToolShortcut(3);
        Assert.That(interaction.SolderingSelected, Is.True);
        yield return Pointer(ScreenPosition(.99f), true);
        yield return Pointer(ScreenPosition(.01f), true);
        yield return Pointer(ScreenPosition(.01f));
        Assert.That(joint.IsRepaired, Is.True, "Reverse travel can finish the seam.");
    }

    [UnityTest]
    public IEnumerator LostContactAndOcclusionCannotBridgeUnvisitedSegments()
    {
        yield return EnterC(); PrepareSeam();
        yield return Press(Key.Digit4);
        yield return Pointer(ScreenPosition(.05f), true);
        yield return Pointer(ScreenPosition(-1f), true);
        Assert.That(tool.HasContact, Is.False);
        yield return Pointer(ScreenPosition(.9f), true);
        Assert.That(tool.HasContact, Is.True);
        Assert.That(joint.Progress, Is.Zero);
        yield return Pointer(ScreenPosition(.9f));
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = Vector3.Lerp(interaction.View.transform.position, joint.transform.position, .5f);
        blocker.transform.localScale = Vector3.one * .2f;
        Physics.SyncTransforms();
        yield return Pointer(ScreenPosition(.1f), true);
        yield return Pointer(ScreenPosition(.8f), true);
        Assert.That(joint.Progress, Is.Zero);
        Assert.That(tool.IsActive, Is.False);
        Object.Destroy(blocker);
    }

    [UnityTest]
    public IEnumerator DomainRulesRejectClosedConnectedForeignDisabledAndUnownedTargets()
    {
        yield return EnterC();
        Assert.That(robot.CanSolder(joint, session, out _), Is.False);
        OpenCover();
        Assert.That(robot.CanSolder(joint, session, out string reason), Is.False);
        Assert.That(reason, Does.Contain("разъём"));
        robot.ToggleConnection(robot.Connector, out _);
        Assert.That(robot.CanSolder(joint, session, out _), Is.True);
        var other = Object.Instantiate(robot.gameObject);
        Assert.That(robot.CanSolder(other.GetComponent<RobotRepair>().SolderJoint, session, out _), Is.False);
        Assert.That(other.GetComponent<RobotRepair>().CanSolder(other.GetComponent<RobotRepair>().SolderJoint, session, out _), Is.False);
        Object.Destroy(other);
        joint.enabled = false;
        Assert.That(robot.SolderStroke(joint, session, 0, 1), Is.Zero);
        joint.enabled = true;
        SetField(session, "<HasSolderingStation>k__BackingField", false);
        Assert.That(robot.CanSolder(joint, session, out _), Is.False);
        yield return Press(Key.Digit4);
        Assert.That(interaction.SolderingSelected, Is.False);
        Assert.That(tool.Begin(joint, interaction.View, ScreenPosition(.1f)), Is.False);
        Assert.That(robot.SolderStroke(joint, session, 0, 1), Is.Zero);
        SetField(session, "<HasSolderingStation>k__BackingField", true);
        session.enabled = false;
        Assert.That(robot.SolderStroke(joint, session, 0, 1), Is.Zero);
        session.enabled = true;
        Assert.That(robot.SolderStroke(joint, session, float.NaN, 1), Is.Zero);
        Assert.That(robot.SolderStroke(joint, session, -.1f, 1), Is.Zero);
        Assert.That(robot.SolderStroke(joint, session, .5f, .5f), Is.Zero);
        Assert.That(joint.Progress, Is.Zero);
    }

    [UnityTest]
    public IEnumerator CoverageDoesNotDependOnSamplingAndResetCreatesFreshJoint()
    {
        yield return EnterC(); PrepareSeam();
        Assert.That(robot.SolderStroke(joint, session, .1f, .6f), Is.GreaterThan(0));
        float expected = joint.Progress;
        Assert.That(robot.SolderStroke(joint, session, .1f, .6f), Is.Zero);
        interaction.ResetRepair();
        yield return null;
        robot = session.ActiveRobot;
        joint = robot.SolderJoint;
        Assert.That(joint.Progress, Is.Zero);
        Assert.That(robot.JointFaultFound, Is.False);
        PrepareSeam();
        for (int i = 0; i < 20; i++) robot.SolderStroke(joint, session, .1f + i * .025f, .1f + (i + 1) * .025f);
        Assert.That(joint.Progress, Is.EqualTo(expected));
        Assert.That(robot.SolderStroke(joint, session, 1f, 0f), Is.GreaterThan(0));
        Assert.That(joint.Progress, Is.EqualTo(1f));
        Assert.That(robot.SolderStroke(joint, session, 0f, 1f), Is.Zero);
    }

    [UnityTest]
    public IEnumerator GoodReplacementCannotBypassBrokenContactOrTestAndUiStopsStroke()
    {
        yield return EnterC(); PrepareSeam();
        var old = robot.InstalledDrive;
        foreach (var screw in old.Screws) robot.Turn(screw, -10000f);
        robot.TryPlace(old, false, out _);
        var replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled && p != old);
        Assert.That(replacement.Faulty, Is.False);
        Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
        foreach (var screw in replacement.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.IsFunctional, Is.False);
        robot.ToggleConnection(robot.Connector, out _);
        robot.TryPlace(robot.Cover, true, out _);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.RunTest(out _), Is.True);
        Assert.That(robot.CanSolder(joint, session, out _), Is.False);
        while (robot.IsTesting) yield return null;
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Faulty));
        Assert.That(session.CanDeliverOrder(out _), Is.False);
        PrepareSeam();
        yield return Press(Key.Digit4);
        yield return Pointer(ScreenPosition(.1f), true);
        var overlay = new GameObject("SolderBlockingUI", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(Object.FindAnyObjectByType<Canvas>().transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return null; yield return null;
        Assert.That(tool.IsActive, Is.False);
        Assert.That(joint.Progress, Is.Zero);
        Object.Destroy(overlay);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("SolderCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
