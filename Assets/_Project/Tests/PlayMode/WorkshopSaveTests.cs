using System;
using System.Collections;
using System.IO;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class WorkshopSaveTests
{
    readonly InputTestFixture fixture = new();
    WorkshopSession session;
    Keyboard keyboard;
    Mouse mouse;
    string PathName => WorkshopProgressStore.DefaultPath;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return Reload();
    }

    IEnumerator Reload()
    {
        var previous = SceneManager.GetActiveScene();
        if (previous.path == "Assets/_Project/Scenes/WorkshopPrototype.unity")
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("SaveRestart"));
            yield return SceneManager.UnloadSceneAsync(previous);
            yield return null;
        }
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindAnyObjectByType<WorkshopSession>();
        yield return null; yield return null;
    }

    WorkshopProgress Data(int balance = 100) => new WorkshopProgress
    {
        version = 1, balance = balance, completedOrders = 1, nextOrder = "B-001", activeOrder = "", activeInstance = "",
        lastDeliveredInstance = Guid.NewGuid().ToString("N")
    };

    IEnumerator PreparePassingRobot()
    {
        var robot = session.ActiveRobot;
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000);
        Assert.That(robot.TryPlace(robot.Cover, false, out _), Is.True);
        if (robot.InstalledDrive.Faulty)
        {
            robot.ToggleConnection(robot.Connector, out _);
            var old = robot.InstalledDrive;
            foreach (var screw in old.Screws) robot.Turn(screw, -10000);
            Assert.That(robot.TryPlace(old, false, out _), Is.True);
            var replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
            Assert.That(robot.TryPlace(replacement, true, out _), Is.True);
            foreach (var screw in replacement.Screws) robot.Turn(screw, 10000);
        }
        if (robot.SolderJoint != null)
        {
            if (robot.Connector.IsConnected) robot.ToggleConnection(robot.Connector, out _);
            robot.SolderStroke(robot.SolderJoint, session, 0, 1);
            Assert.That(robot.SolderJoint.IsRepaired, Is.True);
        }
        if (!robot.Connector.IsConnected) robot.ToggleConnection(robot.Connector, out _);
        if (session.ActiveOrder.RequiresCleaning)
            foreach (var patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2);
        Assert.That(robot.TryPlace(robot.Cover, true, out _), Is.True);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000);
        Assert.That(robot.RunTest(out _), Is.True);
        float deadline = Time.realtimeSinceStartup + 4;
        while (robot.IsTesting && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed));
    }

    IEnumerator Complete()
    {
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
        yield return PreparePassingRobot();
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out reason), Is.True, reason);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PaidOrdersStationPendingCAndQueueSurviveRestartsWithoutDuplicatePayout()
    {
        yield return Complete();
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(100));
        Assert.That(session.CompletedOrders, Is.EqualTo(1));
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("B-001"));
        yield return Complete();
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.TryBuySolderingStation(out string reason), Is.True, reason);
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(session.HasSolderingStation, Is.True);
        Assert.That(session.CanOfferSolderingOrders, Is.True);
        Assert.That(GameObject.Find("SolderingStationInstalled"), Is.Not.Null);
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("C"));
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string activeId = session.ActiveOrderId;
        var robot = session.ActiveRobot;
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000);
        robot.TryPlace(robot.Cover, false, out _);
        robot.ToggleConnection(robot.Connector, out _);
        robot.SolderStroke(robot.SolderJoint, session, 0, .5f);
        Assert.That(robot.SolderJoint.Progress, Is.InRange(.4f, .7f));
        yield return Reload();
        Assert.That(session.ActiveOrderId, Is.EqualTo(activeId));
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(session.ActiveRobot.SolderJoint.Progress, Is.Zero);
        Assert.That(session.ActiveRobot.Cover.IsInstalled, Is.True);
        Assert.That(session.ActiveRobot.Connector.IsConnected, Is.True);
        Assert.That(session.SaveStatus, Does.Contain("с начала"));
        Assert.That(session.CanDeliverOrder(out _), Is.False);
        yield return PreparePassingRobot();
        Assert.That(session.TryDeliverOrder(activeId, out _), Is.True);
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.CompletedOrders, Is.EqualTo(3));
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.TryDeliverOrder(activeId, out _), Is.False);
        Assert.That(session.TryBuySolderingStation(out _), Is.False);
        Assert.That(session.Balance, Is.EqualTo(220));
    }

    [UnityTest]
    public IEnumerator UnfinishedMaintenanceRestartsWithSameContractAndFreshRepair()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string id = session.ActiveOrderId;
        var robot = session.ActiveRobot;
        robot.Turn(robot.Cover.Screws[0], -300);
        var patch = robot.DirtPatches.First(p => !p.RequiresOpenCover);
        patch.CleanStroke(Vector2.zero, Vector2.one, .1f);
        Assert.That(patch.Cleanliness, Is.GreaterThan(0));
        yield return Reload();
        Assert.That(session.ActiveOrderId, Is.EqualTo(id));
        Assert.That(session.ActiveOrderReward, Is.EqualTo(100));
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
        Assert.That(session.ActiveRobot.Cover.Screws.All(s => s.IsTight), Is.True);
        Assert.That(session.ActiveRobot.DirtPatches.All(p => p.Cleanliness == 0), Is.True);
        Assert.That(session.ActiveRobot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator FailedDeliveryPreservesPassingRobotAndCanRetryExactlyOnce()
    {
        session.TryAcceptOrder(out _);
        yield return PreparePassingRobot();
        var robot = session.ActiveRobot;
        string id = session.ActiveOrderId;
        string confirmed = File.ReadAllText(PathName);
        using (new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(session.TryDeliverOrder(id, out string reason), Is.False);
            Assert.That(reason, Does.Contain("Не удалось сохранить"));
            Assert.That(session.ActiveRobot, Is.SameAs(robot));
            Assert.That(robot.isActiveAndEnabled, Is.True);
            Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.Passed));
            Assert.That(session.ActiveOrderId, Is.EqualTo(id));
            Assert.That(session.Balance, Is.Zero);
            Assert.That(session.CompletedOrders, Is.Zero);
        }
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(confirmed));
        Assert.That(session.TryDeliverOrder(id, out _), Is.True);
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(100));
        Assert.That(session.CompletedOrders, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator FailedPurchasePreservesFundsOrderAndEquipmentUntilSuccessfulRetry()
    {
        yield return Complete(); yield return Complete();
        session.TryAcceptOrder(out _);
        var robot = session.ActiveRobot;
        string id = session.ActiveOrderId;
        robot.Turn(robot.Cover.Screws[0], -180);
        float tightness = robot.Cover.Screws[0].Tightness;
        string confirmed = File.ReadAllText(PathName);
        using (new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(session.TryBuySolderingStation(out _), Is.False);
            Assert.That(session.Balance, Is.EqualTo(220));
            Assert.That(session.HasSolderingStation, Is.False);
            Assert.That(session.ActiveOrderId, Is.EqualTo(id));
            Assert.That(session.ActiveRobot, Is.SameAs(robot));
            Assert.That(robot.Cover.Screws[0].Tightness, Is.EqualTo(tightness));
        }
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(confirmed));
        Assert.That(session.TryBuySolderingStation(out _), Is.True);
        Assert.That(session.Balance, Is.EqualTo(40));
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(session.HasSolderingStation, Is.True);
        Assert.That(session.ActiveOrderId, Is.EqualTo(id));
    }

    [UnityTest]
    public IEnumerator FailedAcceptanceLeavesEmptyBenchAndCanRetry()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName));
        Directory.CreateDirectory(PathName + ".tmp");
        Assert.That(session.TryAcceptOrder(out _), Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(session.ActiveRobot, Is.Null);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(File.Exists(PathName), Is.False);
        Directory.Delete(PathName + ".tmp");
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        yield return Reload();
        Assert.That(session.ActiveOrder, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator BackupRecoversConfirmedSnapshotAndNeverLoadsIncompleteTemp()
    {
        var store = new WorkshopProgressStore(PathName);
        Assert.That(store.TrySave(Data(100), out _), Is.True);
        Assert.That(store.TrySave(Data(220), out _), Is.True);
        File.WriteAllText(PathName + ".tmp", "{ unfinished");
        Assert.That(store.TryLoad(out var data, out _), Is.True);
        Assert.That(data.balance, Is.EqualTo(220));
        File.WriteAllText(PathName, "{ corrupted");
        Assert.That(store.TryLoad(out data, out string message), Is.True);
        Assert.That(data.balance, Is.EqualTo(100));
        Assert.That(message, Does.Contain("резервная"));
        Assert.That(store.TrySave(Data(120), out _), Is.True);
        Assert.That(JsonUtility.FromJson<WorkshopProgress>(File.ReadAllText(PathName + ".bak")).balance, Is.EqualTo(100));
        yield return Reload();
        Assert.That(session.Balance, Is.EqualTo(120));
    }

    [UnityTest]
    public IEnumerator CorruptSaveBlocksTransactionsAndDeskOffersLoadRetry()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName));
        File.WriteAllText(PathName, "{ corrupted");
        yield return Reload();
        Assert.That(session.IsProgressReady, Is.False);
        Assert.That(session.ActiveRobot, Is.Null, "A load error must not expose the scene preview as an unpaid repair.");
        Assert.That(session.TryAcceptOrder(out _), Is.False);
        Assert.That(session.TryBuySolderingStation(out _), Is.False);
        Assert.That(File.ReadAllText(PathName), Is.EqualTo("{ corrupted"));
        var interaction = Object.FindAnyObjectByType<InteractionController>();
        var body = interaction.GetComponent<CharacterController>();
        body.enabled = false;
        interaction.transform.SetPositionAndRotation(new Vector3(-2.6f, 0, -1.9f), Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
        Assert.That(interaction.IsAtOrderDesk, Is.True);
        var button = Object.FindObjectsByType<Button>().Single(b => b.name == "AcceptOrderButton");
        Assert.That(button.GetComponentInChildren<Text>().text, Does.Contain("загрузку"));
        File.WriteAllText(PathName, JsonUtility.ToJson(Data()));
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return null; yield return null;
        Assert.That(session.IsProgressReady, Is.True);
        Assert.That(session.Balance, Is.EqualTo(100));
        Assert.That(session.ActiveOrder, Is.Null, "Retrying a load must not silently accept a new order.");
    }

    [UnityTest]
    public IEnumerator FutureVersionCannotFallBackOrOverwriteAndInvalidDataIsRejected()
    {
        var store = new WorkshopProgressStore(PathName);
        var valid = Data();
        store.TrySave(valid, out _);
        store.TrySave(Data(220), out _);
        var future = Data(500); future.version = 2;
        string futureJson = JsonUtility.ToJson(future);
        File.WriteAllText(PathName, futureJson);
        yield return Reload();
        Assert.That(session.IsProgressReady, Is.False);
        Assert.That(session.TryAcceptOrder(out _), Is.False);
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(futureJson));
        valid.balance = -1;
        Assert.That(store.TrySave(valid, out _), Is.False);
        valid.balance = 0; valid.activeOrder = "A-001"; valid.activeInstance = "not-a-guid";
        Assert.That(store.TrySave(valid, out _), Is.False);
        valid.activeInstance = Guid.NewGuid().ToString("N"); valid.lastDeliveredInstance = valid.activeInstance;
        Assert.That(store.TrySave(valid, out _), Is.False);
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(futureJson));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("SaveCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
