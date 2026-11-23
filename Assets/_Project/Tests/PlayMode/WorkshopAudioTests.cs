using System;
using System.Collections;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class WorkshopAudioTests
{
    AudioSource Voice(string cue) => GameObject.Find("Workshop audio")?.transform.Find(cue)?.GetComponent<AudioSource>();

    [UnityTest]
    public IEnumerator CompleteSoundRequiresCleaningAndDoesNotRepeatOnRetest()
    {
        if (GameObject.Find("Workshop audio") is { } previous) Object.Destroy(previous);
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        foreach (WorkshopSound cue in Enum.GetValues(typeof(WorkshopSound)))
        {
            var clip = Resources.Load<AudioClip>("WorkshopSounds/" + cue);
            Assert.That(clip, Is.Not.Null, cue.ToString());
            Assert.That(clip.length, Is.GreaterThan(.005f));
        }
        var session = Object.FindAnyObjectByType<WorkshopSession>();
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!session.IsProgressReady && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(session.TryAcceptOrder(out string reason), Is.True, reason);
        Object.FindAnyObjectByType<Workbench>().PositionRobot(null);
        var robot = session.ActiveRobot;
        Assert.That(robot.RunTest(out reason), Is.True, reason);
        while (robot.IsTesting) yield return null;
        yield return null;
        Assert.That(session.IsRepairComplete, Is.False);
        Assert.That(Voice("RepairComplete"), Is.Null, "A successful arm test alone does not finish a cleaning order.");
        foreach (var patch in robot.DirtPatches) patch.CleanStroke(Vector2.zero, Vector2.one, 2f);
        yield return null;
        Assert.That(session.IsRepairComplete, Is.True);
        Assert.That(Voice("RepairComplete").isPlaying, Is.True);
        Assert.That(robot.RunTest(out reason), Is.True, reason);
        while (robot.IsTesting) yield return null;
        yield return null;
        Assert.That(Voice("RepairComplete").isPlaying, Is.False, "Retesting must not replay the completion reward.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("SoundCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
    }
}
