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
    int moveStep;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture.Setup();
        moveStep = 0;
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
        player.transform.position = Object.FindAnyObjectByType<Workbench>().transform.TransformPoint(new Vector3(0f, 0f, -2.2f));
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
        if (key == Key.Escape) yield return new WaitForSeconds(.5f);
        if (key == Key.E) yield return new WaitForSeconds(1.2f);
    }

    Vector2 Screen(Transform target) => player.View.WorldToScreenPoint(target.position);

    Vector2 VisiblePartScreen(RepairPart part)
    {
        Physics.SyncTransforms();
        float reach = 3f + Object.FindAnyObjectByType<Workbench>().CameraOffset;
        float[] samples = { 0f, .75f, -.75f };
        foreach (var collider in part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
        foreach (float x in samples)
        foreach (float y in samples)
        foreach (float z in samples)
        {
            Vector3 point = collider.bounds.center + Vector3.Scale(collider.bounds.extents, new Vector3(x, y, z));
            Vector3 screen = player.View.WorldToScreenPoint(point);
            if (screen.z <= 0f || screen.x < 0f || screen.y < 0f
                || screen.x > UnityEngine.Screen.width || screen.y > UnityEngine.Screen.height) continue;
            if (Physics.Raycast(player.View.ScreenPointToRay(screen), out var hit, reach,
                ~(1 << player.gameObject.layer), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<RepairPart>() == part)
            {
                var ui = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(
                    new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = screen }, ui);
                if (ui.Count == 0) return screen;
            }
        }
        Assert.Fail("No unobstructed pointer ray reaches " + part.name + " at " + part.transform.position);
        return Vector2.zero;
    }

    string PointerDiagnostics(RepairPart part, Vector2 source)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        object Field(string name) => typeof(InteractionController).GetField(name,flags)?.GetValue(interaction);
        var actionMap = Field("bench") as InputActionMap;
        var primary = Field("primary") as InputAction;
        var selected = Field("selectedTool") as RepairTool;
        var ui = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem != null)
            eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = source },ui);
        float reach = 3f + Object.FindAnyObjectByType<Workbench>().CameraOffset;
        string hits = string.Join(", ",Physics.RaycastAll(player.View.ScreenPointToRay(source),reach,
                ~(1 << player.gameObject.layer),QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance)
            .Select(hit => $"{hit.collider.name}({hit.collider.GetType().Name}, part={hit.collider.GetComponentInParent<RepairPart>()?.name}, d={hit.distance:F3})"));
        bool canMove = robot.CanMove(part,out string moveReason);
        bool canInstall = robot.CanPlace(part,true,out string installReason);
        return $"source={source}, mouse={mouse.position.ReadValue()}, size={UnityEngine.Screen.width}x{UnityEngine.Screen.height}, "
            + $"atBench={interaction.IsAtWorkbench}, inspecting={interaction.IsInspecting}, benchMap={actionMap?.enabled}, "
            + $"primaryEnabled={primary?.enabled}, primary={primary?.ReadValue<float>()}, mouseDown={mouse.leftButton.isPressed}, "
            + $"selectedTool={(selected != null ? selected.name : "Hand")}, held={interaction.HeldPart?.name}, pointed={interaction.PointedObject?.name}, "
            + $"installed={part.IsInstalled}, released={part.IsReleased}, dragging={part.IsDragging}, supported={part.IsSupported}, "
            + $"body={part.Body.position:F3}, velocity={part.Body.linearVelocity:F3}, kinematic={part.Body.isKinematic}, "
            + $"anchor={part.InstallationAnchor.position:F3}, anchorDistance={Vector3.Distance(part.Body.position,part.InstallationAnchor.position):F3}, "
            + $"dropAnchor={interaction.DropAnchor?.name}, heldTarget={Field("heldTarget")}, dragged={Field("dragged")}, "
            + $"canMove={canMove} ({moveReason}), canInstall={canInstall} ({installReason}), feedback={interaction.Feedback}, "
            + $"camera={player.View.transform.position:F3}, ui=[{string.Join(", ",ui.Select(hit => hit.gameObject.name))}], rayHits=[{hits}]";
    }

    IEnumerator Pointer(Vector2 position, bool down)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState {position=position,buttons=down ? (ushort)1 : (ushort)0});
        yield return null; yield return null;
    }

    IEnumerator Turn(ScrewFastener screw, bool loosen)
    {
        yield return KeyPress(Key.Digit2);
        if (!loosen && screw.IsRemoved)
        {
            yield return Pointer(Screen(screw.transform), true);
            Assert.That(screw.IsRemoved, Is.False, "The first click must insert " + screw.name);
            Assert.That(screw.IsTight, Is.False, "Insertion must not tighten the screw automatically.");
            Assert.That(interaction.Screwdriver.Target, Is.Null);
            yield return Pointer(Screen(screw.transform), false);
        }
        yield return Pointer(Screen(screw.transform),true);
        Assert.That(interaction.Screwdriver.Target, Is.SameAs(screw), "Pointer must reach " + screw.name);
        yield return Pointer(Screen(screw.transform)+Vector2.right*45f,true);
        for(int i=1;i<=52;i++)
        {
            float angle = i*15f*Mathf.Deg2Rad*(loosen ? 1 : -1);
            yield return Pointer(Screen(screw.transform)+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*45f,true);
            if (loosen && screw.IsRemoved)
            {
                Assert.That(interaction.Screwdriver.Target, Is.Null, "Extraction ends the captured gesture.");
                break;
            }
            Assert.That(interaction.Screwdriver.Target, Is.SameAs(screw), $"{screw.name}, sample {i}, screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, pointer {mouse.position.ReadValue()}");
        }
        yield return Pointer(Screen(screw.transform),false);
        Assert.That(screw.Tightness, Is.EqualTo(loosen ? 0f : 1f).Within(.001f), screw.name);
    }

    IEnumerator Move(RepairPart part, bool install)
    {
        int step = ++moveStep;
        yield return KeyPress(Key.Digit1);
        Vector2 source = part.IsInstalled ? Screen(part.transform) : VisiblePartScreen(part);
        string before = PointerDiagnostics(part,source);
        yield return Pointer(source,true);
        Assert.That(interaction.HeldPart, Is.SameAs(part),
            $"Move {step}: hand must reach {part.name}, requested install={install}. Before: {before}. After: {PointerDiagnostics(part,source)}");
        Transform anchor = install ? part.InstallationAnchor : part.TrayAnchor;
        yield return Pointer(Screen(anchor),true);
        if (install)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (interaction.DropAnchor != anchor && Time.realtimeSinceStartup < deadline) yield return null;
        }
        else yield return new WaitForSeconds(.5f);
        yield return Pointer(Screen(anchor),false);
        Assert.That(interaction.HeldPart, Is.Null);
        Assert.That(part.IsInstalled, Is.EqualTo(install), interaction.Feedback);
        if (install) Assert.That(Vector3.Distance(part.transform.position,anchor.position), Is.LessThan(.001f));
        else
        {
            Assert.That(part.Body.isKinematic, Is.False);
            yield return new WaitForSeconds(.5f);
        }
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
        Assert.That(last.IsLoose, Is.True);
        Assert.That(robot.ScrewTray.Count, Is.EqualTo(cover.Screws.Length));
        Assert.That(robot.TryInsertScrew(last, out string reason), Is.True, reason);
        robot.Turn(last, 45f);
        Assert.That(cover.IsReleased, Is.False);
        Assert.That(Vector3.Distance(cover.transform.position, cover.InstallationAnchor.position), Is.LessThan(.001f));
        Assert.That(robot.CanMove(cover, out _), Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator CoverReassemblyConsumesCollectedScrews_AndStillRequiresTightening()
    {
        var cover = robot.Cover;
        Assert.That(robot.ScrewTray.Count, Is.Zero);
        foreach (var screw in cover.Screws) robot.Turn(screw, -10000f);
        Assert.That(robot.ScrewTray.Count, Is.EqualTo(4));
        Assert.That(cover.IsReleased, Is.True);
        Assert.That(cover.IsInstalled, Is.True, "The last screw must not take the cover off automatically.");
        Assert.That(robot.TryPlace(cover, false, out _), Is.True);
        Assert.That(robot.CanInsertScrew(cover.Screws[0], out _), Is.False);
        Assert.That(robot.TryInsertScrew(cover.Screws[0], out _), Is.False);
        Assert.That(robot.ScrewTray.Count, Is.EqualTo(4));
        Assert.That(robot.TryPlace(cover, true, out _), Is.True);
        foreach (var screw in cover.Screws)
        {
            Assert.That(robot.TryInsertScrew(screw, out string reason), Is.True, reason);
            Assert.That(screw.IsRemoved || screw.IsTight, Is.False);
        }
        Assert.That(robot.ScrewTray.Count, Is.Zero);
        Assert.That(RobotRepair.FullyTightened(cover), Is.False);
        Assert.That(robot.RunTest(out _), Is.False, "Inserted screws do not count as a completed assembly.");
        foreach (var screw in cover.Screws) robot.Turn(screw, 10000f);
        Assert.That(RobotRepair.FullyTightened(cover), Is.True);
        Assert.That(robot.ScrewTray.Count, Is.Zero);
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
        Assert.That(robot.Cover.Body.isKinematic,Is.False,"A removed cover must remain a physical object.");
        yield return ToggleConnector();
        foreach(var screw in oldDrive.Screws) yield return Turn(screw,true);
        yield return Move(oldDrive,false);
        yield return KeyPress(Key.Escape);
        yield return KeyPress(Key.E);
        Assert.That(robot.Cover.IsInstalled,Is.False);
        Assert.That(oldDrive.IsInstalled,Is.False);
        Assert.That(robot.InstalledDrive,Is.Null);
        Assert.That(robot.Connector.IsConnected,Is.False);
        Assert.That(oldDrive.Body.isKinematic,Is.False);
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
        yield return KeyPress(Key.F1);
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
    public IEnumerator FreeDropKeepsCoverDetachedAndLooseScrewsCannotTighten()
    {
        Release(robot.Cover);
        yield return Pointer(Screen(robot.Cover.transform),true);
        Assert.That(interaction.HeldPart,Is.SameAs(robot.Cover));
        yield return Pointer(new Vector2(20,300),true);
        yield return Pointer(new Vector2(20,300),false);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.False,"Releasing away from a socket must keep the cover removed.");
        Assert.That(robot.Cover.Body.isKinematic,Is.False);
        Assert.That(robot.Cover.GetComponentsInChildren<Collider>().All(c=>c.enabled),Is.True);
        foreach(var screw in robot.Cover.Screws) Assert.That(robot.Turn(screw,720f),Is.Zero);
        Assert.That(robot.Cover.Screws.All(s=>s.Tightness == 0f),Is.True);
    }

    [UnityTest]
    public IEnumerator EscFocusLossAndToolChangeReleaseExactlyOnePhysicalDrag()
    {
        Release(robot.Cover);
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        yield return KeyPress(Key.Escape);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.False);
        Assert.That(robot.Cover.IsDragging,Is.False);
        Assert.That(robot.Cover.Body.isKinematic,Is.False);
        Assert.That(robot.Cover.Screws.All(s=>s.Tightness == 0f),Is.True);
        yield return Pointer(Vector2.zero,false);
        if (!interaction.IsAtWorkbench) yield return KeyPress(Key.E);
        robot.Cover.RecoverToTable();
        yield return new WaitForSeconds(.3f);
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return Pointer(Screen(robot.Cover.TrayAnchor),true);
        interaction.SendMessage("OnApplicationFocus",false);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.False);
        Assert.That(robot.Cover.IsDragging,Is.False);
        Assert.That(robot.Cover.Body.isKinematic,Is.False);
        yield return Pointer(Vector2.zero,false);
        robot.Cover.RecoverToTable();
        yield return new WaitForSeconds(.3f);
        yield return Pointer(Screen(robot.Cover.transform),true);
        yield return KeyPress(Key.Digit2);
        Assert.That(interaction.HeldPart,Is.Null);
        Assert.That(robot.Cover.IsInstalled,Is.False);
        Assert.That(robot.Cover.IsDragging,Is.False);
        Assert.That(robot.Cover.Body.isKinematic,Is.False);
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
        foreach(var screw in oldDrive.Screws)
        {
            Assert.That(robot.TryInsertScrew(screw, out string insertReason), Is.True, insertReason);
            robot.Turn(screw,10000f);
        }
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
        Assert.That(robot.Cover.IsInstalled,Is.False,"UI cancellation releases the physical part instead of reinstalling it.");
        Object.Destroy(canvas);
        robot.Cover.RecoverToTable();
        yield return new WaitForSeconds(.3f);
        yield return Pointer(VisiblePartScreen(robot.Cover),true);
        Assert.That(interaction.HeldPart,Is.SameAs(robot.Cover));
        Vector2 installationPoint = Screen(robot.Cover.InstallationAnchor);
        yield return Pointer(installationPoint,true);
        float deadline = Time.realtimeSinceStartup + 2f;
        while (interaction.DropAnchor != robot.Cover.InstallationAnchor && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(interaction.DropAnchor,Is.SameAs(robot.Cover.InstallationAnchor),
            "Installation must already be available before introducing the obstacle. " + PointerDiagnostics(robot.Cover,installationPoint));
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = Vector3.Lerp(player.View.transform.position,robot.Cover.InstallationAnchor.position,.6f);
        obstacle.transform.localScale = Vector3.one*.4f;
        Physics.SyncTransforms();
        yield return Pointer(installationPoint,true);
        Assert.That(interaction.DropAnchor,Is.Null,"The new obstacle must invalidate the existing installation preview.");
        yield return Pointer(installationPoint,false);
        Assert.That(robot.Cover.IsInstalled,Is.False,"An obstacle must still block installation.");
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
