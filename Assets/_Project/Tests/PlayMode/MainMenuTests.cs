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
        foreach (string name in new[] { "ContinueButton", "CreditsButton" })
        {
            var button = ButtonNamed(name);
            Assert.That(button.interactable, Is.False);
            Assert.That(button.GetComponentInChildren<Text>().text, Does.Not.Contain("РАЗРАБОТКЕ"));
            Click(button);
        }
        Assert.That(ButtonNamed("SettingsButton").interactable, Is.True);
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("main_menu"));
        var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
        Assert.That(store.TryLoad(out var progress, out _), Is.True);
        Assert.That(progress.balance, Is.EqualTo(999));
        Assert.That(ButtonNamed("QuitButton").onClick.GetPersistentMethodName(0), Is.EqualTo(nameof(MainMenu.Quit)));
    }

    [UnityTest]
    public IEnumerator MenuHoverFollowsMouseWithoutLeavingPreviousButtonHighlighted()
    {
        var start = ButtonNamed("NewGameButton");
        var settings = ButtonNamed("SettingsButton");
        var quit = ButtonNamed("QuitButton");
        foreach (var button in new[] { start, settings, quit })
        {
            yield return Hover(button);
            var label = button.GetComponentInChildren<Text>();
            Vector2 local = label.rectTransform.rect.center + Vector2.right * (label.preferredWidth * .5f + 12f);
            Vector2 besideText = RectTransformUtility.WorldToScreenPoint(null, label.rectTransform.TransformPoint(local));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = besideText, delta = besideText - mouse.position.ReadValue() });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(button.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(button.colors.normalColor),
                "Empty space beside " + button.name + " must not highlight it.");
        }
        EventSystem.current.SetSelectedGameObject(start.gameObject);
        yield return Hover(settings);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
        Assert.That(start.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(start.colors.normalColor));
        Assert.That(settings.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(settings.colors.highlightedColor));
        yield return Hover(ButtonNamed("ContinueButton"));
        Assert.That(settings.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(settings.colors.normalColor));
        Assert.That(ButtonNamed("ContinueButton").interactable, Is.False);
        EventSystem.current.SetSelectedGameObject(start.gameObject);
        ExecuteEvents.Execute(start.gameObject,
            new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down, moveVector = Vector2.down }, ExecuteEvents.moveHandler);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(settings.gameObject));
        yield return Hover(quit);
        Assert.That(settings.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(settings.colors.normalColor));
        Assert.That(quit.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(quit.colors.highlightedColor));
        Click(settings);
        yield return null;
        Click(ButtonNamed("BackButton"));
        yield return new WaitForSecondsRealtime(.2f);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
        Assert.That(start.targetGraphic.canvasRenderer.GetColor(), Is.EqualTo(start.colors.normalColor));
    }

    IEnumerator Hover(Button button)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, delta = position - mouse.position.ReadValue() });
        yield return null;
        yield return new WaitForSecondsRealtime(.2f);
    }

    [UnityTest]
    public IEnumerator SettingsDropdownRowsAreReadableAndSelectAnOption()
    {
        Click(ButtonNamed("SettingsButton"));
        yield return null;
        foreach (var dropdown in Object.FindObjectsByType<Dropdown>())
        {
            ExecuteEvents.Execute(dropdown.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            yield return new WaitForSecondsRealtime(.2f);
            var list = GameObject.Find("Dropdown List");
            Assert.That(list, Is.Not.Null);
            var rows = list.GetComponentsInChildren<Toggle>();
            Assert.That(rows.Length, Is.EqualTo(dropdown.options.Count));
            var scroll = list.GetComponent<ScrollRect>();
            if (scroll.content.rect.height > scroll.viewport.rect.height + 96f)
            {
                scroll.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
                float before = scroll.content.anchoredPosition.y;
                ExecuteEvents.Execute(scroll.gameObject,
                    new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -6f) }, ExecuteEvents.scrollHandler);
                Assert.That(scroll.content.anchoredPosition.y - before, Is.InRange(64f, 128f),
                    "One wheel notch should move two to four rows.");
            }
            foreach (var row in rows)
            {
                var label = row.GetComponentInChildren<Text>();
                Assert.That(label.text, Is.Not.Empty);
                Assert.That(label.font, Is.Not.Null);
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height));
                var selection = row.graphic.rectTransform;
                Assert.That(selection.rect.width, Is.EqualTo(((RectTransform)row.transform).rect.width).Within(.1f));
                Assert.That(selection.rect.height, Is.EqualTo(((RectTransform)row.transform).rect.height).Within(.1f));
                Assert.That(row.graphic.transform.GetSiblingIndex(), Is.LessThan(label.transform.GetSiblingIndex()));
            }
            int selected = dropdown.value == 0 && rows.Length > 1 ? 1 : 0;
            ExecuteEvents.Execute(rows[selected].gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            float deadline = Time.realtimeSinceStartup + 1f;
            while (GameObject.Find("Dropdown List") != null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(dropdown.value, Is.EqualTo(selected));
            Assert.That(GameObject.Find("Dropdown List"), Is.Null);
        }
    }

    [UnityTest]
    public IEnumerator SettingsPreviewCancelAndApplyRememberVolume()
    {
        const string key = "BORis.Audio.Volume";
        bool hadVolume = PlayerPrefs.HasKey(key);
        float originalVolume = PlayerPrefs.GetFloat(key, 1f);
        float listenerVolume = AudioListener.volume;
        string[] displayKeys = { "BORis.Display.Width", "BORis.Display.Height", "BORis.Display.Mode" };
        bool[] existed = displayKeys.Select(PlayerPrefs.HasKey).ToArray();
        int[] previous = displayKeys.Select(k => PlayerPrefs.GetInt(k)).ToArray();
        try
        {
            Click(ButtonNamed("SettingsButton"));
            yield return null;
            var slider = Object.FindAnyObjectByType<Slider>();
            Assert.That(slider, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<Dropdown>().Length, Is.EqualTo(2));
            slider.value = .35f;
            Assert.That(AudioListener.volume, Is.EqualTo(.35f).Within(.001f));
            Click(ButtonNamed("BackButton"));
            Assert.That(AudioListener.volume, Is.EqualTo(originalVolume).Within(.001f));
            Click(ButtonNamed("SettingsButton"));
            slider.value = .6f;
            Click(ButtonNamed("ApplyButton"));
            Assert.That(PlayerPrefs.GetFloat(key), Is.EqualTo(.6f).Within(.001f));
            Click(ButtonNamed("SettingsButton"));
            Assert.That(slider.value, Is.EqualTo(.6f).Within(.001f));
            Click(ButtonNamed("BackButton"));
        }
        finally
        {
            if (hadVolume) PlayerPrefs.SetFloat(key, originalVolume); else PlayerPrefs.DeleteKey(key);
            for (int i = 0; i < displayKeys.Length; i++)
                if (existed[i]) PlayerPrefs.SetInt(displayKeys[i], previous[i]); else PlayerPrefs.DeleteKey(displayKeys[i]);
            PlayerPrefs.Save();
            AudioListener.volume = listenerVolume;
        }
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
