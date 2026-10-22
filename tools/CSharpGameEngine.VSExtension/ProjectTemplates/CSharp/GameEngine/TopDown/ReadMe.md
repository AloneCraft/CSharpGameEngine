# $projectname$

A C# Game Engine game created from the Top Down template (a port of Unreal Engine's Top Down template).

- Run: press F5, or `dotnet run` in this folder.
- Move: click the ground with the left mouse button; hold it to keep following the cursor.

The level is `Scenes/Lvl_TopDown.json` and the character is `Scenes/TopDownCharacter.json`; open them in the Scene Designer or edit the JSON. The click is declared in `Gameplay/PlayerActions.cs` (a mouse button and the pointer as a struct). `Gameplay/ClickToMove.cs` asks the `Picking` service which point of the ground the pointer is over and hands it to `MoveIntent.MoveTo`; the engine walks the character there in a straight line and stops at it. The camera is fixed at an angle above the character (an `OrbitCamera` whose `ViewAngles` are never turned). The engine comes from the `CSharpGameEngine.Sdk` package.
