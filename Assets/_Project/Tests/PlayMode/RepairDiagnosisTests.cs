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

public sealed class RepairDiagnosisTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    PlayerController player;
    InteractionController interaction;
    RobotRepair robot;

    Text TextNamed(string name) => Object.FindObjectsByType<Text>(FindObjectsInactive.Include,
        FindObjectsSortMode.None).Single(t => t.name == name);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindFirstObjectByType<WorkshopSession>();
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        yield return null;
    }

    IEnumerator EnterOrder(bool cleaning = false)
    {
        var orders = (OrderDefinition[])typeof(WorkshopSession).GetField("orders",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        int repairIndex = System.Array.FindIndex(orders, o => o.RequiresCleaning == cleaning);
        typeof(WorkshopSession).GetField("nextOrder", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(session, repairIndex);
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        robot = session.ActiveRobot;
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(0, 0, -.7f), Quaternion.identity);
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

    IEnumerator Point(Vector2 position)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null; yield return null;
    }

    void OpenCover()
    {
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        Physics.SyncTransforms();
    }

    Vector2 MotorPoint()
    {
        foreach (var collider in robot.InstalledDrive.GetComponentsInChildren<Collider>())
        {
            Vector2 point = player.View.WorldToScreenPoint(collider.bounds.center);
            if (Physics.Raycast(player.View.ScreenPointToRay(point), out var hit, 3f,
                ~(1 << player.gameObject.layer), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<RepairPart>() == robot.InstalledDrive)
                return point;
        }
        Assert.Fail("The exposed drive must be reachable for inspection.");
        return Vector2.zero;
    }

    [UnityTest]
    public IEnumerator NewOrderShowsSymptomAndInspectionGoalWithoutRevealingTheRepair()
    {
        yield return EnterOrder();
        yield return Point(new Vector2(Screen.width - 30f, Screen.height * .5f));
        Assert.That(TextNamed("Symptom").text, Does.Contain(session.ActiveOrder.Symptom));
        string goal = TextNamed("AssemblyState").text;
        Assert.That(goal, Does.Contain("Причина пока неизвестна"));
        Assert.That(goal, Does.Not.Contain("Замените"));
        Assert.That(TextNamed("Hint").text.Split('\n').Length, Is.LessThanOrEqualTo(3),
            "Idle help must not dump every key and every tool instruction.");
    }

    [UnityTest]
    public IEnumerator FindingRequiresAnExposedObservedDriveAndPersistsUntilRestart()
    {
        yield return EnterOrder();
        yield return Point(player.View.WorldToScreenPoint(robot.InstalledDrive.transform.position));
        yield return new WaitForSeconds(.7f);
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("НАЙДЕНО"));
        OpenCover();
        yield return Point(MotorPoint());
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("НАЙДЕНО"),
            "A fleeting cursor crossing is not an inspection.");
        yield return new WaitForSeconds(.7f);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("НАЙДЕНО"));
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("повреждён"));
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("НАЙДЕНО"));
        interaction.ResetRepair();
        yield return null;
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("НАЙДЕНО"));
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("Причина пока неизвестна"));
    }

    [UnityTest]
    public IEnumerator BlockingUiCannotDiscoverTheHiddenDrive()
    {
        yield return EnterOrder();
        OpenCover();
        Vector2 point = MotorPoint();
        var overlay = new GameObject("DiagnosisBlockingUI", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return Point(point);
        yield return new WaitForSeconds(.7f);
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("НАЙДЕНО"));
        Object.Destroy(overlay);
        yield return Point(point);
        yield return new WaitForSeconds(.7f);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("НАЙДЕНО"));
    }

    [UnityTest]
    public IEnumerator MaintenanceGuidanceFollowsVisibleSurfacesAndDoesNotConfuseTestWithDelivery()
    {
        yield return EnterOrder(true);
        Assert.That(robot.RunTest(out _), Is.True);
        while (robot.IsTesting) yield return null;
        yield return null;
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("ГОТОВ К ВЫДАЧЕ"));
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("90%"));
        Assert.That(TextNamed("Symptom").preferredHeight,
            Is.LessThanOrEqualTo(TextNamed("Symptom").rectTransform.rect.height + 1f));
        Assert.That(TextNamed("AssemblyState").preferredHeight,
            Is.LessThanOrEqualTo(TextNamed("AssemblyState").rectTransform.rect.height + 1f));
        OpenCover();
        foreach (var patch in robot.DirtPatches)
            if (patch.RequiresOpenCover || patch.GetComponentInParent<RepairPart>() != robot.Cover)
                patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        yield return null;
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("Верните крышку"),
            "The outer side faces down in the tray; put the lid back before cleaning it.");
    }

    [UnityTest]
    public IEnumerator UnknownDriveNameDoesNotRevealFaultWhenPickedUpOrFocused()
    {
        yield return EnterOrder();
        OpenCover();
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        foreach (var screw in robot.InstalledDrive.Screws) robot.Turn(screw, -10000f);
        Vector2 point = MotorPoint();
        yield return Point(point);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return null; yield return null;
        Assert.That(interaction.HeldPart, Is.Not.Null);
        Assert.That(TextNamed("Hint").text, Does.Not.Contain("Неисправный"));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return null; yield return null;
        yield return Press(Key.F);
        Assert.That(TextNamed("Symptom").text, Does.Not.Contain("Неисправный"));
        Assert.That(robot.DriveFaultFound, Is.False);
    }

    [UnityTest]
    public IEnumerator InspectingHealthyReplacementDoesNotPreventFindingOriginalFault()
    {
        yield return EnterOrder();
        OpenCover();
        var original = robot.InstalledDrive;
        var replacement = robot.GetComponentsInChildren<RepairPart>(true)
            .Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
        Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        foreach (var screw in original.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(original, false, out _), Is.True);
        Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
        Physics.SyncTransforms();
        yield return Point(MotorPoint());
        yield return new WaitForSeconds(.7f);
        Assert.That(robot.InspectedDrive, Is.EqualTo(replacement));
        Assert.That(robot.DriveFaultFound, Is.False);
        Assert.That(robot.TryPlace(replacement, false, out _), Is.True);
        Assert.That(robot.TryPlace(original, true, out _), Is.True);
        Physics.SyncTransforms();
        yield return Point(MotorPoint());
        yield return new WaitForSeconds(.7f);
        Assert.That(robot.DriveFaultFound, Is.True);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("НАЙДЕНО"));
    }

    [UnityTest]
    public IEnumerator TargetAdviceAndSelectedToolStayReadableInFocusedView()
    {
        yield return EnterOrder();
        yield return Point(player.View.WorldToScreenPoint(robot.Cover.Screws[0].transform.position));
        Assert.That(TextNamed("Hint").text, Does.Contain("ВИНТ КРЫШКИ"));
        Assert.That(TextNamed("Hint").text, Does.Contain("2 — отвёртка"));
        yield return Press(Key.Digit2);
        Assert.That(TextNamed("ToolShortcuts").text, Does.Contain("[2  Отвёртка]"));
        Assert.That(TextNamed("Hint").text, Does.Contain("колесо"));
        yield return Press(Key.F);
        yield return new WaitForSeconds(.7f);
        Assert.That(TextNamed("AssemblyState").gameObject.activeSelf, Is.True);
        Assert.That(TextNamed("AssemblyState").preferredHeight,
            Is.LessThanOrEqualTo(TextNamed("AssemblyState").rectTransform.rect.height + 1f));
        Assert.That(TextNamed("ToolShortcuts").raycastTarget, Is.False);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("DiagnosisCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
