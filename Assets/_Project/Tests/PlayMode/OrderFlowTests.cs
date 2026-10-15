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

public sealed class OrderFlowTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    WorkshopSession session;
    Workbench workbench;
    PlayerController player;
    InteractionController interaction;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        session = Object.FindFirstObjectByType<WorkshopSession>();
        workbench = Object.FindFirstObjectByType<Workbench>();
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        yield return null;
        yield return null;
    }

    void Face(Vector3 position)
    {
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.identity);
        body.enabled = true;
        Physics.SyncTransforms();
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

    IEnumerator Click(Button button)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator EmptyWorkbenchExplainsWhereToAcceptOrder()
    {
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(Object.FindObjectsByType<RobotRepair>(FindObjectsSortMode.None), Is.Empty);
        Face(new Vector3(0, 0, -.7f));
        yield return Press(Key.E);
        Assert.That(interaction.PointedWorkbench, Is.SameAs(workbench));
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(interaction.Feedback, Does.Contain("стойке"));
    }

    [UnityTest]
    public IEnumerator RealDeskInteractionFreezesPlayerAndAcceptsExactlyOneOrder()
    {
        Face(new Vector3(-2.6f, 0, -1.9f));
        yield return Press(Key.E);
        Assert.That(interaction.IsAtOrderDesk, Is.True);
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(Cursor.visible, Is.True);
        Vector3 position = player.transform.position;
        Quaternion rotation = player.transform.rotation;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100, 100) });
        yield return new WaitForSeconds(.1f);
        Assert.That(Vector3.Distance(position, player.transform.position), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(rotation, player.transform.rotation), Is.LessThan(.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Button accept = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "AcceptOrderButton");
        yield return Click(accept);
        Assert.That(session.ActiveOrder, Is.SameAs(session.OfferedOrder));
        Assert.That(session.ActiveOrderId, Is.Not.Empty);
        string instanceId = session.ActiveOrderId;
        RobotRepair robot = session.ActiveRobot;
        Assert.That(workbench.HasRobot, Is.True);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(accept.interactable, Is.False);
        Assert.That(session.TryAcceptOrder(out string reason), Is.False);
        Assert.That(reason, Does.Contain("активный заказ"));
        Assert.That(session.ActiveOrderId, Is.EqualTo(instanceId));
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        Assert.That(Object.FindObjectsByType<RobotRepair>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        yield return Press(Key.Escape);
        Assert.That(interaction.IsAtOrderDesk, Is.False);
        Assert.That(Cursor.visible, Is.False);
    }

    [UnityTest]
    public IEnumerator ClosingWithoutAcceptingDoesNotCreateRobotAndWallBlocksDesk()
    {
        Face(new Vector3(-2.6f, 0, -1.9f));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(-2.6f, 1.5f, -1f);
        wall.transform.localScale = new Vector3(2, 3, .1f);
        Physics.SyncTransforms();
        yield return Press(Key.E);
        Assert.That(interaction.IsAtOrderDesk, Is.False);
        Object.Destroy(wall);
        yield return null;
        yield return Press(Key.E);
        Assert.That(interaction.IsAtOrderDesk, Is.True);
        Button close = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "CloseOrderButton");
        yield return Click(close);
        Assert.That(interaction.IsAtOrderDesk, Is.False);
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
    }

    [UnityTest]
    public IEnumerator VisitsPreserveProgressAndResetRepeatsSameOrder()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        string orderId = session.ActiveOrderId;
        RobotRepair robot = session.ActiveRobot;
        Face(new Vector3(0, 0, -.7f));
        yield return Press(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        robot.Turn(robot.Cover.Screws[0], -180f);
        float progress = robot.Cover.Screws[0].Tightness;
        yield return Press(Key.Escape);
        yield return Press(Key.E);
        Assert.That(interaction.Robot, Is.SameAs(robot));
        Assert.That(robot.Cover.Screws[0].Tightness, Is.EqualTo(progress));
        Assert.That(session.ActiveOrderId, Is.EqualTo(orderId));
        Assert.That(workbench.LoadRobot(session.OfferedOrder.RobotPrefab), Is.Null, "Cannot replace an occupied table.");
        interaction.ResetRepair();
        Assert.That(interaction.Robot, Is.Not.SameAs(robot));
        Assert.That(session.ActiveRobot, Is.SameAs(interaction.Robot));
        Assert.That(session.ActiveOrderId, Is.EqualTo(orderId));
        Assert.That(interaction.Robot.Cover.Screws.All(s => s.IsTight), Is.True);
        yield return null;
        Assert.That(Object.FindObjectsByType<RobotRepair>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator InvalidOrderDoesNotCreatePartialSession()
    {
        OrderDefinition definition = session.OfferedOrder;
        var field = typeof(OrderDefinition).GetField("robotPrefab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        RobotRepair original = definition.RobotPrefab;
        try
        {
            field.SetValue(definition, null);
            Assert.That(definition.IsValid, Is.False);
            Assert.That(session.TryAcceptOrder(out string reason), Is.False);
            Assert.That(reason, Does.Contain("данные"));
            Assert.That(session.ActiveOrder, Is.Null);
            Assert.That(session.ActiveOrderId, Is.Null);
            Assert.That(workbench.HasRobot, Is.False);
        }
        finally { field.SetValue(definition, original); }
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("OrderCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
