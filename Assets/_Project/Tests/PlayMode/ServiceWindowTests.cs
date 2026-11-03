using System.Collections;
using System.IO;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class ServiceWindowTests
{
    WorkshopSession session;
    ServiceWindow window;
    Workbench workbench;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        yield return null;
        session = Object.FindFirstObjectByType<WorkshopSession>();
        window = session.Window;
        workbench = Object.FindFirstObjectByType<Workbench>();
    }

    IEnumerator WaitForVisitor()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (window.Phase != ServiceWindowPhase.AwaitingConversation && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.AwaitingConversation));
    }

    IEnumerator WaitForSequence()
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (window.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(window.IsBusy, Is.False);
    }

    [UnityTest]
    public IEnumerator AutomaticArrivalDoesNotReplaceRobotAlreadyOnWorkbench()
    {
        RobotRepair existing = workbench.LoadRobot(session.OfferedOrder.RobotPrefab);
        existing.Turn(existing.Cover.Screws[0], -180f);
        float tightness = existing.Cover.Screws[0].Tightness;
        yield return new WaitForSeconds(1.5f);
        Assert.That(workbench.Robot, Is.SameAs(existing));
        Assert.That(existing.Cover.Screws[0].Tightness, Is.EqualTo(tightness));
        Assert.That(session.TryPrepareVisitor(out string reason), Is.False);
        Assert.That(reason, Does.Contain("верстак"));
        Assert.That(session.VisitorRobot, Is.Null);
    }

    [UnityTest]
    public IEnumerator ConveyorOnlyMovesWithDoorOpenAndArrivalDoesNotAcceptOrTestRobot()
    {
        bool sawOpenDoor = false, sawMovingBelt = false;
        float deadline = Time.realtimeSinceStartup + 10f;
        while (window.Phase != ServiceWindowPhase.AwaitingConversation && Time.realtimeSinceStartup < deadline)
        {
            sawOpenDoor |= window.DoorOpenness > .1f;
            if (window.BeltDirection != 0f)
            {
                sawMovingBelt = true;
                Assert.That(window.DoorOpenness, Is.EqualTo(1f));
                Assert.That(window.BeltDirection, Is.GreaterThan(0f));
                Assert.That(window.TryTakeVisitor(out _), Is.False);
            }
            yield return null;
        }
        Assert.That(sawOpenDoor && sawMovingBelt, Is.True);
        Assert.That(window.DoorOpenness, Is.Zero);
        Assert.That(window.BeltDirection, Is.Zero);
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(session.ActiveRobot, Is.Null);
        Assert.That(session.VisitorRobot, Is.Not.Null);
        Assert.That(session.VisitorRobot.TestResult, Is.EqualTo(RobotTestResult.NotTested));
        Assert.That(workbench.HasRobot, Is.False);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(File.Exists(session.SavePath), Is.False, "Arrival alone is not a saved contract.");
    }

    [UnityTest]
    public IEnumerator RequestMustBeHeardAndRemarksCanBeRepeatedWithoutAccepting()
    {
        yield return WaitForVisitor();
        Transform head = session.VisitorRobot.InspectionPivot.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
        Quaternion restingHead = head.localRotation;
        Assert.That(window.TryTakeVisitor(out string reason), Is.False);
        Assert.That(reason, Does.Contain("поговорите"));
        Assert.That(window.Talk(out reason), Is.True, reason);
        Assert.That(window.SpeechText, Does.Contain(session.OfferedOrder.Symptom));
        Assert.That(window.Talk(out reason), Is.True, reason);
        Assert.That(window.SpeechText, Does.Contain(session.OfferedOrder.PersonalRemark));
        Assert.That(window.Talk(out _), Is.True);
        Assert.That(window.SpeechText, Does.Contain(session.OfferedOrder.Symptom));
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(File.Exists(session.SavePath), Is.False);
        Object.FindFirstObjectByType<PlayerController>().View.transform.position += Vector3.forward * .6f;
        yield return new WaitForSeconds(.2f);
        Assert.That(Quaternion.Angle(head.localRotation, restingHead), Is.GreaterThan(1f), "The robot looks at its visitor during conversation.");
        Assert.That(window.TryTakeVisitor(out _), Is.True);
        Assert.That(Quaternion.Angle(head.localRotation, restingHead), Is.LessThan(.01f), "Pickup restores the repair pose.");
        session.Transport.CancelCarry();
    }

    [UnityTest]
    public IEnumerator UntestedAndDirtyRobotStayInsideWithSpecificReasonAndNoPayment()
    {
        yield return WaitForVisitor();
        window.Talk(out _);
        Assert.That(window.TryTakeVisitor(out string reason), Is.True, reason);
        session.Transport.CancelCarry();
        RobotTransportTestFlow.Deliver(session);
        Assert.That(window.TrySend(out reason), Is.False);
        Assert.That(reason, Does.Contain("тест"));
        Assert.That(session.ActiveRobot.RunTest(out reason), Is.True, reason);
        while (session.ActiveRobot.IsTesting) yield return null;
        Assert.That(window.TrySend(out reason), Is.False);
        Assert.That(reason, Does.Contain("очистку"));
        Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.InRepair));
        Assert.That(window.DoorOpenness, Is.Zero);
        Assert.That(window.BeltDirection, Is.Zero);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
        Assert.That(session.Transport.IsAtDelivery, Is.True);
    }

    [UnityTest]
    public IEnumerator FailedAcceptanceKeepsSameVisitorAndCanBeRetried()
    {
        yield return WaitForVisitor();
        RobotRepair visitor = session.VisitorRobot;
        window.Talk(out _);
        string blocked = session.SavePath + ".tmp";
        Directory.CreateDirectory(blocked);
        try
        {
            Assert.That(window.TryTakeVisitor(out _), Is.False);
            Assert.That(session.ActiveOrder, Is.Null);
            Assert.That(session.VisitorRobot, Is.SameAs(visitor));
            Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.AwaitingPickup));
            Assert.That(session.Transport.IsCarrying, Is.False);
        }
        finally { Directory.Delete(blocked); }
        Assert.That(window.TryTakeVisitor(out string reason), Is.True, reason);
        Assert.That(session.ActiveRobot, Is.SameAs(visitor));
        session.Transport.CancelCarry();
    }

    [UnityTest]
    public IEnumerator FailedPaymentBringsRobotBackAndRetryPaysExactlyOnce()
    {
        yield return WaitForVisitor();
        window.Talk(out _);
        Assert.That(window.TryTakeVisitor(out _), Is.True);
        session.Transport.CancelCarry();
        workbench.PositionRobot(null);
        foreach (DirtPatch patch in session.ActiveRobot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        Assert.That(session.ActiveRobot.RunTest(out _), Is.True);
        while (session.ActiveRobot.IsTesting) yield return null;
        RobotTransportTestFlow.Deliver(session);
        RobotRepair robot = session.ActiveRobot;
        string instance = session.ActiveOrderId;
        int reward = session.ActiveOrderReward;
        string confirmed = File.ReadAllText(session.SavePath);
        string blocked = session.SavePath + ".tmp";
        Directory.CreateDirectory(blocked);
        try
        {
            Assert.That(window.TrySend(out string reason), Is.True, reason);
            Assert.That(session.Transport.TryPickUp(out _), Is.False, "Dispatch owns the robot until it finishes.");
            yield return WaitForSequence();
            Assert.That(session.ActiveOrderId, Is.EqualTo(instance));
            Assert.That(session.ActiveRobot, Is.SameAs(robot));
            Assert.That(session.ActiveRobot.TestResult, Is.EqualTo(RobotTestResult.Passed), "The goodbye gesture must not invalidate a repair test.");
            Assert.That(session.Balance, Is.Zero);
            Assert.That(File.ReadAllText(session.SavePath), Is.EqualTo(confirmed));
            Assert.That(window.Phase, Is.EqualTo(ServiceWindowPhase.InRepair));
            Assert.That(window.DoorOpenness, Is.Zero);
            Assert.That(window.SpeechText, Does.Contain("сохранить"));
        }
        finally { Directory.Delete(blocked); }
        Assert.That(window.TrySend(out _), Is.True);
        bool sawReverse = false;
        float deadline = Time.realtimeSinceStartup + 10f;
        while (window.IsBusy && Time.realtimeSinceStartup < deadline)
        {
            if (window.BeltDirection != 0f)
            {
                sawReverse = true;
                Assert.That(window.BeltDirection, Is.LessThan(0f));
                Assert.That(window.DoorOpenness, Is.EqualTo(1f));
            }
            yield return null;
        }
        Assert.That(sawReverse, Is.True);
        Assert.That(window.IsBusy, Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(session.Balance, Is.EqualTo(reward));
        Assert.That(session.CompletedOrders, Is.EqualTo(1));
        Assert.That(session.TryDeliverOrder(instance, out _), Is.False);
        Assert.That(window.DoorOpenness, Is.Zero);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("WindowCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
    }
}
