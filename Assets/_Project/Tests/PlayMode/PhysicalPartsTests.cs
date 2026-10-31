using System.Collections;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class PhysicalPartsTests
{
    Workbench bench;
    RobotRepair robot;
    RepairPart spare;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        bench = Object.FindAnyObjectByType<Workbench>();
        robot = bench.Robot;
        spare = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
    }

    [UnityTest]
    public IEnumerator DetachedCoverFallsOntoTableAndRetainsCleaningUVs()
    {
        RepairPart cover = robot.Cover;
        foreach (var screw in cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(cover, false, out string reason), Is.True, reason);
        cover.RecoverToTable();
        cover.Body.position += Vector3.up * .35f;
        float startHeight = cover.Body.position.y;
        Assert.That(cover.Body.isKinematic, Is.False);
        yield return new WaitForSeconds(.3f);
        Assert.That(cover.Body.position.y, Is.LessThan(startHeight - .1f), "A released part must actually fall.");
        yield return new WaitForSeconds(1f);
        Assert.That(cover.Body.position.y, Is.GreaterThan(cover.TrayAnchor.position.y - .3f), "The cover must rest on the bench instead of passing through it.");
        Assert.That(cover.Body.linearVelocity.magnitude, Is.LessThan(.6f), "Contact should settle rather than continually bounce.");
        Assert.That(cover.IsInstalled, Is.False);
        Physics.SyncTransforms();
        foreach (DirtPatch patch in cover.GetComponentsInChildren<DirtPatch>())
        {
            var collider = patch.GetComponent<MeshCollider>();
            Vector3 center = patch.transform.TransformPoint(Vector3.zero);
            bool hit = collider.Raycast(new Ray(center - patch.transform.forward * .2f, patch.transform.forward), out RaycastHit contact, .4f);
            if (!hit) hit = collider.Raycast(new Ray(center + patch.transform.forward * .2f, -patch.transform.forward), out contact, .4f);
            Assert.That(hit, Is.True, "The original dirt mesh must remain raycastable after a physical fall: " + patch.name);
            Assert.That(contact.textureCoord.x, Is.EqualTo(.5f).Within(.05f));
            Assert.That(contact.textureCoord.y, Is.EqualTo(.5f).Within(.05f));
            Assert.That(robot.CanClean(patch, out reason), Is.True, reason);
            Assert.That(patch.CleanStroke(contact.textureCoord - Vector2.right * .15f,
                contact.textureCoord + Vector2.right * .15f, .05f), Is.GreaterThan(0));
        }
    }

    void AssertDirtFollowsCover(RepairPart cover,
        System.Collections.Generic.Dictionary<DirtPatch, Matrix4x4> surfacePoses, string stage)
    {
        foreach (var surface in surfacePoses)
        {
            DirtPatch patch = surface.Key;
            var renderer = patch.GetComponent<MeshRenderer>();
            Mesh mesh = patch.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 expected = cover.transform.localToWorldMatrix * surface.Value;
            string context = stage + ": " + patch.name;
            Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, context);
            foreach (Vector3 vertex in mesh.vertices)
                Assert.That(Vector3.Distance(renderer.localToWorldMatrix.MultiplyPoint3x4(vertex),
                    expected.MultiplyPoint3x4(vertex)), Is.LessThan(.002f),
                    context + " visible dirt vertices must remain on their authored cover surface.");
            Vector3 center = expected.MultiplyPoint3x4(mesh.bounds.center);
            Assert.That(Vector3.Distance(renderer.bounds.center, center), Is.LessThan(.002f),
                context + " renderer bounds must move with the visible cover, not remain in the robot opening.");
            Vector3 normal = expected.inverse.transpose.MultiplyVector(mesh.normals[0]).normalized;
            var collider = patch.GetComponent<MeshCollider>();
            Assert.That(collider.Raycast(new Ray(center + normal * .15f, -normal), out var hit, .3f), Is.True,
                context + " UV ray target must coincide with the expected visible surface.");
            Assert.That(Vector3.Distance(hit.point, center), Is.LessThan(.002f), context);
            Assert.That(hit.textureCoord.x, Is.EqualTo(.5f).Within(.01f), context);
            Assert.That(hit.textureCoord.y, Is.EqualTo(.5f).Within(.01f), context);
            Assert.That(Vector3.Angle(hit.normal, normal), Is.LessThan(.2f), context);
        }
    }

    [UnityTest]
    public IEnumerator DirtRenderersAndUVTargetsFollowFallingRotatedAndCarriedCover()
    {
        RepairPart cover = robot.Cover;
        var surfacePoses = cover.GetComponentsInChildren<DirtPatch>().ToDictionary(patch => patch,
            patch => cover.transform.worldToLocalMatrix * patch.GetComponent<MeshRenderer>().localToWorldMatrix);
        Assert.That(surfacePoses.Count, Is.EqualTo(2));
        foreach (var screw in cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.TryPlace(cover, false, out string reason), Is.True, reason);
        cover.RecoverToTable();
        cover.Body.position += Vector3.up * .45f;
        cover.Body.rotation = Quaternion.Euler(-55f, 22f, 12f);
        cover.Body.angularVelocity = new Vector3(1.5f, .7f, .4f);
        float startHeight = cover.Body.position.y;
        yield return new WaitForSeconds(1.4f);
        Assert.That(cover.Body.position.y, Is.LessThan(startHeight - .1f), "The cover must actually move under physics.");
        cover.SetSupported(true);
        yield return null;
        yield return null;
        AssertDirtFollowsCover(cover, surfacePoses, "After physical fall");

        cover.Body.rotation = Quaternion.Euler(78f, 135f, 22f);
        yield return null;
        yield return null;
        AssertDirtFollowsCover(cover, surfacePoses, "After turning the supported cover");

        Assert.That(robot.TryPlace(cover, true, out reason), Is.True, reason);
        var destination = new GameObject("DirtCarryDestination").transform;
        destination.SetPositionAndRotation(bench.transform.position + new Vector3(2f, .8f, 1.5f),
            Quaternion.Euler(0f, 55f, 0f));
        bench.PositionRobot(destination);
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;
        AssertDirtFollowsCover(cover, surfacePoses, "After carrying the robot");
        Object.Destroy(destination.gameObject);
    }

    [UnityTest]
    public IEnumerator HeldDriveKeepsPhysicalContactsAndFallsWhenReleased()
    {
        spare.SetSupported(true);
        spare.Body.position = bench.transform.TransformPoint(new Vector3(-.7f, 2.5f, -.35f));
        spare.Body.rotation = Quaternion.identity;
        Vector3 center = spare.Body.worldCenterOfMass;
        Vector3 bodyOffset = center - spare.Body.position;
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "PhysicalHoldObstacle";
        obstacle.transform.position = center + Vector3.right * .6f;
        obstacle.transform.localScale = new Vector3(.3f, 1.2f, 1.2f);
        Physics.SyncTransforms();
        spare.SetSupported(false);
        spare.SetDragging(true);
        spare.MoveHeld(center + Vector3.right * 1.2f - bodyOffset, Quaternion.identity);
        Assert.That(spare.Body.isKinematic, Is.False);
        Assert.That(spare.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).All(c => c.enabled), Is.True);
        yield return new WaitForSeconds(.5f);
        Assert.That(spare.Body.worldCenterOfMass.x, Is.GreaterThan(center.x + .1f),
            "The physical hold must move the drive toward the requested position before contact stops it.");
        Assert.That(spare.Body.worldCenterOfMass.x, Is.LessThan(obstacle.GetComponent<Collider>().bounds.max.x),
            "A held drive must not teleport through a solid obstacle to reach the cursor target.");
        float heldHeight = spare.Body.position.y;
        spare.RestorePose();
        Assert.That(spare.IsDragging, Is.False);
        Assert.That(spare.Body.isKinematic, Is.False);
        Object.Destroy(obstacle);
        yield return new WaitForSeconds(.3f);
        Assert.That(spare.Body.position.y, Is.LessThan(heldHeight - .1f));
    }

    [UnityTest]
    public IEnumerator RecoveryPreservesDetachedIdentityAndInstalledPartsStayFixed()
    {
        var coverPose = robot.Cover.transform.position;
        bool faulty = spare.Faulty;
        string identity = spare.DriveIdentity;
        spare.Body.position = bench.transform.position + Vector3.down * 3f;
        spare.RecoverToTable();
        Assert.That(spare.IsInstalled, Is.False);
        Assert.That(spare.Faulty, Is.EqualTo(faulty));
        Assert.That(spare.DriveIdentity, Is.EqualTo(identity));
        Assert.That(spare.Body.position.y, Is.GreaterThan(bench.transform.position.y));
        Assert.That(spare.Body.isKinematic, Is.False);
        Assert.That(robot.Cover.Body.isKinematic, Is.True);
        yield return new WaitForSeconds(.3f);
        Assert.That(Vector3.Distance(robot.Cover.transform.position, coverPose), Is.LessThan(.001f));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("PhysicalPartsCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
    }
}
