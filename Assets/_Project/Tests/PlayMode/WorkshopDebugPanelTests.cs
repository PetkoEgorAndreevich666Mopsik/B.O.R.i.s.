using System.Collections;
using System.IO;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class WorkshopDebugPanelTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    WorkshopDebugPanel panel;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        yield return null;
        yield return null;
        session = Object.FindFirstObjectByType<WorkshopSession>();
        panel = session.GetComponent<WorkshopDebugPanel>();
        Assert.That(panel, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator PresetsSetProgressEquipmentAndBothSaveCopies()
    {
        int[] balances = { 0, 100, 40, 220, 370 };
        string[] orders = { "A-001", "B-001", "C", "D-001", "E-001" };
        var store = new WorkshopProgressStore(session.SavePath);
        Assert.That(store.TrySave(NewGameProgressTests.OldProgress(), out _), Is.True);
        for (int i = 0; i < orders.Length; i++)
        {
            Assert.That(session.TrySaveDebugCheckpoint(i, out string reason), Is.True, reason);
            Assert.That(store.TryLoad(out var data, out reason), Is.True, reason);
            Assert.That(data.nextOrder, Is.EqualTo(orders[i]));
            Assert.That(data.balance, Is.EqualTo(balances[i]));
            Assert.That(data.completedOrders, Is.EqualTo(i));
            Assert.That(data.hasSolderingStation, Is.EqualTo(i >= 2));
            Assert.That(data.activeOrder, Is.Null.Or.Empty);
            Assert.That(data.activeInstance, Is.Null.Or.Empty);
            Assert.That(data.lastDeliveredInstance, Is.Null.Or.Empty);
            Assert.That(File.ReadAllText(session.SavePath + ".bak"), Is.EqualTo(File.ReadAllText(session.SavePath)));
        }
        File.WriteAllText(session.SavePath, "broken");
        Assert.That(store.TryLoad(out var recovered, out _), Is.True);
        Assert.That(recovered.nextOrder, Is.EqualTo("E-001"));
        Assert.That(recovered.balance, Is.EqualTo(370));
        yield return null;
    }

    [UnityTest]
    public IEnumerator InvalidPresetDoesNotChangeSave()
    {
        var store = new WorkshopProgressStore(session.SavePath);
        Assert.That(store.TrySave(NewGameProgressTests.OldProgress(), out _), Is.True);
        string original = File.ReadAllText(session.SavePath);
        Assert.That(session.TrySaveDebugCheckpoint(-1, out _), Is.False);
        Assert.That(session.TrySaveDebugCheckpoint(5, out _), Is.False);
        Assert.That(File.ReadAllText(session.SavePath), Is.EqualTo(original));
        yield return null;
    }

    [UnityTest]
    public IEnumerator LockedSaveKeepsPanelOpenAndDoesNotReloadScene()
    {
        var store = new WorkshopProgressStore(session.SavePath);
        Assert.That(store.TrySave(NewGameProgressTests.OldProgress(), out _), Is.True);
        string original = File.ReadAllText(session.SavePath);
        var scene = SceneManager.GetActiveScene().handle;
        panel.SetOpen(true);
        using (new FileStream(session.SavePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.That(panel.JumpToOrder(2), Is.False);
        yield return null;
        Assert.That(panel.IsOpen, Is.True);
        Assert.That(panel.Status, Does.Contain("сбросить"));
        Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene));
        Assert.That(File.ReadAllText(session.SavePath), Is.EqualTo(original));
    }

    [UnityTest]
    public IEnumerator F8CancelsCarryBlocksWalkingAndRestoresControlsOnClose()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Assert.That(session.Transport.TryPickUp(out _), Is.True);
        InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F8));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Assert.That(panel.IsOpen, Is.True);
        Assert.That(session.Transport.IsCarrying, Is.False);
        var interaction = Object.FindFirstObjectByType<InteractionController>(FindObjectsInactive.Include);
        Assert.That(interaction.enabled, Is.False);
        Assert.That(Cursor.visible, Is.True);
        Vector3 position = interaction.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.1f);
        Assert.That(Vector3.Distance(position, interaction.transform.position), Is.LessThan(.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Assert.That(panel.IsOpen, Is.False);
        Assert.That(interaction.enabled, Is.True);
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.Locked));
    }

    IEnumerator ClickOrder(int number)
    {
        panel.SetOpen(true);
        var button = GameObject.Find("DeveloperOrder" + number).GetComponent<Button>();
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return null;
        var previousScene = SceneManager.GetActiveScene().handle;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        float deadline = Time.realtimeSinceStartup + 15f;
        while (SceneManager.GetActiveScene().handle == previousScene && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(SceneManager.GetActiveScene().handle, Is.Not.EqualTo(previousScene));
        yield return null; yield return null;
        session = Object.FindFirstObjectByType<WorkshopSession>();
        panel = session.GetComponent<WorkshopDebugPanel>();
    }

    [UnityTest]
    public IEnumerator RealButtonsJumpToSolderingAndThenToNewGame()
    {
        yield return ClickOrder(3);
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("C"));
        Assert.That(session.HasSolderingStation, Is.True);
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(session.CompletedOrders, Is.EqualTo(2));
        Assert.That(session.ActiveOrder, Is.Null);
        float deadline = Time.realtimeSinceStartup + 8f;
        while (session.Window.Phase != ServiceWindowPhase.AwaitingConversation && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(session.VisitorRobot.SolderJoint.IsRepaired, Is.False);
        Assert.That(panel.IsOpen, Is.False);
        yield return ClickOrder(1);
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
        Assert.That(session.HasSolderingStation, Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(Object.FindObjectsByType<WorkshopDebugPanel>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator RestartRestoresCurrentOrderWithoutChangingItsContractOrMoney()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        var bench = Object.FindFirstObjectByType<Workbench>();
        bench.PositionRobot(null);
        session.ActiveRobot.Turn(session.ActiveRobot.Cover.Screws[0], -180f);
        string instance = session.ActiveOrderId;
        var scene = SceneManager.GetActiveScene().handle;
        Assert.That(panel.RestartOrder(), Is.True);
        float deadline = Time.realtimeSinceStartup + 15f;
        while (SceneManager.GetActiveScene().handle == scene && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null; yield return null;
        session = Object.FindFirstObjectByType<WorkshopSession>();
        Assert.That(session.ActiveOrderId, Is.EqualTo(instance));
        Assert.That(session.ActiveOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.ActiveRobot.Cover.Screws[0].IsTight, Is.True);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("DebugPanelCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
