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

public sealed class WorkshopProgressTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    InteractionController interaction;
    OrderDefinition solderingOrder;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindFirstObjectByType<WorkshopSession>();
        interaction = Object.FindFirstObjectByType<InteractionController>();
        yield return null;
        yield return null;
    }

    static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    IEnumerator CompleteOrder()
    {
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
        RobotRepair robot = session.ActiveRobot;
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(robot.Cover, false, out reason), Is.True, reason);
        if (!robot.IsFunctional)
        {
            Assert.That(robot.ToggleConnection(robot.Connector, out reason), Is.True, reason);
            RepairPart old = robot.InstalledDrive;
            foreach (var screw in old.Screws) robot.Turn(screw, -10000f);
            Assert.That(robot.TryPlace(old, false, out reason), Is.True, reason);
            RepairPart replacement = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.Faulty);
            Assert.That(robot.TryPlace(replacement, true, out reason), Is.True, reason);
            foreach (var screw in replacement.Screws) robot.Turn(screw, 10000f);
            Assert.That(robot.ToggleConnection(robot.Connector, out reason), Is.True, reason);
        }
        if (session.ActiveOrder.RequiresCleaning)
            foreach (var patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        Assert.That(robot.TryPlace(robot.Cover, true, out reason), Is.True, reason);
        foreach (var screw in robot.Cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(robot.RunTest(out reason), Is.True, reason);
        float deadline = Time.realtimeSinceStartup + 4f;
        while (robot.IsTesting && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(session.TryDeliverOrder(session.ActiveOrderId, out reason), Is.True, reason);
        yield return null;
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

    void FaceShop()
    {
        var body = interaction.GetComponent<CharacterController>();
        body.enabled = false;
        interaction.transform.SetPositionAndRotation(new Vector3(2.6f, 0, -1.9f), Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
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
    public IEnumerator TwoPaidOrdersBuyStationThroughRealTerminalWithoutResettingActiveRepair()
    {
        yield return CompleteOrder();
        Assert.That(session.Balance, Is.EqualTo(100));
        yield return CompleteOrder();
        Assert.That(session.Balance, Is.EqualTo(220));
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        RobotRepair robot = session.ActiveRobot;
        string orderId = session.ActiveOrderId;
        var screw = robot.Cover.Screws[0];
        robot.Turn(screw, -180f);
        float tightness = screw.Tightness;
        FaceShop();
        yield return Press(Key.E);
        Assert.That(interaction.IsAtEquipmentShop, Is.True);
        var card = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(t => t.name == "EquipmentDescription");
        Assert.That(card.text, Does.Contain("180").And.Contain("40").And.Contain("пока недоступны"));
        yield return Click("BuyStationButton");
        Assert.That(session.HasSolderingStation, Is.True);
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(GameObject.Find("SolderingStationInstalled"), Is.Not.Null);
        Assert.That(GameObject.Find("SolderingStationPreview"), Is.Null);
        Assert.That(session.CanOfferSolderingOrders, Is.False);
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        Assert.That(session.ActiveOrderId, Is.EqualTo(orderId));
        Assert.That(screw.Tightness, Is.EqualTo(tightness));
        Assert.That(session.TryBuySolderingStation(out _), Is.False);
        Assert.That(session.Balance, Is.EqualTo(40));
        Assert.That(Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "BuyStationButton").interactable, Is.False);
        yield return Press(Key.Escape);
        Assert.That(interaction.IsAtEquipmentShop, Is.False);
        Assert.That(Cursor.visible, Is.False);
    }

    [UnityTest]
    public IEnumerator InsufficientFundsExactPriceAndDisabledSessionAreSafe()
    {
        Assert.That(session.TryBuySolderingStation(out string reason), Is.False);
        Assert.That(reason, Does.Contain("180"));
        SetField(session, "<Balance>k__BackingField", 179);
        Assert.That(session.TryBuySolderingStation(out reason), Is.False);
        Assert.That(session.Balance, Is.EqualTo(179));
        Assert.That(session.HasSolderingStation, Is.False);
        SetField(session, "<Balance>k__BackingField", 180);
        session.enabled = false;
        Assert.That(session.TryBuySolderingStation(out _), Is.False);
        Assert.That(session.Balance, Is.EqualTo(180));
        session.enabled = true;
        Assert.That(session.TryBuySolderingStation(out reason), Is.True, reason);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.TryBuySolderingStation(out _), Is.False);
        Assert.That(session.Balance, Is.Zero);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LockedOrdersCannotBlockBasicJobsAndNeedOwnershipAndReadyContent()
    {
        var basics = (OrderDefinition[])typeof(WorkshopSession).GetField("orders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        solderingOrder = Object.Instantiate(basics[1]);
        SetField(solderingOrder, "requiresSolderingStation", true);
        SetField(session, "orders", new[] { solderingOrder, basics[0], basics[1] });
        Assert.That(session.OfferedOrder, Is.SameAs(basics[0]));
        SetField(session, "solderingRepairsReady", true);
        Assert.That(session.OfferedOrder, Is.SameAs(basics[0]), "Ready content still needs the equipment.");
        SetField(session, "solderingRepairsReady", false);
        yield return CompleteOrder();
        yield return CompleteOrder();
        Assert.That(session.TryBuySolderingStation(out _), Is.True);
        Assert.That(session.OfferedOrder, Is.SameAs(basics[0]), "Ownership alone cannot expose unfinished soldering.");
        SetField(session, "solderingRepairsReady", true);
        Assert.That(session.CanOfferSolderingOrders, Is.True);
        Assert.That(session.OfferedOrder, Is.SameAs(solderingOrder));
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
    }

    [UnityTest]
    public IEnumerator EquipmentPanelBlocksMovementClosesWithoutSpendingAndRespectsWalls()
    {
        FaceShop();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(2.6f, 1.5f, -1f);
        wall.transform.localScale = new Vector3(2, 3, .1f);
        Physics.SyncTransforms();
        yield return Press(Key.E);
        Assert.That(interaction.IsAtEquipmentShop, Is.False);
        Object.Destroy(wall);
        yield return null;
        yield return Press(Key.E);
        Assert.That(interaction.IsAtEquipmentShop, Is.True);
        Assert.That(Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "BuyStationButton").interactable, Is.False);
        Vector3 position = interaction.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.1f);
        Assert.That(Vector3.Distance(position, interaction.transform.position), Is.LessThan(.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return Click("CloseEquipmentButton");
        Assert.That(interaction.IsAtEquipmentShop, Is.False);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.HasSolderingStation, Is.False);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("ProgressCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        if (solderingOrder != null) Object.Destroy(solderingOrder);
        yield return null;
        fixture.TearDown();
    }
}
