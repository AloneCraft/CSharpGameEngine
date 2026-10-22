# $projectname$

A C# Game Engine game created from the Third Person template (a port of Unreal Engine's Third Person template).

- Run: press F5, or `dotnet run` in this folder.
- Move: W A S D or the arrow keys, or the left stick.
- Look: the mouse, or the right stick.
- Jump: Space, or the bottom face button.

The level is `Scenes/Lvl_ThirdPerson.json` and the character is `Scenes/ThirdPersonCharacter.json`; open them in the Scene Designer or edit the JSON. The controls are declared in `Gameplay/PlayerActions.cs` (keys, mouse and gamepad as a struct), and the game logic that uses them is in `Gameplay/PlayerControls.cs`. The engine comes from the `CSharpGameEngine.Sdk` package.
