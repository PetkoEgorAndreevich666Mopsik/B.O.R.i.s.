using System;
using System.Collections.Generic;
using BORis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ServiceWindowSetup
{
    const string Materials = "Assets/_Project/Art/Materials/Workbench/";
    static Font font;

    [MenuItem("BORis/Install Service Window")]
    public static void Install()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Project/Scenes/WorkshopPrototype.unity")
            throw new InvalidOperationException("Open WorkshopPrototype before installing the service window.");
        if (UnityEngine.Object.FindFirstObjectByType<ServiceWindow>() != null)
            throw new InvalidOperationException("The service window is already installed; edit the existing scene objects.");
        WorkshopSession session = UnityEngine.Object.FindFirstObjectByType<WorkshopSession>();
        Workbench workbench = UnityEngine.Object.FindFirstObjectByType<Workbench>();
        if (session == null || workbench == null) throw new InvalidOperationException("Workshop objects are missing.");
        foreach (TextMesh label in workbench.GetComponentsInChildren<TextMesh>(true))
            if (label.text.Contains("ВЕРСТАК ПУСТ"))
            {
                Undo.RecordObject(label, "Update empty workbench hint");
                label.text = "ВЕРСТАК ПУСТ\nПоговорите с посетителем у окна";
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            }
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Material ivory = Material("WB_Ivory"), teal = Material("WB_Teal"), dark = Material("WB_Dark");
        Material orange = Material("WB_Orange"), rubber = Material("WB_Bristles"), cyan = Material("WB_Cyan");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Install BORis service window");

        foreach (string name in new[] { "OrderDesk", "RobotReception", "RobotDelivery", "ReceptionZoneLabel", "DeliveryZoneLabel", "WestWall" })
        {
            GameObject old = GameObject.Find(name);
            if (old == null) continue;
            Undo.RecordObject(old, "Replace old reception");
            old.SetActive(false);
        }
        var root = new GameObject("ServiceWindow");
        Undo.RegisterCreatedObjectUndo(root, "Create service window");
        root.transform.SetPositionAndRotation(new Vector3(-3.9f, 0f, -.65f), Quaternion.Euler(0f, 90f, 0f));
        ServiceWindow window = root.AddComponent<ServiceWindow>();

        Transform wall = Child(root.transform, "WallAroundOpening", Vector3.zero);
        Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/WorkshopWall.mat");
        Box(wall, "WallBelow", new Vector3(0, .39f, -.1f), new Vector3(2f, .78f, .2f), wallMaterial, true);
        Box(wall, "WallAbove", new Vector3(0, 2.77f, -.1f), new Vector3(2f, .66f, .2f), wallMaterial, true);
        Box(wall, "WallSouth", new Vector3(2.175f, 1.5f, -.1f), new Vector3(2.35f, 3f, .2f), wallMaterial, true);
        Box(wall, "WallNorth", new Vector3(-2.825f, 1.5f, -.1f), new Vector3(3.65f, 3f, .2f), wallMaterial, true);

        Box(root.transform, "ConveyorCabinet", new Vector3(0, .43f, .25f), new Vector3(1.98f, .86f, 2.5f), teal, true);
        Box(root.transform, "FrontPanelInset", new Vector3(0, .46f, 1.515f), new Vector3(1.75f, .53f, .04f), dark);
        for (int i = 0; i < 8; i++)
            Box(root.transform, "CabinetVent", new Vector3(-.61f + i * .175f, .43f, 1.545f), new Vector3(.075f, .24f, .025f), teal);
        Box(root.transform, "BeltBed", new Vector3(0, .9f, .1f), new Vector3(1.7f, .12f, 2.6f), rubber, true);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(root.transform, "SideRail", new Vector3(side * .94f, .96f, .1f), new Vector3(.15f, .22f, 2.7f), ivory, true);
            Box(root.transform, "RailAccent", new Vector3(side * .94f, 1.075f, .1f), new Vector3(.09f, .015f, 2.55f), orange);
            Box(root.transform, "DoorJamb", new Vector3(side * .97f, 1.72f, .08f), new Vector3(.2f, 1.6f, .35f), ivory, true);
            Box(root.transform, "ShutterGuide", new Vector3(side * .835f, 1.7f, .12f), new Vector3(.045f, 1.53f, .1f), dark);
            for (int bolt = 0; bolt < 4; bolt++)
            {
                var screw = Primitive(root.transform, "FrameBolt", PrimitiveType.Cylinder,
                    new Vector3(side * .98f, 1.13f + bolt * .39f, .27f), new Vector3(.055f, .016f, .055f), dark);
                screw.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
        }
        Box(root.transform, "ShutterHousing", new Vector3(0, 2.76f, .02f), new Vector3(2.18f, .57f, .54f), teal, true);
        Box(root.transform, "HousingFace", new Vector3(0, 2.76f, .3f), new Vector3(1.98f, .37f, .035f), ivory);
        Label(root.transform, "WindowTitle", new Vector3(0, 2.78f, .325f), new Vector2(900, 110), "B.O.R.i.S.  /  СЕРВИС", 35, .002f);

        Transform shutter = Child(root.transform, "LiftDoor", new Vector3(0, 1.7f, 0));
        Box(shutter, "DoorPanel", Vector3.zero, new Vector3(1.66f, 1.5f, .12f), dark, true);
        for (int i = 0; i < 12; i++)
            Box(shutter, "DoorRib", new Vector3(0, -.66f + i * .12f, .08f), new Vector3(1.6f, .095f, .06f), teal);
        Box(shutter, "DoorSafetyEdge", new Vector3(0, -.73f, .08f), new Vector3(1.66f, .065f, .1f), orange);
        Label(shutter, "DoorNumber", new Vector3(0, .02f, .125f), new Vector2(350, 150), "01", 80, .002f);

        var slats = new List<Transform>();
        for (int i = 0; i < 22; i++)
            slats.Add(Box(root.transform, "BeltSlat", new Vector3(0, .972f, -.95f + i * .1f), new Vector3(1.65f, .025f, .075f),
                i % 7 == 0 ? orange : dark).transform);
        var rollers = new List<Transform>();
        foreach (float z in new[] { -1.1f, 1.35f })
        {
            var roller = Primitive(root.transform, "ConveyorRoller", PrimitiveType.Cylinder, new Vector3(0, .86f, z),
                new Vector3(.22f, .82f, .22f), dark);
            roller.transform.localRotation = Quaternion.Euler(0, 0, 90);
            rollers.Add(roller.transform);
        }
        Transform platform = Child(root.transform, "RobotPlatform", new Vector3(0, .99f, .82f));
        platform.localRotation = Quaternion.Euler(0, 180, 0);
        BoxCollider zone = platform.gameObject.AddComponent<BoxCollider>();
        zone.size = new Vector3(1.62f, .08f, .92f);
        zone.center = new Vector3(0, -.045f, 0);
        Transform outside = Child(root.transform, "OutsideArrival", new Vector3(0, .99f, -.95f));
        outside.localRotation = platform.localRotation;

        Box(root.transform, "TerminalHousing", new Vector3(1.4f, 1.74f, .18f), new Vector3(.93f, 1.32f, .36f), ivory, true);
        Canvas screen = Panel(root.transform, "WindowDisplay", new Vector3(1.4f, 1.76f, .37f), new Vector2(470, 640), .0018f);
        var state = Text(screen.transform, "WindowState", new Vector2(0, 262), new Vector2(430, 90), "", 26, new Color(.45f, 1f, .9f));
        var contract = Text(screen.transform, "WindowContract", new Vector2(0, -42), new Vector2(415, 475), "", 22, Color.white);
        contract.alignment = TextAnchor.UpperLeft;
        contract.resizeTextForBestFit = true;
        contract.resizeTextMinSize = 17;
        contract.resizeTextMaxSize = 22;
        var lamp = Primitive(root.transform, "WindowIndicator", PrimitiveType.Sphere, new Vector3(1.4f, 2.49f, .25f), Vector3.one * .11f, cyan);
        Box(root.transform, "DispatchHousing", new Vector3(1.4f, .93f, .27f), new Vector3(.73f, .3f, .32f), teal, true);
        var button = Box(root.transform, "SendRobotButton", new Vector3(1.4f, .94f, .48f), new Vector3(.5f, .19f, .12f), orange, true);
        Label(root.transform, "SendRobotLabel", new Vector3(1.4f, 1.105f, .48f), new Vector2(450, 75), "ОТПРАВИТЬ", 27, .0017f);

        Canvas speech = Panel(root.transform, "RobotSpeech", new Vector3(0, 2.4f, 1f), new Vector2(650, 185), .0018f);
        var speechText = Text(speech.transform, "RobotSpeechText", Vector2.zero, new Vector2(610, 155), "", 24, Color.white);
        speechText.alignment = TextAnchor.MiddleLeft;
        speechText.resizeTextForBestFit = true;
        speechText.resizeTextMinSize = 20;
        speechText.resizeTextMaxSize = 24;
        AudioSource motor = Audio(root, true), signal = Audio(root, false);

        var windowHint = PlacementHint(root.transform, "WindowPlacementHint", new Vector3(0, 1.005f, .82f), new Vector2(1.58f, .88f), cyan);
        var tableHint = PlacementHint(workbench.transform, "RobotPlacementHint", new Vector3(0, 1.115f, .1f), new Vector2(1.25f, .95f), cyan);
        var serializedWindow = new SerializedObject(window);
        Set(serializedWindow, "session", session); Set(serializedWindow, "workbench", workbench);
        Set(serializedWindow, "shutter", shutter); Set(serializedWindow, "outside", outside); Set(serializedWindow, "platform", platform);
        Set(serializedWindow, "sendButton", button.GetComponent<Collider>());
        Set(serializedWindow, "stateLabel", state); Set(serializedWindow, "contractLabel", contract);
        Set(serializedWindow, "speech", speech); Set(serializedWindow, "speechLabel", speechText);
        Set(serializedWindow, "indicator", lamp.GetComponent<Renderer>()); Set(serializedWindow, "motor", motor); Set(serializedWindow, "signal", signal);
        SetArray(serializedWindow, "beltSlats", slats); SetArray(serializedWindow, "rollers", rollers);
        serializedWindow.ApplyModifiedProperties();
        var serializedSession = new SerializedObject(session);
        Set(serializedSession, "serviceWindow", window);
        serializedSession.ApplyModifiedProperties();
        var transport = new SerializedObject(session.Transport);
        Set(transport, "reception", platform); Set(transport, "deliveryZone", zone);
        Set(transport, "tablePlacementHint", tableHint); Set(transport, "windowPlacementHint", windowHint);
        transport.ApplyModifiedProperties();
        AuthorDialogue();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static void AuthorDialogue()
    {
        string[][] lines = {
            new[] { "MaintenanceOrder", "После пыльной смены даже обняться страшно. Только наклейку на плече побереги — это подарок.", "Теперь и обнять кого-нибудь можно. Спасибо, что привёл меня в порядок!" },
            new[] { "DriveRepairOrder", "Сегодня уронил чужой обед. Очень хочется завтра обойтись без этого.", "Рука больше не дёргается! Завтра обед доедет целым. Спасибо!" },
            new[] { "SolderingOrder", "Я уже перестал здороваться этой рукой. Все решили, что я обиделся.", "Снова могу помахать! Пойду объясню соседям, что не обижался." },
            new[] { "IndependentDriveOrder", "Самое обидное — мелочь держу, а что потяжелее обязательно роняю.", "Держит уверенно! Теперь снова можно доверять этой руке." },
            new[] { "IndependentContactOrder", "Думал, просто устал. Но после отдыха рука тоже не заработала.", "Получилось! А я уже привык всё делать одной рукой. Спасибо!" }
        };
        foreach (string[] line in lines)
        {
            var order = AssetDatabase.LoadAssetAtPath<OrderDefinition>($"Assets/_Project/Orders/{line[0]}.asset");
            if (order == null) throw new InvalidOperationException("Missing order: " + line[0]);
            Undo.RecordObject(order, "Author visitor dialogue");
            var serialized = new SerializedObject(order);
            serialized.FindProperty("personalRemark").stringValue = line[1];
            serialized.FindProperty("farewell").stringValue = line[2];
            serialized.ApplyModifiedProperties();
        }
    }

    static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat")
        ?? throw new InvalidOperationException("Missing workshop material: " + name);

    static Transform Child(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        return child;
    }

    static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collision = false)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collision = false)
        => Primitive(parent, name, PrimitiveType.Cube, position, scale, material, collision);

    static Canvas Panel(Transform parent, string name, Vector3 position, Vector2 size, float scale)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.Euler(0, 180, 0);
        obj.transform.localScale = Vector3.one * scale;
        ((RectTransform)obj.transform).sizeDelta = size;
        Canvas canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        obj.GetComponent<UnityEngine.UI.Image>().color = new Color(.035f, .075f, .08f, .97f);
        obj.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        return canvas;
    }

    static UnityEngine.UI.Text Text(Transform parent, string name, Vector2 position, Vector2 size, string value, int fontSize, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
        obj.transform.SetParent(parent, false);
        var rect = (RectTransform)obj.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = obj.GetComponent<UnityEngine.UI.Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.text = value;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    static void Label(Transform parent, string name, Vector3 position, Vector2 size, string value, int fontSize, float scale)
    {
        Canvas canvas = Panel(parent, name, position, size, scale);
        UnityEngine.Object.DestroyImmediate(canvas.GetComponent<UnityEngine.UI.Image>());
        Text(canvas.transform, name + "Text", Vector2.zero, size, value, fontSize, new Color(.85f, .93f, .91f));
    }

    static AudioSource Audio(GameObject root, bool loop)
    {
        AudioSource audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.loop = loop;
        audio.spatialBlend = 1f;
        audio.minDistance = 1.5f;
        audio.maxDistance = 8f;
        audio.volume = loop ? .5f : .65f;
        return audio;
    }

    static GameObject PlacementHint(Transform parent, string name, Vector3 position, Vector2 size, Material material)
    {
        Transform hint = Child(parent, name, position);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(hint, "Edge", new Vector3(side * size.x * .5f, 0, 0), new Vector3(.025f, .008f, size.y), material);
            Box(hint, "Edge", new Vector3(0, 0, side * size.y * .5f), new Vector3(size.x, .008f, .025f), material);
        }
        hint.gameObject.SetActive(false);
        return hint.gameObject;
    }

    static void Set(SerializedObject target, string field, UnityEngine.Object value) => target.FindProperty(field).objectReferenceValue = value;

    static void SetArray(SerializedObject target, string field, List<Transform> values)
    {
        var array = target.FindProperty(field);
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
