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

public sealed class RepairCompletionTests
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
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindAnyObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        workbench = Object.FindAnyObjectByType<Workbench>();
        robot = Object.FindAnyObjectByType<RobotRepair>();
        var body = player.GetComponent<CharacterController>();
        body.enabled = false; player.transform.position = Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f)); body.enabled = true;
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
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
        if (key == Key.E) yield return new WaitForSeconds(1.2f);
    }

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
        yield return null; yield return null;
    }

    void OpenCover()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out string reason), Is.True, reason);
    }

    void Repair()
    {
        OpenCover();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        RepairPart old = robot.InstalledDrive;
        foreach (var screw in old.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(old, false, out _), Is.True);
        var replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
        Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
        foreach (var screw in replacement.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
    }

    IEnumerator WaitForTest()
    {
        float deadline = Time.realtimeSinceStartup + 3f;
        while (robot.IsTesting && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(robot.IsTesting, Is.False, "Test must finish within its configured duration.");
    }

    [UnityTest]
    public IEnumerator PhysicalButtonShowsFaultAndBlocksRepairDuringTest()
    {
        Quaternion resting = robot.InspectionPivot.Find("ShoulderPivot").localRotation;
        yield return Pointer(player.View.WorldToScreenPoint(workbench.TestButton.transform.position), true);
        Assert.That(robot.IsTesting, Is.True, "Real click must reach the physical test button.");
        yield return Pointer(Vector2.zero, false);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot.Turn(robot.Cover.Screws[0], -180f), Is.Zero);
        Assert.That(robot.CanMove(robot.Cover, out _), Is.False);
        Assert.That(robot.CanClean(robot.GetComponentsInChildren<DirtPatch>()[0], out _), Is.False);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.False);
        Assert.That(robot.RunTest(out _), Is.False);
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(Quaternion.Angle(resting, robot.InspectionPivot.Find("ShoulderPivot").localRotation), Is.GreaterThan(.05f));
        yield return WaitForTest();
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Faulty));
        Assert.That(robot.AssemblyHint, Does.Contain("не пройден"));
        Assert.That(Quaternion.Angle(resting, robot.InspectionPivot.Find("ShoulderPivot").localRotation), Is.LessThan(.01f));
    }

    [UnityTest]
    public IEnumerator IncompleteAssemblyCannotStartOrAnnounceSuccess()
    {
        OpenCover();
        Assert.That(robot.RunTest(out string reason), Is.False);
        Assert.That(reason, Does.Contain("крышку"));
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        Assert.That(robot.RunTest(out reason), Is.False);
        Assert.That(reason, Does.Contain("разъём"));
        foreach (var screw in robot.InstalledDrive.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.RunTest(out reason), Is.False);
        Assert.That(reason, Does.Contain("винта привода"));
        Assert.That(robot.TryPlace(robot.InstalledDrive, false, out _), Is.True);
        Assert.That(robot.RunTest(out reason), Is.False);
        Assert.That(reason, Does.Contain("установите привод"));
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        yield return null;
    }

    [UnityTest]
    public IEnumerator SuccessRequiresCompletedTestAndRealDisassemblyInvalidatesIt()
    {
        Repair();
        Assert.That(robot.IsFunctional, Is.True);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot.RunTest(out _), Is.True);
        yield return new WaitForSecondsRealtime(.25f);
        Assert.That(robot.IsTesting, Is.True);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot.AssemblyHint, Does.Not.Contain("РЕМОНТ ЗАВЕРШЁН"));
        yield return WaitForTest();
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed));
        Assert.That(Quaternion.Angle(Quaternion.identity, robot.InspectionPivot.Find("ShoulderPivot").localRotation), Is.EqualTo(65f).Within(.1f));
        Assert.That(robot.Turn(robot.Cover.Screws[0], 20f), Is.Zero);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed), "An end-stop is not a repair change.");
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed), "Completed result survives a visit.");
        robot.Turn(robot.Cover.Screws[0], -20f);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot.RunTest(out string reason), Is.False);
        Assert.That(reason, Does.Contain("винта крышки"));
    }

    [UnityTest]
    public IEnumerator ExitAndFocusLossCancelSuccessfulTestWithoutLateSuccess()
    {
        Repair();
        robot.RunTest(out _);
        yield return Press(Key.Escape);
        Assert.That(robot.IsTesting, Is.False);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        yield return new WaitForSecondsRealtime(2f);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        yield return Press(Key.E);
        Assert.That(robot.RunTest(out _), Is.True);
        interaction.SendMessage("OnApplicationFocus", false);
        Assert.That(robot.IsTesting, Is.False);
        yield return new WaitForSecondsRealtime(2f);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
    }

    [UnityTest]
    public IEnumerator ResetReplacesDirtyRobotAndCancelsHeldPart()
    {
        var dirt = robot.GetComponentsInChildren<DirtPatch>().First();
        dirt.CleanStroke(new Vector2(.2f, .5f), new Vector2(.8f, .5f), .035f);
        Assert.That(dirt.Cleanliness, Is.GreaterThan(0f));
        OpenCover();
        Physics.SyncTransforms();
        yield return Pointer(player.View.WorldToScreenPoint(robot.Cover.transform.position), true);
        Assert.That(interaction.HeldPart, Is.SameAs(robot.Cover));
        RobotRepair old = robot;
        interaction.ResetRepair();
        Assert.That(old.gameObject.activeSelf, Is.False);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(interaction.Robot, Is.Not.SameAs(old));
        robot = interaction.Robot;
        yield return Pointer(Vector2.zero, false);
        Assert.That(old == null, Is.True);
        AssertInitial();
        Assert.That(player.View.transform.parent, Is.SameAs(player.transform));
    }

    [UnityTest]
    public IEnumerator RestartButtonCancelsRunningTestAndNewRobotGetsNoOldResult()
    {
        Repair();
        Assert.That(robot.RunTest(out _), Is.True);
        var restart = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "RestartButton");
        Vector2 pointer = RectTransformUtility.WorldToScreenPoint(null, restart.transform.position);
        yield return Pointer(pointer, true);
        yield return Pointer(pointer, false);
        Assert.That(interaction.Robot, Is.Not.SameAs(robot));
        robot = interaction.Robot;
        yield return new WaitForSecondsRealtime(2f);
        AssertInitial();
        Assert.That(Object.FindObjectsByType<RobotRepair>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(robot.RunTest(out _), Is.True);
        yield return WaitForTest();
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Faulty));
    }

    void AssertInitial()
    {
        Assert.That(robot.Cover.IsInstalled, Is.True);
        Assert.That(RobotRepair.FullyTightened(robot.Cover), Is.True);
        Assert.That(robot.InstalledDrive.Faulty, Is.True);
        Assert.That(RobotRepair.FullyTightened(robot.InstalledDrive), Is.True);
        Assert.That(robot.Connector.IsConnected, Is.True);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot.IsTesting, Is.False);
        Assert.That(robot.GetComponentsInChildren<RepairPart>().Count(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled && !p.Faulty), Is.EqualTo(1));
        Assert.That(robot.GetComponentsInChildren<DirtPatch>().All(p => p.Cleanliness == 0f), Is.True);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("CompletionCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
