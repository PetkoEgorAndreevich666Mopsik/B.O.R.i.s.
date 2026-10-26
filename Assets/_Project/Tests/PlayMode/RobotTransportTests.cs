using System.Collections;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public static class RobotTransportTestFlow
{
    public static void Deliver(WorkshopSession session)
    {
        if (session.Transport.IsAtDelivery) return;
        Assert.That(session.Transport.TryPickUp(out string reason), Is.True, reason);
        Physics.SyncTransforms();
        Vector3 point = session.Transport.DeliveryZone.bounds.center;
        Assert.That(session.Transport.TryDrop(new Ray(point + Vector3.up * .5f, Vector3.down), 1f), Is.True);
        Assert.That(session.Transport.IsAtDelivery, Is.True);
    }
}

public sealed class RobotTransportTests
{
    readonly InputTestFixture fixture = new();
    WorkshopSession session;
    Workbench workbench;
    PlayerController player;
    Mouse mouse;
    Keyboard keyboard;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        yield return null;
        session = Object.FindFirstObjectByType<WorkshopSession>();
        workbench = Object.FindFirstObjectByType<Workbench>();
        player = Object.FindFirstObjectByType<PlayerController>();
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
    }

    void Face(Vector3 target)
    {
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.position = new Vector3(target.x, 0f, target.z - 1.5f);
        player.transform.rotation = Quaternion.identity;
        player.View.transform.rotation = Quaternion.LookRotation(target - player.View.transform.position);
        float pitch = player.View.transform.localEulerAngles.x;
        typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(player, pitch > 180f ? pitch - 360f : pitch);
        body.enabled = true;
        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator HeldMouseCarriesRobotWithWalkingAndReleasePlacesItOnWorkbench()
    {
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(workbench.Enter(player.View), Is.False);
        var robot = session.ActiveRobot;
        Collider bodyCollider = null;
        foreach (Collider collider in robot.InspectionPivot.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger && (bodyCollider == null || collider.bounds.size.sqrMagnitude > bodyCollider.bounds.size.sqrMagnitude))
                bodyCollider = collider;
        Assert.That(bodyCollider, Is.Not.Null);
        Face(bodyCollider.bounds.center);
        yield return null;
        Assert.That(Physics.Raycast(player.View.transform.position, player.View.transform.forward, out var hit, 3f,
            ~(1 << player.gameObject.layer), QueryTriggerInteraction.Ignore), Is.True);
        Assert.That(hit.collider.GetComponentInParent<RobotRepair>(), Is.SameAs(robot), hit.collider.name);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f), buttons = 1 });
        yield return null;
        yield return null;
        Assert.That(session.Transport.IsCarrying, Is.True, player.GetComponent<InteractionController>().Feedback);
        Vector3 start = robot.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
        yield return new WaitForSeconds(.15f);
        Assert.That(Vector3.Distance(start, robot.transform.position), Is.GreaterThan(.1f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Face(workbench.transform.position + Vector3.up * 1.1f);
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
        yield return null;
        Assert.That(session.Transport.IsCarrying, Is.False);
        Assert.That(workbench.HasRobot, Is.True);
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        Assert.That(workbench.Enter(player.View), Is.True);
        workbench.Exit();
    }

    [UnityTest]
    public IEnumerator InvalidDropAndDisableRestoreRobotAndIncompleteRobotCannotLeaveTable()
    {
        var transport = session.Transport;
        Vector3 position = session.ActiveRobot.transform.position;
        Assert.That(transport.TryPickUp(out _), Is.True);
        transport.Follow(player.View);
        Assert.That(transport.TryDrop(new Ray(Vector3.up * 10f, Vector3.up), 1f), Is.False);
        Assert.That(Vector3.Distance(position, session.ActiveRobot.transform.position), Is.LessThan(.001f));
        Assert.That(transport.TryPickUp(out _), Is.True);
        transport.Follow(player.View);
        player.GetComponent<InteractionController>().enabled = false;
        Assert.That(transport.IsCarrying, Is.False);
        Assert.That(Vector3.Distance(position, session.ActiveRobot.transform.position), Is.LessThan(.001f));
        workbench.PositionRobot(null);
        session.ActiveRobot.Turn(session.ActiveRobot.Cover.Screws[0], -180f);
        Assert.That(transport.TryPickUp(out string reason), Is.False);
        Assert.That(reason, Does.Contain("винта"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator PassingRobotMustBeDeliveredPhysicallyBeforePaymentAndCanReturnForRepair()
    {
        workbench.PositionRobot(null);
        RobotRepair robot = session.ActiveRobot;
        foreach (var patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        Assert.That(robot.RunTest(out string reason), Is.True, reason);
        while (robot.IsTesting) yield return null;
        Assert.That(session.CanDeliverOrder(out reason), Is.False);
        Assert.That(reason, Does.Contain("зону выдачи"));
        RobotTransportTestFlow.Deliver(session);
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(session.CanDeliverOrder(out reason), Is.True, reason);
        Assert.That(session.Transport.TryPickUp(out reason), Is.True, reason);
        session.Transport.CancelCarry();
        Assert.That(session.Transport.IsAtDelivery, Is.True);
        string id = session.ActiveOrderId;
        Assert.That(session.TryDeliverOrder(id, out reason), Is.True, reason);
        Assert.That(session.TryDeliverOrder(id, out _), Is.False);
        Assert.That(session.Balance, Is.GreaterThan(0));
    }

    [TearDown]
    public void TearDown() => fixture.TearDown();
}
