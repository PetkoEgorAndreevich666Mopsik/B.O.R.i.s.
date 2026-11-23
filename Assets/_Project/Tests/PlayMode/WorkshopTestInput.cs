using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class WorkshopTestInput
{
    public static IEnumerator Interact(float settle = 1.2f)
    {
        var mouse = Mouse.current;
        Vector2 position = mouse.position.ReadValue();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
        yield return new WaitForSeconds(settle);
    }
}
