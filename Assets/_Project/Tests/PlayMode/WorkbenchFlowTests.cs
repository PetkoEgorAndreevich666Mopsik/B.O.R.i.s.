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

public sealed class WorkbenchFlowTests
{
    readonly InputTestFixture inputFixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    Workbench workbench;

    [UnitySetUp]
    public IEnumerator LoadPrototype()
    {
        inputFixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindFirstObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        workbench = Object.FindFirstObjectByType<Workbench>();
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.position = new Vector3(0, 0, -.7f);
        body.enabled = true;
        yield return null;
        yield return null;
        Assert.That(interaction.PointedWorkbench, Is.EqualTo(workbench), "Fixture must face an accessible table.");
    }

    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator EnterFreezesMovementAndLook_ExitRestoresViewAndWalking()
    {
        Transform view = player.View.transform;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(30, 10) });
        yield return null;
        yield return null;
        Assert.That(Quaternion.Angle(player.transform.rotation, Quaternion.identity), Is.GreaterThan(1f));
        Assert.That(Quaternion.Angle(view.localRotation, Quaternion.identity), Is.GreaterThan(.5f));
        Vector3 originalLocal = view.localPosition;
        Quaternion originalRotation = view.localRotation;
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Assert.That(workbench.IsOccupied, Is.True);
        Assert.That(view.parent.name, Is.EqualTo("CameraAnchor"));
        Assert.That(Cursor.visible, Is.True);
        Assert.That(view.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(Quaternion.Angle(view.localRotation, Quaternion.identity), Is.LessThan(.001f));
        var quest = Object.FindObjectsByType<Image>(FindObjectsSortMode.None).Single(i => i.name == "RepairQuest");
        Assert.That(quest.rectTransform.anchorMin, Is.EqualTo(new Vector2(0, 1)));
        Assert.That(quest.raycastTarget, Is.False);
        Vector3 position = player.transform.position;
        Quaternion rotation = player.transform.rotation;
        Quaternion benchView = view.rotation;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(200, 150) });
        yield return new WaitForSeconds(.15f);
        Assert.That(Vector3.Distance(player.transform.position, position), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(player.transform.rotation, rotation), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(view.rotation, benchView), Is.LessThan(.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return KeyPress(Key.Escape);
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(view.parent, Is.EqualTo(player.transform));
        Assert.That(Vector3.Distance(view.localPosition, originalLocal), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(view.localRotation, originalRotation), Is.LessThan(.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.15f);
        Assert.That(player.transform.position.z - position.z, Is.GreaterThan(.15f));
    }

    [UnityTest]
    public IEnumerator TenVisitsRestoreCameraAndDoNotMultiplyMovement()
    {
        for (int i = 0; i < 10; i++)
        {
            yield return KeyPress(Key.E);
            Assert.That(interaction.IsAtWorkbench, Is.True, "Visit " + i);
            yield return KeyPress(Key.Escape);
            Assert.That(interaction.IsAtWorkbench, Is.False, "Exit " + i);
            Assert.That(player.View.transform.parent, Is.EqualTo(player.transform));
        }
        float startZ = player.transform.position.z;
        float startTime = Time.time;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.15f);
        float expectedDistance = 3f * (Time.time - startTime);
        Assert.That(player.transform.position.z - startZ, Is.InRange(expectedDistance * .65f, expectedDistance * 1.35f));
    }

    [UnityTest]
    public IEnumerator WallBlocksWorkbenchInteraction()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1.5f, .1f);
        wall.transform.localScale = new Vector3(3, 3, .2f);
        Physics.SyncTransforms();
        yield return KeyPress(Key.E);
        Assert.That(interaction.PointedWorkbench, Is.Null);
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Object.Destroy(wall);
    }

    [UnityTest]
    public IEnumerator UIBlocksSceneInput_ExitButtonReturnsToShop()
    {
        var overlay = new GameObject("BlockingUI", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return KeyPress(Key.E);
        Assert.That(interaction.PointedWorkbench, Is.Null);
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Object.Destroy(overlay);
        yield return null;
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "ExitBenchButton");
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
        Assert.That(interaction.IsAtWorkbench, Is.False, "Actual UI pointer click must leave bench.");
        Assert.That(player.View.transform.parent, Is.EqualTo(player.transform));
    }

    [UnityTest]
    public IEnumerator DisabledWorkbenchRestoresCameraAndInput()
    {
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        workbench.gameObject.SetActive(false);
        yield return null;
        yield return null;
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(player.View.transform.parent, Is.EqualTo(player.transform));
    }

    [UnityTest]
    public IEnumerator DestroyedWorkbenchRestoresCameraAndWalking()
    {
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
        Object.Destroy(workbench.gameObject);
        yield return null;
        yield return null;
        Assert.That(interaction.IsAtWorkbench, Is.False);
        Assert.That(player.View.transform.parent, Is.EqualTo(player.transform));
        float startZ = player.transform.position.z;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.15f);
        Assert.That(player.transform.position.z - startZ, Is.GreaterThan(.15f));
    }

    [UnityTearDown]
    public IEnumerator UnloadPrototype()
    {
        var scene = SceneManager.GetActiveScene();
        var empty = SceneManager.CreateScene("TestCleanup");
        SceneManager.SetActiveScene(empty);
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        inputFixture.TearDown();
    }
}
