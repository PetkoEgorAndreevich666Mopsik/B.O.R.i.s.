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

public static class EquipmentTestFlow
{
    public static void FaceTerminal(InteractionController interaction)
    {
        Transform terminal = Object.FindFirstObjectByType<EquipmentShop>().transform;
        var body = interaction.GetComponent<CharacterController>();
        body.enabled = false;
        Vector3 point = terminal.position + terminal.forward * 1.75f;
        point.y = 0;
        interaction.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-terminal.forward, Vector3.up));
        body.enabled = true;
        Physics.SyncTransforms();
    }

    public static IEnumerator InstallParcel(WorkshopSession session)
    {
        var window = session.Window;
        float deadline = Time.realtimeSinceStartup + 9f;
        while (window.Phase != ServiceWindowPhase.ParcelReady && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.ParcelReady));
        Assert.That(window.Robot, Is.Null);
        var parcel = GameObject.Find("SolderingStationParcel").GetComponent<Collider>();
        Assert.That(window.TryTakeParcel(parcel), Is.True);
        window.CancelParcel();
        Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.ParcelReady));
        Assert.That(window.TryTakeParcel(parcel), Is.True);
        Vector3 destination = Object.FindFirstObjectByType<EquipmentShop>().InstallationPoint.position;
        Assert.That(window.TryPlaceParcel(new Ray(destination + Vector3.up * .5f, Vector3.down),
            1f, Physics.DefaultRaycastLayers, out string reason), Is.True, reason);
        yield return null;
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

    static void AssertInstalledPartsAttached(RobotRepair robot, string stage)
    {
        int checkedParts = 0;
        foreach (RepairPart part in robot.GetComponentsInChildren<RepairPart>())
        {
            if (!part.IsInstalled) continue;
            checkedParts++;
            string context = $"{stage}: {part.name}";
            Assert.That(Vector3.Distance(part.transform.position, part.InstallationAnchor.position),
                Is.LessThan(.001f), context + " visible pose must follow its attachment");
            Assert.That(Quaternion.Angle(part.transform.rotation, part.InstallationAnchor.rotation),
                Is.LessThan(.05f), context + " visible rotation must follow its attachment");
            Assert.That(Vector3.Distance(part.Body.position, part.InstallationAnchor.position),
                Is.LessThan(.001f), context + " physics pose must follow its attachment");
            Assert.That(Quaternion.Angle(part.Body.rotation, part.InstallationAnchor.rotation),
                Is.LessThan(.05f), context + " physics rotation must follow its attachment");
            Assert.That(part.Body.isKinematic, Is.True, context);
        }
        Assert.That(checkedParts, Is.EqualTo(2), stage);
    }

    [UnityTest]
    public IEnumerator CarryAndPlacementKeepInstalledPhysicsAlignedWithRobot()
    {
        // Drive the transport directly so a released test mouse cannot cancel the carry between physics steps.
        player.GetComponent<InteractionController>().enabled = false;
        RobotRepair robot = session.ActiveRobot;
        Assert.That(session.Transport.TryPickUp(out string reason), Is.True, reason);
        for (int frame = 0; frame < 4; frame++)
        {
            player.View.transform.SetPositionAndRotation(new Vector3(frame * .6f - .8f, 1.8f, -1.5f),
                Quaternion.Euler(10f, 30f + frame * 35f, 0f));
            session.Transport.Follow(player.View);
            AssertInstalledPartsAttached(robot, $"carry frame {frame}");
            yield return new WaitForFixedUpdate();
            yield return null;
            AssertInstalledPartsAttached(robot, $"after physics frame {frame}");
        }
        Collider table = workbench.transform.Find("TableTop").GetComponent<Collider>();
        Assert.That(session.Transport.TryDrop(new Ray(table.bounds.center + Vector3.up * 2f, Vector3.down), 3f), Is.True);
        Assert.That(workbench.HasRobot, Is.True);
        AssertInstalledPartsAttached(robot, "immediately after placing on workbench");
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return null;
        AssertInstalledPartsAttached(robot, "after settling on workbench");
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
        yield return new WaitForSeconds(.42f);
        Assert.That(session.Transport.IsCarrying, Is.True, player.GetComponent<InteractionController>().Feedback);
        AssertInstalledPartsAttached(robot, "mouse pickup");
        Vector3 start = robot.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
        yield return new WaitForSeconds(.15f);
        Assert.That(Vector3.Distance(start, robot.transform.position), Is.GreaterThan(.1f));
        AssertInstalledPartsAttached(robot, "walking with held mouse");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Face(workbench.transform.position + Vector3.up * 1.1f);
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return null;
        yield return null;
        Assert.That(session.Transport.IsCarrying, Is.False);
        Assert.That(workbench.HasRobot, Is.True);
        Assert.That(session.ActiveRobot, Is.SameAs(robot));
        AssertInstalledPartsAttached(robot, "mouse release onto workbench");
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
