using CSharpGameEngine.Entities;
using CSharpGameEngine.Platform.Windows;

namespace UserGameProject.Systems;

/// <summary>
/// The ECS input abstraction called for by the FirstPerson-port requirements: this is the only
/// place in the game loop that touches <see cref="Win32Window"/> or a virtual-key code. Every
/// other system reads player intent exclusively through the <see cref="InputState"/> component
/// this writes, so they stay testable with a hand-built <see cref="InputState"/> and would need no
/// changes at all if the input backend were ever swapped out.
/// </summary>
[GameSystem]
public static class InputSystem
{
    private const int VkSpace = 0x20;

    [Update]
    public static void Poll(ref InputState state, in PlayerTag _, [Context] Win32Window window)
    {
        bool jumpKeyDown = window.IsKeyDown(VkSpace);
        state.JumpPressed = jumpKeyDown && !state.WasJumpKeyDown;
        state.WasJumpKeyDown = jumpKeyDown;

        state.MoveForward = window.IsKeyDown('W');
        state.MoveBack = window.IsKeyDown('S');
        state.MoveLeft = window.IsKeyDown('A');
        state.MoveRight = window.IsKeyDown('D');

        (float deltaX, float deltaY) = window.ConsumeMouseDelta();
        state.LookDeltaX = deltaX;
        state.LookDeltaY = deltaY;
    }
}
