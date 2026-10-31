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

public sealed class DriveTestStandTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    Workbench bench;
    RobotRepair robot;
    InteractionController interaction;
    DriveTestStand stand;
    RepairPart original, spare;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        bench = Object.FindFirstObjectByType<Workbench>();
        robot = bench.Robot;
        stand = bench.DriveStand;
        Assert.That(stand, Is.Not.Null);
        original = robot.InstalledDrive;
        spare = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
        interaction = Object.FindFirstObjectByType<InteractionController>();
        var body = interaction.GetComponent<CharacterController>();
        body.enabled = false;
        interaction.transform.SetPositionAndRotation(Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f)), Quaternion.identity);
        body.enabled = true;
        yield return null;
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        typeof(DriveTestStand).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stand, .2f);
    }

    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
        if (key == Key.E) yield return new WaitForSeconds(1.2f);
    }

    IEnumerator Pointer(Vector2 p, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = p, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null; yield return null;
    }

    string StandDropDiagnostic(RepairPart expected, Vector2 pointer)
    {
        RepairPart held = interaction.HeldPart;
        Ray ray = interaction.View.ScreenPointToRay(pointer);
        var hits = Physics.RaycastAll(ray, 10f, ~(1 << interaction.gameObject.layer), QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance).ToArray();
        var nearest = hits.FirstOrDefault(hit => held == null || hit.collider.GetComponentInParent<RepairPart>() != held);
        string hitNames = string.Join(", ", hits.Take(10).Select(hit =>
            $"{hit.collider.name}@{hit.distance:F3}{(hit.collider.GetComponentInParent<RepairPart>() == held ? "[held]" : "")}"));
        Vector3 target = (Vector3)typeof(InteractionController).GetField("heldTarget", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
        string body = held == null ? "none" : $"{held.Body.position:F3}, rot={held.Body.rotation.eulerAngles:F1}, velocity={held.Body.linearVelocity:F3}, socketDistance={Vector3.Distance(held.Body.position, stand.Socket.position):F3}";
        return $"Expected={expected.name}; held={(held != null ? held.name : "none")}; pointer={pointer:F1}; socketScreen={interaction.View.WorldToScreenPoint(stand.Socket.position):F1}; socket={stand.Socket.position:F3}; body={body}; heldTarget={target:F3}; nearestNonHeld={(nearest.collider != null ? nearest.collider.name : "none")}@{nearest.distance:F3}; hits=[{hitNames}]; feedback={interaction.Feedback}";
    }

    void RemoveOriginal()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        foreach (var screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(original, false, out _), Is.True);
        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator OnlyOwnedReleasedDriveCanUseStand()
    {
        Assert.That(stand.TryLoad(robot, original, out string reason), Is.False);
        Assert.That(reason, Does.Contain("снимите"));
        Assert.That(stand.TryLoad(robot, robot.Cover, out _), Is.False);
        var foreign = Object.Instantiate(robot);
        Assert.That(stand.TryLoad(robot, foreign.GetComponentsInChildren<RepairPart>().First(p => !p.IsInstalled), out _), Is.False);
        Object.Destroy(foreign.gameObject);
        Assert.That(robot.RunTest(out _), Is.True);
        Assert.That(stand.TryLoad(robot, spare, out _), Is.False);
        robot.CancelTest();
        bench.Focus(spare);
        Assert.That(stand.TryLoad(robot, spare, out _), Is.True, "Inspection must not block the stand.");
        stand.Cancel();
        bench.ReturnToOverview();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ComparisonRecordsEachPartAndNeverPassesOrRepairsRobot()
    {
        RemoveOriginal();
        foreach (RepairPart part in new[] { original, spare, original })
        {
            var parent = part.transform.parent;
            Vector3 sourcePosition = part.transform.position;
            Quaternion sourceRotation = part.transform.rotation;
            Assert.That(stand.TryLoad(robot, part, out _), Is.True);
            Assert.That(part.IsInstalled, Is.False);
            Assert.That(part.IsSupported, Is.True);
            Assert.That(part.Body.isKinematic, Is.True);
            Assert.That(stand.RunTest(out _), Is.True);
            while (stand.IsTesting) yield return null;
            Assert.That(part.StandResult, Is.EqualTo(part == original ? DriveTestResult.Interrupted : DriveTestResult.Smooth));
            Assert.That(part.Faulty, Is.EqualTo(part == original));
            Assert.That(part.IsInstalled, Is.False);
            Assert.That(part.transform.parent, Is.SameAs(parent));
            Assert.That(Vector3.Distance(part.transform.position, sourcePosition), Is.LessThan(.03f));
            Assert.That(Quaternion.Angle(part.transform.rotation, sourceRotation), Is.LessThan(2f));
            Assert.That(part.IsSupported, Is.False);
            Assert.That(part.Body.isKinematic, Is.False);
            Assert.That(part.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
            Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
            Assert.That(stand.Part, Is.Null);
            Assert.That(robot.ActiveDriveStand, Is.Null);
        }
        Assert.That(robot.Findings, Does.Contain("Исходный привод: вращение прерывается"));
        Assert.That(robot.Findings, Does.Contain("Запасной привод: вал вращается равномерно"));
        Assert.That(robot.TryPlace(spare, true, out _), Is.True);
        foreach (var screw in spare.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.RunTest(out _), Is.True);
        while (robot.IsTesting) yield return null;
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed));
    }

    [UnityTest]
    public IEnumerator EscCancelsWithoutExitingAndCannotLeaveCompetingInteractions()
    {
        Assert.That(stand.TryLoad(robot, spare, out _), Is.True);
        Assert.That(stand.RunTest(out _), Is.True);
        Assert.That(robot.CanMove(robot.Cover, out _), Is.False);
        Assert.That(robot.CanTurn(robot.Cover.Screws[0], out _), Is.False);
        Assert.That(robot.CanClean(robot.DirtPatches[0], out _), Is.False);
        Assert.That(robot.CanRunTest(out _), Is.False);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.False);
        bench.Focus(spare);
        Assert.That(bench.IsFocused, Is.True);
        Assert.That(bench.FocusedPart, Is.Null, "The active stand must not switch the inspected object.");
        interaction.SelectToolShortcut(1);
        Assert.That(interaction.ScrewdriverSelected, Is.False);
        yield return KeyPress(Key.Escape);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(stand.Part, Is.Null);
        Assert.That(spare.StandResult, Is.EqualTo(DriveTestResult.NotTested));
        Assert.That(spare.GetComponentsInChildren<Collider>().All(c => c.enabled), Is.True);
        Assert.That(robot.ActiveDriveStand, Is.Null);
    }

    [UnityTest]
    public IEnumerator LostFocusExitResetAndDisableRestorePartAndReleaseSlot()
    {
        foreach (int cancellation in new[] { 0, 1, 2 })
        {
            Vector3 source = spare.transform.position;
            Assert.That(stand.TryLoad(robot, spare, out _), Is.True);
            Assert.That(stand.RunTest(out _), Is.True);
            if (cancellation == 0) interaction.SendMessage("OnApplicationFocus", false);
            if (cancellation == 1) stand.enabled = false;
            if (cancellation == 2) bench.Exit();
            Assert.That(stand.Part, Is.Null);
            Assert.That(robot.ActiveDriveStand, Is.Null);
            Assert.That(Vector3.Distance(spare.transform.position, source), Is.LessThan(.0001f));
            Assert.That(spare.IsSupported, Is.False);
            Assert.That(spare.Body.isKinematic, Is.False);
            Assert.That(spare.StandResult, Is.EqualTo(DriveTestResult.NotTested));
            stand.enabled = true;
        }
        yield return null;
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(stand.TryLoad(robot, spare, out _), Is.True);
        interaction.ResetRepair();
        yield return null;
        Assert.That(stand.Part, Is.Null);
        Assert.That(bench.Robot, Is.Not.SameAs(robot));
        Assert.That(bench.Robot.ActiveDriveStand, Is.Null);
    }

    [UnityTest]
    public IEnumerator ReleasedDriveGoesDirectlyFromRobotToStandAndReturnsToWorldPose()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        foreach (var screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(stand.TryLoad(robot, original, out _), Is.False, "A connected drive must not bypass the connector rule.");
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Assert.That(original.IsInstalled, Is.True);
        Assert.That(stand.TryLoad(robot, original, out string reason), Is.True, reason);
        Assert.That(original.IsInstalled, Is.False);
        Assert.That(original.IsSupported, Is.True);
        Assert.That(original.Body.isKinematic, Is.True);
        Assert.That(Vector3.Distance(original.transform.position, stand.Socket.position), Is.LessThan(.001f));
        yield return null;
        Assert.That(Vector3.Distance(original.Body.position, original.transform.position), Is.LessThan(.001f),
            "The supported actor must follow the visible drive on the stand.");
        Assert.That(Quaternion.Angle(original.Body.rotation, original.transform.rotation), Is.LessThan(.01f));
        stand.Cancel();
        Assert.That(original.IsSupported, Is.False);
        Assert.That(original.Body.isKinematic, Is.False);
        Assert.That(original.IsInstalled, Is.False);
        Assert.That(Vector3.Distance(original.transform.position, original.TrayAnchor.position), Is.GreaterThan(.1f),
            "The stand must not introduce a mandatory tray visit on release.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PointerCanLoadClickTestAndReturnBothDrives()
    {
        RemoveOriginal();
        robot.Cover.RecoverToTable();
        original.RecoverToTable();
        yield return new WaitForSeconds(.5f);
        foreach (var part in new[] { original, spare })
        {
            yield return KeyPress(Key.Digit1);
            Vector3 pickupPosition = part.Body.position;
            Vector2 source = interaction.View.WorldToScreenPoint(part.transform.position);
            yield return Pointer(source, true);
            Assert.That(interaction.HeldPart, Is.SameAs(part), interaction.Feedback);
            Vector2 target = interaction.View.WorldToScreenPoint(stand.Socket.position);
            yield return Pointer(target, true);
            yield return new WaitForSeconds(.5f);
            Assert.That(interaction.DropAnchor, Is.SameAs(stand.Socket), StandDropDiagnostic(part, target));
            yield return Pointer(target, false);
            Assert.That(stand.Part, Is.SameAs(part), interaction.Feedback);
            Assert.That(stand.IsTesting, Is.False, "Loading alone does not start the test.");
            yield return Pointer(target, true);
            Assert.That(stand.IsTesting, Is.True, interaction.Feedback);
            yield return Pointer(target, false);
            while (stand.IsTesting) yield return null;
            Assert.That(part.StandResult, Is.Not.EqualTo(DriveTestResult.NotTested));
            Assert.That(interaction.HeldPart, Is.Null);
            Assert.That(Vector3.Distance(part.Body.position, pickupPosition), Is.LessThan(.05f),
                "The tested drive must return to its pickup position so it does not obstruct the next drive.");
        }
    }

    [UnityTest]
    public IEnumerator FaultyShaftVisiblyPausesWhileHealthyShaftKeepsTurning()
    {
        RemoveOriginal();
        typeof(DriveTestStand).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stand, 1.1f);
        var spindle = (Transform)typeof(DriveTestStand).GetField("spindle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stand);
        foreach (var part in new[] { original, spare })
        {
            Assert.That(stand.TryLoad(robot, part, out _), Is.True);
            Assert.That(stand.RunTest(out _), Is.True);
            int movingFrames = 0, stoppedFrames = 0;
            while (stand.IsTesting)
            {
                Quaternion previous = spindle.localRotation;
                yield return new WaitForSecondsRealtime(.025f);
                if (!stand.IsTesting) break;
                if (Quaternion.Angle(previous, spindle.localRotation) < .01f) stoppedFrames++;
                else movingFrames++;
            }
            Assert.That(movingFrames, Is.GreaterThan(0), "Both drives must visibly turn.");
            if (part == original) Assert.That(stoppedFrames, Is.GreaterThan(0), "The internal defect must be visible as pauses.");
            else Assert.That(stoppedFrames, Is.Zero, "The healthy shaft must not imitate the faulty shaft's pauses.");
        }
    }

    [UnityTest]
    public IEnumerator ArmSymptomsDistinguishStallingFromNoPowerWithoutSound()
    {
        var poweredShoulder = (Transform)typeof(RobotRepair).GetField("shoulder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(robot);
        var poweredRest = poweredShoulder.localRotation;
        float largestTwitch = 0f;
        Assert.That(robot.RunTest(out _), Is.True);
        while (robot.IsTesting)
        {
            largestTwitch = Mathf.Max(largestTwitch, Quaternion.Angle(poweredRest, poweredShoulder.localRotation));
            yield return null;
        }
        Assert.That(largestTwitch, Is.GreaterThan(1f), "A powered mechanical fault must visibly twitch.");
        Assert.That(robot.LastArmObservation, Is.EqualTo(ArmObservation.Stuttering));
        Assert.That(robot.ArmObservationText, Does.Contain("дёргается"));
        interaction.ExitWorkbench();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        yield return null;
        var session = Object.FindFirstObjectByType<WorkshopSession>();
        var orders = (OrderDefinition[])typeof(WorkshopSession).GetField("orders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        var contactRobot = orders.First(o => o.RequiresSolderingStation).RobotPrefab;
        bench = Object.FindFirstObjectByType<Workbench>();
        var contact = bench.LoadRobot(contactRobot);
        Assert.That(contact, Is.Not.Null);
        var shoulder = (Transform)typeof(RobotRepair).GetField("shoulder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(contact);
        var rest = shoulder.localRotation;
        Assert.That(contact.RunTest(out _), Is.True);
        while (contact.IsTesting)
        {
            Assert.That(Quaternion.Angle(rest, shoulder.localRotation), Is.LessThan(.001f));
            yield return null;
        }
        Assert.That(contact.LastArmObservation, Is.EqualTo(ArmObservation.NoMovement));
        Assert.That(contact.TestResult, Is.EqualTo(RobotTestResult.Faulty));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("StandCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
