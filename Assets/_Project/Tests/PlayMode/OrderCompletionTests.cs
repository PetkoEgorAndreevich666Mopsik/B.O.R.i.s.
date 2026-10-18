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

public sealed class OrderCompletionTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    Workbench workbench;
    PlayerController player;
    InteractionController interaction;
    OrderDefinition temporaryOrder;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindAnyObjectByType<WorkshopSession>();
        workbench = Object.FindAnyObjectByType<Workbench>();
        player = Object.FindAnyObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        yield return null;
        yield return null;
    }

    static void SetField(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    void Repair(bool cleanRequired = true)
    {
        RobotRepair robot = session.ActiveRobot;
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        if (!robot.IsFunctional)
        {
            Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
            RepairPart oldDrive = robot.InstalledDrive;
            foreach (var screw in oldDrive.Screws) robot.Turn(screw, -10000f);
            Assert.That(robot.TryPlace(oldDrive, false, out _), Is.True);
            RepairPart replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
            Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
            foreach (var screw in replacement.Screws) robot.Turn(screw, 10000f);
            Assert.That(robot.ToggleConnection(robot.Connector, out _), Is.True);
        }
        if (cleanRequired && session.ActiveOrder.RequiresCleaning)
            foreach (DirtPatch patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
    }

    IEnumerator TestRobot()
    {
        Assert.That(session.ActiveRobot.RunTest(out string reason), Is.True, reason);
        float deadline = Time.realtimeSinceStartup + 4f;
        while (session.ActiveRobot.IsTesting && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(session.ActiveRobot.TestResult, Is.EqualTo(RobotTestResult.Passed));
    }

    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    IEnumerator OpenDesk()
    {
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(-2.6f, 0, -1.9f), Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
        yield return Press(Key.E);
        Assert.That(interaction.IsAtOrderDesk, Is.True);
    }

    IEnumerator Click(string name)
    {
        Button button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == name);
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator TwoOrdersUseRealDeskButtonsAndShowReceiptBeforeNextAcceptance()
    {
        string previousInstance = null;
        string firstTemplate = session.OfferedOrder.OrderId;
        int expectedBalance = 0;
        for (int i = 0; i < 2; i++)
        {
            yield return OpenDesk();
            yield return Click("AcceptOrderButton");
            Assert.That(session.ActiveOrderId, Is.Not.EqualTo(previousInstance));
            previousInstance = session.ActiveOrderId;
            int reward = session.ActiveOrderReward;
            yield return Press(Key.Escape);
            Repair();
            yield return TestRobot();
            Assert.That(session.Balance, Is.EqualTo(expectedBalance), "A passed test must not pay.");
            yield return OpenDesk();
            yield return Click("DeliverOrderButton");
            expectedBalance += reward;
            Assert.That(session.Balance, Is.EqualTo(expectedBalance));
            Assert.That(session.CompletedOrders, Is.EqualTo(i + 1));
            Assert.That(session.ActiveOrder, Is.Null);
            Assert.That(workbench.HasRobot, Is.False);
            var card = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(t => t.name == "OrderDescription");
            Assert.That(card.text, Does.Contain("ВЫДАН").And.Contain("+" + reward));
            var progress = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(t => t.name == "WorkshopProgress");
            Assert.That(progress.text, Does.Contain(expectedBalance.ToString()));
            yield return Click("AcceptOrderButton");
            Assert.That(session.ActiveOrder, Is.Null, "Next-order button previews; it must not accept automatically.");
            Assert.That(card.text, Does.Contain(session.OfferedOrder.ClientName));
            Assert.That(session.Balance, Is.EqualTo(expectedBalance));
            if (i == 0) Assert.That(session.OfferedOrder.OrderId, Is.Not.EqualTo(firstTemplate));
            yield return Press(Key.Escape);
        }
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo(firstTemplate), "The two available jobs repeat.");
    }

    [UnityTest]
    public IEnumerator UnfinishedAndRunningTestsCannotPay_AndResetDoesNotFinishOrder()
    {
        Assert.That(session.TryDeliverOrder(null, out _), Is.False);
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string id = session.ActiveOrderId;
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
        Repair();
        Assert.That(session.TryDeliverOrder(id, out _), Is.False, "Correct assembly without a test is insufficient.");
        Assert.That(session.ActiveRobot.RunTest(out _), Is.True);
        Assert.That(session.TryDeliverOrder(id, out _), Is.False, "Wait for the test animation to finish.");
        session.ActiveRobot.CancelTest();
        yield return TestRobot();
        Assert.That(session.CanDeliverOrder(out _), Is.True);
        workbench.ResetRobot();
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
        Assert.That(session.ActiveOrderId, Is.EqualTo(id));
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
    }

    [UnityTest]
    public IEnumerator OldSubmissionCannotPayTwiceOrDeliverAnotherInstance()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string first = session.ActiveOrderId;
        Repair();
        yield return TestRobot();
        int reward = session.ActiveOrderReward;
        Assert.That(session.TryDeliverOrder(first, out _), Is.True);
        Assert.That(session.TryDeliverOrder(first, out _), Is.False);
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string second = session.ActiveOrderId;
        Repair();
        yield return TestRobot();
        Assert.That(session.TryDeliverOrder(first, out _), Is.False);
        Assert.That(session.ActiveOrderId, Is.EqualTo(second));
        Assert.That(session.Balance, Is.EqualTo(reward));
        Assert.That(session.TryDeliverOrder(second, out _), Is.True);
        Assert.That(session.CompletedOrders, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator CurrentAssemblyAndOccupiedWorkbenchBlockDelivery()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Repair();
        yield return TestRobot();
        Assert.That(workbench.Enter(player.View), Is.True);
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.False);
        Assert.That(session.ActiveRobot, Is.Not.Null);
        workbench.Exit();
        var screw = session.ActiveRobot.Cover.Screws[0];
        session.ActiveRobot.Turn(screw, -90f);
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.False);
        Assert.That(session.Balance, Is.Zero);
        session.ActiveRobot.Turn(screw, 10000f);
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.False, "Reassembly does not restore an invalidated test.");
        yield return TestRobot();
        Assert.That(session.CanDeliverOrder(out _), Is.True);
    }

    [UnityTest]
    public IEnumerator CleaningRequirementIsLiveAndRewardIsAgreedAtAcceptance()
    {
        temporaryOrder = Object.Instantiate(session.OfferedOrder);
        SetField(temporaryOrder, "requiresCleaning", true);
        SetField(session, "orders", new[] { temporaryOrder });
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        int reward = session.ActiveOrderReward;
        SetField(temporaryOrder, "reward", reward + 1000);
        Repair(false);
        yield return TestRobot();
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out string reason), Is.False);
        Assert.That(reason, Does.Contain("очистку"));
        foreach (var patch in session.ActiveRobot.GetComponentsInChildren<DirtPatch>())
            patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.True);
        Assert.That(session.Balance, Is.EqualTo(reward), "The accepted reward does not change with the template.");
    }

    [UnityTest]
    public IEnumerator BalanceOverflowKeepsOrderAndRobotUntouched()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Repair();
        yield return TestRobot();
        typeof(WorkshopSession).GetProperty("Balance").SetValue(session, int.MaxValue - 1);
        string id = session.ActiveOrderId;
        RobotRepair robot = session.ActiveRobot;
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
        Assert.That(session.Balance, Is.EqualTo(int.MaxValue - 1));
        Assert.That(session.ActiveOrderId, Is.EqualTo(id));
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        Assert.That(session.CompletedOrders, Is.Zero);
    }

    [UnityTest]
    public IEnumerator MaintenanceStartsWorkingButEverySurfaceMustBeCleanEvenAfterPassingTest()
    {
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        RobotRepair robot = session.ActiveRobot;
        RepairPart originalDrive = robot.InstalledDrive;
        Assert.That(session.ActiveOrder.RequiresCleaning, Is.True);
        Assert.That(robot.IsFunctional, Is.True);
        Assert.That(originalDrive.Faulty, Is.False);
        Assert.That(robot.DirtPatches.Length, Is.EqualTo(3));
        yield return TestRobot();
        Assert.That(session.CanDeliverOrder(out string reason), Is.False);
        Assert.That(reason, Does.Contain("очистку"));
        Assert.That(robot.CanClean(robot.DirtPatches.First(p => p.RequiresOpenCover), out _), Is.False);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        for (int i = 0; i < robot.DirtPatches.Length; i++)
        {
            DirtPatch patch = robot.DirtPatches[i];
            Assert.That(robot.CanClean(patch, out _), Is.True);
            patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
            Assert.That(patch.IsClean, Is.True);
            Assert.That(robot.CheckCleaning(out _), Is.EqualTo(i == robot.DirtPatches.Length - 1));
        }
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(session.CanDeliverOrder(out _), Is.False, "Cleaning and reassembly cannot restore the previous test.");
        yield return TestRobot();
        Assert.That(robot.InstalledDrive, Is.SameAs(originalDrive), "Maintenance must not require a replacement.");
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.True);
        Assert.That(session.Balance, Is.EqualTo(100));
    }

    [UnityTest]
    public IEnumerator DriveOrderNeedsReplacementButNotCleaningAndNextMaintenanceHasFreshDirt()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        DirtPatch firstPatch = session.ActiveRobot.DirtPatches[0];
        Texture2D previousMask = firstPatch.Mask;
        Repair();
        yield return TestRobot();
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.True);
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Assert.That(session.ActiveOrder.OrderId, Is.EqualTo("B-001"));
        Assert.That(session.ActiveOrder.RequiresCleaning, Is.False);
        Assert.That(session.ActiveRobot.IsFunctional, Is.False);
        Assert.That(session.ActiveRobot.InstalledDrive.Faulty, Is.True);
        Assert.That(session.ActiveRobot.DirtPatches, Is.Empty);
        Assert.That(session.ActiveRobot.GetComponentsInChildren<DirtPatch>(true), Is.Empty,
            "The drive order must contain no dirt surfaces, including hidden ones.");
        var originalRobot = session.ActiveRobot;
        workbench.ResetRobot();
        yield return null;
        Assert.That(session.ActiveRobot, Is.Not.SameAs(originalRobot));
        Assert.That(session.ActiveRobot.GetComponentsInChildren<DirtPatch>(true), Is.Empty,
            "Restarting B must keep the clean robot configuration.");
        Assert.That(session.CanDeliverOrder(out _), Is.False);
        Repair(false);
        Assert.That(session.ActiveRobot.DirtPatches, Is.Empty);
        yield return TestRobot();
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out _), Is.True);
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Assert.That(session.ActiveOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.ActiveRobot.IsFunctional, Is.True);
        Assert.That(session.ActiveRobot.DirtPatches.All(p => p.Cleanliness == 0f), Is.True);
        Assert.That(session.ActiveRobot.DirtPatches[0].Mask, Is.Not.SameAs(previousMask));
    }

    [UnityTest]
    public IEnumerator MaintenanceResetKeepsOrderAndRestoresHealthyDriveAndDirtySurfaces()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string id = session.ActiveOrderId;
        RobotRepair previous = session.ActiveRobot;
        previous.DirtPatches[0].CleanStroke(Vector2.zero, Vector2.one, 2f);
        workbench.ResetRobot();
        yield return null;
        Assert.That(session.ActiveOrderId, Is.EqualTo(id));
        Assert.That(session.ActiveRobot, Is.Not.SameAs(previous));
        Assert.That(session.ActiveRobot.IsFunctional, Is.True);
        Assert.That(session.ActiveRobot.DirtPatches.All(p => p.Cleanliness == 0f), Is.True);
        Assert.That(session.ActiveRobot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(session.Balance, Is.Zero);
    }

    [UnityTest]
    public IEnumerator MaintenanceHUDShowsCleaningAfterPassedTestAndProgressSurvivesVisits()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        yield return TestRobot();
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(0, 0, -.7f), Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
        yield return Press(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        var hud = Object.FindAnyObjectByType<RepairHUD>();
        Text assembly = (Text)typeof(RepairHUD).GetField("assemblyState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
        Assert.That(assembly.text, Does.Contain("Очистка ≥90%").And.Contain("0 / 3"));
        Assert.That(assembly.text, Does.Not.Contain("выдайте робота"));
        Assert.That(assembly.text, Does.Contain("СЕЙЧАС"));
        DirtPatch outer = session.ActiveRobot.DirtPatches.Single(p => !p.RequiresOpenCover);
        outer.CleanStroke(new Vector2(.2f, .5f), new Vector2(.8f, .5f), .035f);
        float progress = outer.Cleanliness;
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(outer.Cleanliness, Is.EqualTo(progress));
        Assert.That(assembly.text, Does.Contain(outer.DisplayName).And.Contain(progress.ToString("P0")));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("DeliveryCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        if (temporaryOrder != null) Object.Destroy(temporaryOrder);
        yield return null;
        fixture.TearDown();
    }
}
