using System.Collections;
using BORis;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class WorkshopControlsTests
{
    readonly InputTestFixture input = new();
    Keyboard keyboard;
    CharacterController body;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        input.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/WorkshopPrototype.unity");
        body = Object.FindAnyObjectByType<PlayerController>().GetComponent<CharacterController>();
    }

    [UnityTest]
    public IEnumerator BalconyDoorStartsClosedAndMutesStreetSoundOnlyInside()
    {
        var door = GameObject.Find("Sliding balcony door").GetComponent<WorkshopDoor>();
        var ambience = Object.FindAnyObjectByType<WorkshopAmbience>().transform;
        var wind = ambience.Find("BalconyWind").GetComponent<AudioSource>();
        var traffic = ambience.Find("DistantTraffic").GetComponent<AudioSource>();

        PlacePlayer(4.15f, 1.2f, Vector3.right);
        yield return null;
        Assert.That(door.OpenFraction, Is.Zero);
        Assert.That(wind.volume, Is.Zero);
        Assert.That(traffic.volume, Is.Zero);

        body.Move(Vector3.right * 2f);
        Assert.That(body.transform.position.x, Is.LessThan(5.06f));

        PlacePlayer(5.95f, 1.2f, Vector3.left);
        yield return null;
        yield return null;
        Assert.That(wind.volume, Is.EqualTo(.22f).Within(.001f));
        Assert.That(traffic.volume, Is.EqualTo(.14f).Within(.001f));
        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.EqualTo(1f));
        body.Move(Vector3.left * .85f);
        Assert.That(body.transform.position.x, Is.EqualTo(5.1f).Within(.06f));
        Assert.That(door.TryToggle(body), Is.False, "The door must stay open while the player stands in the opening.");
        body.Move(Vector3.left * .95f);
        Assert.That(body.transform.position.x, Is.LessThan(4.5f));

        PlacePlayer(5.95f, .15f, Vector3.left);
        yield return null;
        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.Zero, "The parked leaf can be reached from the balcony.");

        PlacePlayer(4.15f, 1.2f, Vector3.right);
        yield return null;
        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.EqualTo(1f));
        Assert.That(traffic.volume, Is.GreaterThan(0f));
    }

    [UnityTest]
    public IEnumerator EntryDoorCanBeOpenedAndClosedFromTheHall()
    {
        var door = GameObject.Find("Workshop entry door").GetComponent<WorkshopDoor>();
        PlacePlayer(-3.05f, -3.35f, Vector3.left);
        yield return null;
        Assert.That(door.OpenFraction, Is.Zero);
        body.Move(Vector3.left * 2f);
        Assert.That(body.transform.position.x, Is.GreaterThan(-4.01f));

        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.EqualTo(1f));
        body.Move(Vector3.left * 2f);
        Assert.That(body.transform.position.x, Is.LessThan(-4.55f));
        Assert.That(door.TryToggle(body), Is.False, "The player is in the swing area.");

        PlacePlayer(-5.9f, -3.3f, new Vector3(.6f, 0f, -.72f));
        yield return null;
        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.Zero);
        PlacePlayer(-5.9f, -3.35f, Vector3.right);
        yield return null;
        yield return PressInteract();
        Assert.That(door.OpenFraction, Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator VentilationSwitchControlsOnlyItsLoopAndIntakeEffect()
    {
        var control = Object.FindAnyObjectByType<VentilationSwitch>();
        var ambience = Object.FindAnyObjectByType<WorkshopAmbience>().transform;
        var ventilation = ambience.Find("Ventilation").GetComponent<AudioSource>();
        var wind = ambience.Find("BalconyWind").GetComponent<AudioSource>();
        var particles = GameObject.Find("Extractor intake particles").GetComponent<ParticleSystem>();
        var indicator = control.transform.Find("Power indicator").GetComponent<Renderer>();
        PlacePlayer(control.transform.position.x, control.transform.position.z - .9f, Vector3.forward);
        yield return null;
        Assert.That(ventilation.mute, Is.True);
        Assert.That(particles.isStopped, Is.True);
        Assert.That(indicator.enabled, Is.False);

        yield return PressInteract();
        Assert.That(ventilation.mute, Is.False);
        Assert.That(particles.particleCount, Is.GreaterThan(0));
        Assert.That(indicator.enabled, Is.True);

        yield return PressInteract();
        Assert.That(ventilation.mute, Is.True);
        Assert.That(particles.particleCount, Is.Zero);
        Assert.That(particles.isStopped, Is.True);
        Assert.That(indicator.enabled, Is.False);
        Assert.That(wind.mute, Is.False);
    }

    void PlacePlayer(float x, float z, Vector3 facing)
    {
        body.enabled = false;
        body.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.LookRotation(facing));
        body.enabled = true;
        Physics.SyncTransforms();
    }

    IEnumerator PressInteract()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return new WaitForSeconds(.65f);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown() => input.TearDown();
}
