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
    OrderDefinition independentOrder;

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

    IEnumerator EnterOrder(bool cleaning = false, bool independent = false, bool contact = false)
    {
        var orders = (OrderDefinition[])typeof(WorkshopSession).GetField("orders",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        int repairIndex = System.Array.FindIndex(orders, o => o.RequiresCleaning == cleaning && o.RequiresSolderingStation == contact);
        if (contact)
        {
            typeof(WorkshopSession).GetField("<Balance>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, WorkshopSession.SolderingStationPrice);
            Assert.That(session.TryBuySolderingStation(out _), Is.True);
        }
        if (independent)
        {
            orders = (OrderDefinition[])orders.Clone();
            independentOrder = Object.Instantiate(orders[repairIndex]);
            typeof(OrderDefinition).GetField("independentDiagnosis", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(independentOrder, true);
            orders[repairIndex] = independentOrder;
            typeof(WorkshopSession).GetField("orders", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, orders);
        }
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

    IEnumerator ClickButton(string name)
    {
        var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == name);
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
        yield return null; yield return null;
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
    public IEnumerator LookingAtDriveRecordsExteriorWithoutRevealingInternalFault()
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
        Assert.That(robot.HasInspectedDrive, Is.True);
        Assert.That(robot.DriveFaultFound, Is.False, "Hover cannot reveal an internal mechanical defect.");
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("видимых повреждений"));
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("видимых повреждений"));
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
        Assert.That(robot.HasInspectedDrive, Is.True);
        Assert.That(robot.DriveFaultFound, Is.False);
    }

    [UnityTest]
    public IEnumerator MaintenanceGuidanceFollowsVisibleSurfacesAndDoesNotConfuseTestWithDelivery()
    {
        yield return EnterOrder(true);
        Assert.That(robot.RunTest(out _), Is.True);
        yield return null;
        var toolButton = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "ToolSlot2");
        Assert.That(toolButton.interactable, Is.False);
        yield return ClickButton("ToolSlot2");
        Assert.That(interaction.ScrewdriverSelected, Is.False, "The test must block tool selection through UI too.");
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
        Assert.That(robot.DriveFaultFound, Is.False);
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("видимых повреждений"));
    }

    [UnityTest]
    public IEnumerator ContextCardFitsScreenAvoidsWorkPointAndHidesWhenIdle()
    {
        Assert.That(TextNamed("Title").gameObject.activeInHierarchy, Is.False);
        Assert.That(TextNamed("ToolShortcuts").gameObject.activeInHierarchy, Is.False);
        yield return EnterOrder();
        Vector2 point = player.View.WorldToScreenPoint(robot.Cover.Screws[0].transform.position);
        yield return Point(point);
        yield return new WaitForSeconds(.3f);
        var panel = TextNamed("Hint").transform.parent.GetComponent<RectTransform>();
        var group = panel.GetComponent<CanvasGroup>();
        Assert.That(group.alpha, Is.GreaterThan(.9f));
        Assert.That(group.blocksRaycasts, Is.False);
        Assert.That(panel.rect.width, Is.LessThanOrEqualTo(380f));
        var corners = new Vector3[4]; panel.GetWorldCorners(corners);
        Rect screenRect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        Assert.That(screenRect.xMin, Is.GreaterThanOrEqualTo(0f));
        Assert.That(screenRect.yMin, Is.GreaterThanOrEqualTo(0f));
        Assert.That(screenRect.xMax, Is.LessThanOrEqualTo(Screen.width));
        Assert.That(screenRect.yMax, Is.LessThanOrEqualTo(Screen.height));
        Assert.That(screenRect.Contains(point), Is.False, "The card must leave the working point clear.");
        yield return Point(new Vector2(Screen.width - 30f, Screen.height * .5f));
        yield return new WaitForSeconds(.25f);
        Assert.That(group.alpha, Is.EqualTo(0f));
        Assert.That(TextNamed("Hint").text, Is.Empty);
    }

    [UnityTest]
    public IEnumerator OrderDetailsCollapseAndContextualHelpCanBeOpenedAndClosed()
    {
        yield return EnterOrder(true);
        yield return ClickButton("ToolSlot2");
        Assert.That(interaction.ScrewdriverSelected, Is.True);
        yield return ClickButton("ToolSlot3");
        Assert.That(interaction.BrushSelected, Is.True);
        yield return ClickButton("ToolSlot1");
        Assert.That(interaction.BrushSelected || interaction.ScrewdriverSelected, Is.False);
        yield return ClickButton("CollapseOrderButton");
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("СЕЙЧАС"));
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("Крышка снаружи:"));
        Assert.That(TextNamed("Symptom").text, Does.Contain("Обслуживание"));
        yield return Press(Key.F1);
        Assert.That(TextNamed("HelpText").gameObject.activeInHierarchy, Is.True);
        yield return Press(Key.F1);
        Assert.That(TextNamed("HelpText").gameObject.activeInHierarchy, Is.False);
        yield return Press(Key.Escape);
        Assert.That(TextNamed("ToolShortcuts").gameObject.activeInHierarchy, Is.False);
        Assert.That(TextNamed("CameraHints").gameObject.activeInHierarchy, Is.False);
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
        yield return Point(player.View.WorldToScreenPoint(robot.Cover.transform.position));
        Assert.That(TextNamed("CameraHints").text, Does.Contain("Повернуть предмет"));
        Assert.That(TextNamed("CameraHints").text, Does.Contain("Выровнять"));
        Assert.That(TextNamed("Hint").text, Is.Empty, "Inspection must not cover the object with a duplicate tooltip.");
        Assert.That(TextNamed("Hint").GetComponentInParent<CanvasGroup>().alpha, Is.Zero);
        Assert.That(TextNamed("ToolShortcuts").text, Does.Contain("[1  Рука]"));
        Assert.That(TextNamed("AssemblyState").gameObject.activeSelf, Is.True);
        Assert.That(TextNamed("AssemblyState").preferredHeight,
            Is.LessThanOrEqualTo(TextNamed("AssemblyState").rectTransform.rect.height + 1f));
        Assert.That(TextNamed("ToolShortcuts").raycastTarget, Is.False);
    }

    [UnityTest]
    public IEnumerator IndependentDiagnosisNeverRevealsRecipeWithoutThreeExplicitRequests()
    {
        yield return EnterOrder(independent: true);
        Assert.That(session.ActiveOrder.Requirements, Does.Not.Contain("контакт"));
        OpenCover();
        yield return Point(MotorPoint());
        yield return new WaitForSeconds(.7f);
        string goal = TextNamed("AssemblyState").text;
        Assert.That(goal, Does.Contain("Найдите причину"));
        Assert.That(goal, Does.Not.Contain("освободите оба винта"));
        Assert.That(goal, Does.Not.Contain("повреждён привод"));
        Assert.That(TextNamed("Symptom").text, Does.Not.Contain(session.ActiveOrder.OrderId));
        yield return Press(Key.H);
        Assert.That(TextNamed("DiagnosisText").gameObject.activeInHierarchy, Is.True);
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("Плата → контакт питания"));
        Assert.That(TextNamed("DiagnosisText").text, Does.Not.Contain("Решение:"));
        yield return ClickButton("DiagnosisHelpButton");
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("Куда смотреть:"));
        yield return ClickButton("DiagnosisHelpButton");
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("Что проверить:"));
        Assert.That(TextNamed("DiagnosisText").text, Does.Not.Contain("Решение:"));
        yield return Press(Key.Escape);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(TextNamed("DiagnosisText").gameObject.activeInHierarchy, Is.False);
        yield return Press(Key.H);
        yield return ClickButton("DiagnosisHelpButton");
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("Решение:"));
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("установите запасной"));
        Assert.That(TextNamed("DiagnosisText").preferredHeight,
            Is.LessThanOrEqualTo(TextNamed("DiagnosisText").rectTransform.rect.height + 1f));
        yield return Press(Key.H);
        interaction.ResetRepair();
        yield return null;
        yield return Press(Key.H);
        Assert.That(TextNamed("DiagnosisText").text, Does.Not.Contain("Решение:"));
    }

    [UnityTest]
    public IEnumerator IndependentContactObservationDoesNotChooseSolderingToolForPlayer()
    {
        yield return EnterOrder(independent: true, contact: true);
        Assert.That(session.ActiveOrder.Requirements, Does.Not.Contain("Восстановить контакт"));
        OpenCover();
        yield return Point(player.View.WorldToScreenPoint(robot.SolderJoint.PointOnSeam(.5f)));
        yield return new WaitForSeconds(2.4f);
        Assert.That(robot.JointFaultFound, Is.True, "The visible seam must still yield a fact.");
        Assert.That(TextNamed("AssemblyState").text, Does.Contain("виден разрыв"));
        Assert.That(TextNamed("AssemblyState").text, Does.Not.Contain("паяльник"));
        Assert.That(TextNamed("Hint").text, Does.Not.Contain("4 —"));
        Assert.That(TextNamed("Hint").text, Does.Not.Contain("паяльник"));
        yield return Press(Key.H);
        yield return ClickButton("DiagnosisHelpButton");
        yield return ClickButton("DiagnosisHelpButton");
        Assert.That(TextNamed("DiagnosisText").text, Does.Not.Contain("Решение:"));
        yield return ClickButton("DiagnosisHelpButton");
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("Решение:"));
        Assert.That(TextNamed("DiagnosisText").text, Does.Contain("паяльником"));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("DiagnosisCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        if (independentOrder != null) Object.Destroy(independentOrder);
        fixture.TearDown();
    }
}
