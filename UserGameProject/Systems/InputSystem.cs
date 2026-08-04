using CSharpGameEngine.Platform.Windows;

namespace UserGameProject.Systems;

/// <summary>
/// The ECS input abstraction called for by the FirstPerson-port requirements: this is the only
/// place in the game loop that touches <see cref="Win32Window"/> or a virtual-key code. Every
/// other system reads player intent exclusively through the <see cref="InputState"/> component
/// this writes, so they stay testable with a hand-built <see cref="InputState"/> and would need no
/// changes at all if the input backend were ever swapped out.
/// </summary>
public static class InputSystem
{
    private const int VkSpace = 0x20;

    /// <summary><paramref name="wasJumpKeyDown"/> is the caller's persisted edge-detection state from the previous frame (mirrors the pre-ECS loop in the old Program.cs) - kept explicit rather than static/hidden so this method stays a pure function of its inputs.</summary>
    public static void Update(Win32Window window, ref InputState state, ref bool wasJumpKeyDown)
    {
        bool jumpKeyDown = window.IsKeyDown(VkSpace);
        state.JumpPressed = jumpKeyDown && !wasJumpKeyDown;
        wasJumpKeyDown = jumpKeyDown;

        state.MoveForward = window.IsKeyDown('W');
        state.MoveBack = window.IsKeyDown('S');
        state.MoveLeft = window.IsKeyDown('A');
        state.MoveRight = window.IsKeyDown('D');

        (float deltaX, float deltaY) = window.ConsumeMouseDelta();
        state.LookDeltaX = deltaX;
        state.LookDeltaY = deltaY;
    }
}
