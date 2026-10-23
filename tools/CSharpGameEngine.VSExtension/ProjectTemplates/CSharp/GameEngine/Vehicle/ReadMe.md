# $projectname$

A C# Game Engine game created from the Vehicle template (a port of Unreal Engine's Vehicle template).

- Run: press F5, or `dotnet run` in this folder.
- Drive: W or the up arrow, the right trigger or the left face button accelerates; S or the down arrow, the left trigger or the bottom face button brakes and then reverses; D / A or the right / left arrow, or the left stick sideways, steer. Triggers and the stick are analog: half a trigger is half the throttle.
- Handbrake: Space, or the left shoulder button.

The level is `Scenes/Lvl_VehicleBasic.json` (a flat arena standing in for the Landscape) and the car is `Scenes/SportsCar.json`; open them in the Scene Designer or edit the JSON. The controls are declared in `Gameplay/PlayerActions.cs`, `Gameplay/VehicleControls.cs` turns them into the car's `VehicleIntent`, and the engine's simplified vehicle model drives the car from the `Vehicle` values in the scene. The speed and the gear are shown in the window's title bar by `Gameplay/Speedometer.cs`. The engine comes from the `CSharpGameEngine.Sdk` package.
