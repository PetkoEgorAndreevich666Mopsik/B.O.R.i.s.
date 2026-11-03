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
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
        if (key == Key.E) yield return new WaitForSeconds(1.2f);
    }

    IEnumerator ClickRobot()
    {
        Vector2 position = new(Screen.width * .5f, Screen.height * .5f);
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
        Assert.That(session.ActiveRobot, Is.Null);
        Face(Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f)));
        yield return Press(Key.E);
        Assert.That(interaction.PointedWorkbench, Is.SameAs(workbench));
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(interaction.Feedback, Does.Contain("окна"));
    }

    [UnityTest]
    public IEnumerator ShortClickTalksAndHoldAcceptsExactlyOneVisibleVisitor()
    {
        yield return WaitForVisitor();
        RobotRepair visitor = session.VisitorRobot;
        AimAtVisitor();
        yield return ClickRobot();
        Assert.That(session.Window.HasHeardRequest, Is.True);
        Assert.That(session.Window.SpeechText, Does.Contain(session.OfferedOrder.Symptom));
        Assert.That(session.ActiveOrder, Is.Null, "Reading the request must not accept it.");
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(Cursor.visible, Is.False);
        Vector3 position = player.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
        yield return new WaitForSeconds(.1f);
        Assert.That(Vector3.Distance(position, player.transform.position), Is.GreaterThan(.1f), "Conversation must leave walking available.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        AimAtVisitor();
        InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
        yield return new WaitForSeconds(.42f);
        Assert.That(session.ActiveOrder, Is.SameAs(session.OfferedOrder));
        Assert.That(session.Transport.IsCarrying, Is.True, interaction.Feedback);
        Assert.That(session.ActiveOrderId, Is.Not.Empty);
        string instanceId = session.ActiveOrderId;
        RobotRepair robot = session.ActiveRobot;
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(robot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(robot, Is.SameAs(visitor), "Accept the visitor itself rather than replacing it.");
        Assert.That(session.TryAcceptOrder(out string reason), Is.False);
        Assert.That(reason, Does.Contain("активный заказ"));
        Assert.That(session.ActiveOrderId, Is.EqualTo(instanceId));
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        Assert.That(Object.FindObjectsByType<RobotRepair>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        session.Transport.CancelCarry();
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
        Assert.That(interaction.IsAtOrderDesk, Is.False);
        Assert.That(Cursor.visible, Is.False);
    }

    [UnityTest]
    public IEnumerator WallBlocksTalkingAndWalkingAwayDoesNotAcceptVisitor()
    {
        yield return WaitForVisitor();
        AimAtVisitor();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = Vector3.Lerp(player.View.transform.position, session.VisitorRobot.InspectionPivot.position, .5f);
        wall.transform.rotation = Quaternion.LookRotation(session.VisitorRobot.InspectionPivot.position - player.View.transform.position);
        wall.transform.localScale = new Vector3(2, 3, .1f);
        Physics.SyncTransforms();
        yield return ClickRobot();
        Assert.That(session.Window.HasHeardRequest, Is.False);
        Object.Destroy(wall);
        yield return null;
        AimAtVisitor();
        yield return ClickRobot();
        Assert.That(session.Window.HasHeardRequest, Is.True);
        Face(Vector3.zero);
        Assert.That(interaction.IsAtOrderDesk, Is.False);
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
    }

    IEnumerator WaitForVisitor()
    {
        float deadline = Time.realtimeSinceStartup + 9f;
        while (session.Window.Phase != ServiceWindowPhase.AwaitingConversation && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(session.Window.Phase, Is.EqualTo(ServiceWindowPhase.AwaitingConversation));
    }

    void AimAtVisitor()
    {
        Vector3 target = session.VisitorRobot.InspectionPivot.GetComponentInChildren<Renderer>().bounds.center;
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        Vector3 position = target + session.Window.transform.forward * 1.8f;
        position.y = 0f;
        player.transform.position = position;
        Vector3 forward = target - position;
        forward.y = 0f;
        player.transform.rotation = Quaternion.LookRotation(forward);
        player.View.transform.rotation = Quaternion.LookRotation(target - player.View.transform.position);
        float pitch = player.View.transform.localEulerAngles.x;
        typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(player, pitch > 180 ? pitch - 360 : pitch);
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator VisitsPreserveProgressAndResetRepeatsSameOrder()
    {
        Assert.That(session.TryAcceptOrder(out _), Is.True);
        Object.FindFirstObjectByType<Workbench>().PositionRobot(null);
        string orderId = session.ActiveOrderId;
        RobotRepair robot = session.ActiveRobot;
        Face(Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f)));
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
