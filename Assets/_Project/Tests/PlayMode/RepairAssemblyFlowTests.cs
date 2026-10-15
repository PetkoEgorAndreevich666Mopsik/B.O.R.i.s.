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

public sealed class RepairAssemblyFlowTests
{
    readonly InputTestFixture fixture = new();
    Keyboard keyboard;
    Mouse mouse;
    PlayerController player;
    InteractionController interaction;
    RobotRepair robot;
    RepairPart oldDrive, newDrive;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/RepairPrototype.unity");
        player = Object.FindAnyObjectByType<PlayerController>();
        interaction = player.GetComponent<InteractionController>();
        robot = Object.FindAnyObjectByType<RobotRepair>();
        oldDrive = robot.InstalledDrive;
        newDrive = robot.GetComponentsInChildren<RepairPart>().Single(p => p.Kind == RepairPartKind.Drive && !p.IsInstalled);
        var body = player.GetComponent<CharacterController>();
        body.enabled = false;
        player.transform.position = new Vector3(0,0,-.7f);
        body.enabled = true;
        yield return null;
        yield return KeyPress(Key.E);
        Assert.That(interaction.IsAtWorkbench, Is.True);
    }

    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
    }

    Vector2 Screen(Transform target) => player.View.WorldToScreenPoint(target.position);

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState {position=position,buttons=down ? (ushort)1 : (ushort)0});
        yield return null; yield return null;
    }

    IEnumerator Turn(ScrewFastener screw, bool loosen)
    {
        yield return KeyPress(Key.Digit2);
        yield return Pointer(Screen(screw.transform),true);
        Assert.That(interaction.Screwdriver.Target, Is.SameAs(screw), "Pointer must reach " + screw.name);
        yield return Pointer(Screen(screw.transform)+Vector2.right*45f,true);
        for(int i=1;i<=52;i++)
        {
            float angle = i*15f*Mathf.Deg2Rad*(loosen ? 1 : -1);
            yield return Pointer(Screen(screw.transform)+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*45f,true);
            Assert.That(interaction.Screwdriver.Target, Is.SameAs(screw), $"{screw.name}, sample {i}, screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, pointer {mouse.position.ReadValue()}");
        }
        yield return Pointer(Screen(screw.transform),false);
        Assert.That(screw.Tightness, Is.EqualTo(loosen ? 0f : 1f).Within(.001f), screw.name);
    }

    IEnumerator Move(RepairPart part, bool install)
    {
        yield return KeyPress(Key.Digit1);
        yield return Pointer(Screen(part.transform),true);
        Assert.That(interaction.HeldPart, Is.SameAs(part), "Hand must reach " + part.name);
        Transform anchor = install ? part.InstallationAnchor : part.TrayAnchor;
        yield return Pointer(Screen(anchor),true);
        yield return Pointer(Screen(anchor),false);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(part.IsInstalled, Is.EqualTo(install), interaction.Feedback);
        Assert.That(Vector3.Distance(part.transform.position,anchor.position), Is.LessThan(.001f));
    }

    IEnumerator ToggleConnector()
    {
        yield return KeyPress(Key.Digit1);
        Transform plug = robot.Connector.transform.Find("Plug");
        bool connected = robot.Connector.IsConnected;
        yield return Pointer(Screen(plug),true);
        yield return Pointer(Screen(plug),false);
        Assert.That(robot.Connector.IsConnected, Is.EqualTo(!connected), interaction.Feedback);
    }

    void Release(RepairPart part)
    {
        foreach(var screw in part.Screws) robot.Turn(screw,-10000f);
    }

    void OpenCover()
    {
        Release(robot.Cover);
        Assert.That(robot.TryPlace(robot.Cover,false,out string reason),Is.True,reason);
        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator LastScrewReleasesCover_WithSoundAndVisibleGap_AndTighteningSeatsIt()
    {
        var cover = robot.Cover;
        for (int i = 0; i < cover.Screws.Length - 1; i++) robot.Turn(cover.Screws[i], -10000f);
        Assert.That(cover.IsReleased, Is.False);
        var last = cover.Screws[cover.Screws.Length - 1];
        robot.Turn(last, -10000f);
        Assert.That(cover.IsReleased, Is.True);
        Assert.That(cover.IsInstalled, Is.True, "Release must not remove the cover automatically.");
        Assert.That(Vector3.Distance(cover.transform.position, cover.InstallationAnchor.position), Is.EqualTo(.035f).Within(.001f));
        Assert.That(cover.GetComponent<AudioSource>().isPlaying, Is.True);
        Assert.That(last.transform.Find("ReleasedBand").gameObject.activeSelf, Is.True);
        robot.Turn(last, 45f);
        Assert.That(cover.IsReleased, Is.False);
        Assert.That(Vector3.Distance(cover.transform.position, cover.InstallationAnchor.position), Is.LessThan(.001f));
        Assert.That(robot.CanMove(cover, out _), Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FullRepairUsesMouseForEveryScrewConnectorAndPart()
    {
        Assert.That(robot.Cover.Screws.Length,Is.EqualTo(4));
        Assert.That(oldDrive.Screws.Length,Is.EqualTo(2));
        yield return TestButton();
        Assert.That(robot.TestResult,Is.EqualTo(RobotTestResult.Faulty));
        var dirt = robot.Cover.GetComponentsInChildren<DirtPatch>().Single(p=>!p.RequiresOpenCover);
        yield return KeyPress(Key.Digit3);
        Vector2 DirtPoint(float u) => player.View.WorldToScreenPoint(dirt.transform.TransformPoint(new Vector3(u-.5f,0,0)));
        yield return Pointer(DirtPoint(.2f),true);
        yield return Pointer(DirtPoint(.8f),true);
        yield return Pointer(DirtPoint(.8f),false);
        Assert.That(dirt.Cleanliness,Is.GreaterThan(0f));
        foreach(var screw in robot.Cover.Screws) yield return Turn(screw,true);
        yield return Move(robot.Cover,false);
        Assert.That(Vector3.Dot(robot.Cover.transform.forward,Vector3.up),Is.GreaterThan(.99f),"Inside face must lie upward.");
        yield return ToggleConnector();
        foreach(var screw in oldDrive.Screws) yield return Turn(screw,true);
        yield return Move(oldDrive,false);
        yield return KeyPress(Key.Escape);
        yield return KeyPress(Key.E);
        Assert.That(robot.Cover.IsInstalled,Is.False);
        Assert.That(oldDrive.IsInstalled,Is.False);
        Assert.That(robot.InstalledDrive,Is.Null);
        Assert.That(robot.Connector.IsConnected,Is.False);
        Assert.That(Vector3.Distance(oldDrive.transform.position,oldDrive.TrayAnchor.position),Is.LessThan(.001f));
        yield return Move(newDrive,true);
        foreach(var screw in newDrive.Screws) yield return Turn(screw,false);
        yield return ToggleConnector();
        yield return Move(robot.Cover,true);
        foreach(var screw in robot.Cover.Screws) yield return Turn(screw,false);
        Assert.That(robot.InstalledDrive,Is.SameAs(newDrive));
        Assert.That(oldDrive.IsInstalled,Is.False);
        Assert.That(robot.Connector.IsConnected,Is.True);
        Assert.That(RobotRepair.FullyTightened(robot.Cover),Is.True);
        Assert.That(robot.AssemblyHint,Does.Contain("Сборка завершена"));
        yield return KeyPress(Key.Escape);
        yield return KeyPress(Key.E);
        Assert.That(robot.InstalledDrive,Is.SameAs(newDrive));
        Assert.That(robot.Cover.IsInstalled,Is.True);
        yield return TestButton();
        Assert.That(robot.TestResult,Is.EqualTo(RobotTestResult.Passed));
        var restart=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="RestartButton");
        Vector2 resetPoint=RectTransformUtility.WorldToScreenPoint(null,restart.transform.position);
        yield return Pointer(resetPoint,true);
        yield return Pointer(resetPoint,false);
        robot=interaction.Robot;
        oldDrive=robot.InstalledDrive;
        newDrive=robot.GetComponentsInChildren<RepairPart>().Single(p=>p.Kind==RepairPartKind.Drive && !p.IsInstalled);
        Assert.That(oldDrive.Faulty,Is.True);
        Assert.That(robot.TestResult,Is.EqualTo(RobotTestResult.NotTested));
        foreach(var screw in robot.Cover.Screws) yield return Turn(screw,true);
        yield return Move(robot.Cover,false);
        yield return ToggleConnector();
        foreach(var screw in oldDrive.Screws) yield return Turn(screw,true);
        yield return Move(oldDrive,false);
        yield return Move(newDrive,true);
        foreach(var screw in newDrive.Screws) yield return Turn(screw,false);
        yield return ToggleConnector();
        yield return Move(robot.Cover,true);
        foreach(var screw in robot.Cover.Screws) yield return Turn(screw,false);
        yield return TestButton();
        Assert.That(robot.TestResult,Is.EqualTo(RobotTestResult.Passed),"Second repair must also finish through actual controls.");
    }

    IEnumerator TestButton()
    {
        var button=Object.FindAnyObjectByType<Workbench>().TestButton;
        yield return Pointer(Screen(button.transform),true);
        Assert.That(robot.IsTesting,Is.True,"Physical button must start the test.");
        yield return Pointer(Screen(button.transform),false);
        float deadline=Time.realtimeSinceStartup+3f;
        while(robot.IsTesting && Time.realtimeSinceStartup<deadline) yield return null;
        Assert.That(robot.IsTesting,Is.False);
    }

    [UnityTest]
    public IEnumerator RulesRejectSecuredPartsConnectedDriveAndOccupiedSocket()
    {
        Vector3 start = robot.Cover.transform.position;
        Assert.That(robot.TryPlace(robot.Cover,false,out string reason),Is.False);
        Assert.That(reason,Does.Contain("винты"));
        Assert.That(robot.Cover.transform.position,Is.EqualTo(start));
        Assert.That(robot.ToggleConnection(robot.Connector,out reason),Is.False);
        Assert.That(robot.Turn(oldDrive.Screws[0],-180f),Is.Zero);
        Assert.That(robot.Turn(newDrive.Screws[0],180f),Is.Zero);
        OpenCover();
        Release(oldDrive);
        Assert.That(robot.TryPlace(oldDrive,false,out reason),Is.False);
        Assert.That(reason,Does.Contain("разъём"));
        Assert.That(robot.ToggleConnection(robot.Connector,out reason),Is.True);
        Vector3 replacementPose = newDrive.transform.position;
        Assert.That(robot.TryPlace(newDrive,true,out reason),Is.False);
        Assert.That(reason,Does.Contain("занято"));
        Assert.That(newDrive.transform.position,Is.EqualTo(replacementPose));
        Assert.That(oldDrive.IsInstalled,Is.True);
        Assert.That(newDrive.IsInstalled,Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InvalidDropReturnsCoverAndTrayScrewsCannotTighten()
    {
        Release(robot.Cover);
        Vector3 start = robot.Cover.transform.position;
        yield return Pointer(Screen(robot.Cover.transform),true);
        Assert.That(interaction.HeldPart,Is.SameAs(robot.Cover));
        yield return Pointer(new Vector2(20,300),true);
        yield return Pointer(new Vector2(20,300),false);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.True);
        Assert.That(robot.Cover.transform.position,Is.EqualTo(start));
        Assert.That(robot.Cover.GetComponentsInChildren<Collider>().All(c=>c.enabled),Is.True);
        yield return Move(robot.Cover,false);
        foreach(var screw in robot.Cover.Screws) Assert.That(robot.Turn(screw,720f),Is.Zero);
        Assert.That(robot.Cover.Screws.All(s=>s.Tightness == 0f),Is.True);
    }

    [UnityTest]
    public IEnumerator EscFocusLossAndToolChangeCancelExactlyOneDrag()
    {
        Release(robot.Cover);
        Vector3 source = robot.Cover.transform.position;
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        yield return KeyPress(Key.Escape);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.transform.position,Is.EqualTo(source));
        Assert.That(robot.Cover.Screws.All(s=>s.Tightness == 0f),Is.True);
        yield return Pointer(Vector2.zero,false);
        yield return KeyPress(Key.E);
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        interaction.SendMessage("OnApplicationFocus",false);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.transform.position,Is.EqualTo(source));
        yield return Pointer(Vector2.zero,false);
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return KeyPress(Key.Digit2);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.transform.position,Is.EqualTo(source));
        Assert.That(interaction.ScrewdriverSelected,Is.True);
        Assert.That(newDrive.IsInstalled,Is.False);
    }

    [UnityTest]
    public IEnumerator FaultyDriveCanReturnAndCoverRequiresSecuredConnectedDrive()
    {
        OpenCover();
        Assert.That(robot.ToggleConnection(robot.Connector,out _),Is.True);
        Release(oldDrive);
        Assert.That(robot.TryPlace(oldDrive,false,out _),Is.True);
        Assert.That(robot.TryPlace(robot.Cover,true,out string reason),Is.False);
        Assert.That(reason,Does.Contain("установите привод"));
        Assert.That(robot.TryPlace(oldDrive,true,out _),Is.True);
        Assert.That(oldDrive.Faulty,Is.True);
        Assert.That(robot.TryPlace(robot.Cover,true,out reason),Is.False);
        Assert.That(reason,Does.Contain("винта привода"));
        Assert.That(robot.ToggleConnection(robot.Connector,out reason),Is.False);
        foreach(var screw in oldDrive.Screws) robot.Turn(screw,10000f);
        Assert.That(robot.TryPlace(robot.Cover,true,out reason),Is.False);
        Assert.That(reason,Does.Contain("разъём"));
        Assert.That(robot.ToggleConnection(robot.Connector,out reason),Is.True);
        Assert.That(robot.TryPlace(robot.Cover,true,out reason),Is.True,reason);
        Assert.That(robot.InstalledDrive,Is.SameAs(oldDrive));
        Assert.That(newDrive.IsInstalled,Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator OverlayAndPhysicalObstacleBlockPartPlacement()
    {
        Release(robot.Cover);
        yield return Pointer(Screen(robot.Cover.transform),true);
        var canvas = new GameObject("BlockingUI",typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var image = new GameObject("Overlay",typeof(RectTransform),typeof(UnityEngine.UI.Image));
        image.transform.SetParent(canvas.transform,false);
        var rect = image.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),false);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.True);
        Object.Destroy(canvas);
        yield return null;
        yield return Pointer(Screen(robot.Cover.transform),true);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = Vector3.Lerp(player.View.transform.position,robot.Cover.TrayAnchor.position,.6f);
        obstacle.transform.localScale = Vector3.one*.4f;
        Physics.SyncTransforms();
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),false);
        Assert.That(robot.Cover.IsInstalled,Is.True);
        Assert.That(interaction.HeldPart,Is.Null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(SceneManager.CreateScene("AssemblyCleanup"));
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return null;
        fixture.TearDown();
    }
}
