using BORis;
using NUnit.Framework;
using UnityEngine;

public sealed class RepairRulesTests
{
    GameObject robot;
    RobotRepair repair;
    ScrewFastener screw;

    [SetUp]
    public void SetUp()
    {
        robot = new GameObject("Robot");
        repair = robot.AddComponent<RobotRepair>();
        var fastener = new GameObject("Screw");
        fastener.transform.SetParent(robot.transform);
        screw = fastener.AddComponent<ScrewFastener>();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(robot);

    [Test]
    public void TwoTurnsReleaseAndRetightenScrew_WithVisibleTravel()
    {
        Assert.That(repair.Turn(screw, -360f), Is.EqualTo(-360f).Within(.01f));
        Assert.That(screw.Tightness, Is.EqualTo(.5f).Within(.0001f));
        Assert.That(screw.transform.localPosition.y, Is.EqualTo(.03f).Within(.0001f));
        repair.Turn(screw, -360f);
        Assert.That(screw.Tightness, Is.Zero);
        Assert.That(screw.transform.localPosition.y, Is.EqualTo(.06f).Within(.0001f));
        repair.Turn(screw, 720f);
        Assert.That(screw.Tightness, Is.EqualTo(1f));
        Assert.That(screw.transform.localPosition.y, Is.Zero.Within(.0001f));
    }

    [Test]
    public void RotatedScrewTravelsAlongItsOwnAxis()
    {
        Object.DestroyImmediate(screw.gameObject);
        var fastener = new GameObject("RotatedScrew");
        fastener.transform.SetParent(robot.transform);
        fastener.transform.localPosition = new Vector3(1f, 2f, 3f);
        fastener.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        screw = fastener.AddComponent<ScrewFastener>();
        repair.Turn(screw, -720f);
        Assert.That(Vector3.Distance(screw.transform.localPosition, new Vector3(1f, 2f, 2.94f)), Is.LessThan(.0001f));
        repair.Turn(screw, 720f);
        Assert.That(Vector3.Distance(screw.transform.localPosition, new Vector3(1f, 2f, 3f)), Is.LessThan(.0001f));
    }

    [Test]
    public void EndStopsRejectAdditionalTravelAndNonFiniteInput()
    {
        Assert.That(repair.Turn(screw, 100f), Is.Zero);
        repair.Turn(screw, -10000f);
        Assert.That(screw.Tightness, Is.Zero);
        Assert.That(repair.Turn(screw, -100f), Is.Zero);
        Assert.That(repair.Turn(screw, float.NaN), Is.Zero);
        Assert.That(repair.Turn(screw, float.PositiveInfinity), Is.Zero);
        Assert.That(screw.Tightness, Is.Zero);
    }

    [Test]
    public void EndpointToleranceFinishesGesture_AndAllowsImmediateReverse()
    {
        repair.Turn(screw, -705f);
        Assert.That(screw.IsLoose, Is.True);
        repair.Turn(screw, 5f);
        Assert.That(screw.Tightness, Is.GreaterThan(0f).And.LessThan(.03f));
        repair.Turn(screw, 700f);
        Assert.That(screw.IsTight, Is.True);
        repair.Turn(screw, -5f);
        Assert.That(screw.Tightness, Is.LessThan(1f).And.GreaterThan(.97f));
    }

    [Test]
    public void AnotherRobotAndDisabledScrewCannotBeChanged()
    {
        var other = new GameObject("OtherRobot").AddComponent<RobotRepair>();
        Assert.That(other.Turn(screw, -180f), Is.Zero);
        Object.DestroyImmediate(other.gameObject);
        screw.enabled = false;
        Assert.That(repair.Turn(screw, -180f), Is.Zero);
        Assert.That(screw.Tightness, Is.EqualTo(1f));
    }
}
