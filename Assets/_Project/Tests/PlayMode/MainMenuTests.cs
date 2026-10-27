using System;
using System.Collections;
using System.IO;
using System.Linq;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class NewGameProgressTests
{
    public static WorkshopProgress OldProgress() => new WorkshopProgress
    {
        version = 1, balance = 999, completedOrders = 7, hasSolderingStation = true,
        nextOrder = "D-001", activeOrder = "C", activeInstance = Guid.NewGuid().ToString("N"), activeReward = 180
    };

    [Test]
    public void ResetClearsProgressAndBackupSoRecoveryCannotRestoreOldGame()
    {
        var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
        Assert.That(store.TrySave(OldProgress(), out _), Is.True);
        Assert.That(store.TryReset("A-001", out string reason), Is.True, reason);
        Assert.That(store.TryLoad(out var fresh, out reason), Is.True, reason);
        AssertFresh(fresh);
        File.WriteAllText(store.FilePath, "broken");
        Assert.That(store.TryLoad(out fresh, out reason), Is.True, reason);
        AssertFresh(fresh);
        Assert.That(reason, Does.Contain("резервная копия"));
    }

    [Test]
    public void LockedSavePreventsResetWithoutOverwritingExistingProgress()
    {
        var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
        Assert.That(store.TrySave(OldProgress(), out _), Is.True);
        string original = File.ReadAllText(store.FilePath);
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.That(store.TryReset("A-001", out _), Is.False);
        Assert.That(File.ReadAllText(store.FilePath), Is.EqualTo(original));
    }

    static void AssertFresh(WorkshopProgress progress)
    {
        Assert.That(progress.IsValid, Is.True);
        Assert.That(progress.balance, Is.Zero);
        Assert.That(progress.completedOrders, Is.Zero);
        Assert.That(progress.hasSolderingStation, Is.False);
        Assert.That(progress.nextOrder, Is.EqualTo("A-001"));
        Assert.That(progress.activeOrder, Is.Null.Or.Empty);
        Assert.That(progress.activeInstance, Is.Null.Or.Empty);
        Assert.That(progress.activeReward, Is.Zero);
        Assert.That(progress.lastDeliveredInstance, Is.Null.Or.Empty);
    }
}

public sealed class MainMenuFlowTests
{
    readonly InputTestFixture fixture = new();
    Mouse mouse;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        Assert.That(new WorkshopProgressStore(WorkshopProgressStore.DefaultPath).TrySave(NewGameProgressTests.OldProgress(), out _), Is.True);
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/main_menu.unity");
        yield return null;
        yield return null;
    }

    static Button ButtonNamed(string name) => Object.FindObjectsByType<Button>().Single(b => b.name == name);
    static void Click(Button button) => ExecuteEvents.Execute(button.gameObject,
        new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

    [UnityTest]
    public IEnumerator MenuHasFiveCenteredButtonsAndUnavailableEntriesCannotStartAnything()
    {
        Assert.That(Object.FindObjectsByType<Button>().Length, Is.EqualTo(5));
        foreach (var button in Object.FindObjectsByType<Button>())
            Assert.That(button.GetComponent<RectTransform>().anchoredPosition.x, Is.Zero);
        Assert.That(Cursor.visible, Is.True);
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        foreach (string name in new[] { "ContinueButton", "SettingsButton", "CreditsButton" })
        {
            var button = ButtonNamed(name);
            Assert.That(button.interactable, Is.False);
            Assert.That(button.GetComponentInChildren<RawImage>().texture, Is.Not.Null);
            Click(button);
        }
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("main_menu"));
        var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
        Assert.That(store.TryLoad(out var progress, out _), Is.True);
        Assert.That(progress.balance, Is.EqualTo(999));
        Assert.That(ButtonNamed("QuitButton").onClick.GetPersistentMethodName(0), Is.EqualTo(nameof(MainMenu.Quit)));
    }

    [UnityTest]
    public IEnumerator NewGameButtonLoadsWorkshopWithZeroProgressAndFirstClient()
    {
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, ButtonNamed("NewGameButton").transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        yield return new WaitForSecondsRealtime(.05f);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return new WaitForSecondsRealtime(.05f);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        float deadline = Time.realtimeSinceStartup + 20f;
        while (SceneManager.GetActiveScene().name != "WorkshopPrototype" && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("WorkshopPrototype"));
        var session = Object.FindAnyObjectByType<WorkshopSession>();
        Assert.That(session.IsProgressReady, Is.True);
        Assert.That(session.Balance, Is.Zero);
        Assert.That(session.CompletedOrders, Is.Zero);
        Assert.That(session.HasSolderingStation, Is.False);
        Assert.That(session.ActiveOrder, Is.Null);
        Assert.That(session.OfferedOrder.OrderId, Is.EqualTo("A-001"));
        Assert.That(session.ActiveRobot, Is.Null);
    }

    [UnityTest]
    public IEnumerator SaveFailureKeepsMenuOpenAndAllowsRetry()
    {
        var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
        string original = File.ReadAllText(store.FilePath);
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) Click(ButtonNamed("NewGameButton"));
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("main_menu"));
        Assert.That(ButtonNamed("NewGameButton").interactable, Is.True);
        Assert.That(Object.FindObjectsByType<Text>().Single(t => t.name == "Status").text, Is.Not.Empty);
        Assert.That(File.ReadAllText(store.FilePath), Is.EqualTo(original));
    }

    [TearDown]
    public void TearDown() => fixture.TearDown();
}
