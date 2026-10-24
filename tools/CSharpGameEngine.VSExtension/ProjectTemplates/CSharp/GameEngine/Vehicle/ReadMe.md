# $projectname$

A C# Game Engine game created from the Vehicle template (a port of Unreal Engine's Vehicle template).

- Run: press F5, or `dotnet run` in this folder.
- Drive: W or the up arrow, the right trigger or the left face button accelerates; S or the down arrow, the left trigger or the bottom face button brakes and then reverses; D / A or the right / left arrow, or the left stick sideways, steer. Triggers and the stick are analog: half a trigger is half the throttle.
- Handbrake: Space, or the left shoulder button.

The level is `Scenes/Lvl_VehicleBasic.json` and the car is `Scenes/SportsCar.json`; open them in the Scene Designer or edit the JSON. The level follows Unreal's: a 126 m arena whose ground is the Landscape as a triangle mesh (`Assets/Stage/Lvl_VehicleBasic/Meshes/Landscape_Terrain.glb`, with a hill in the middle and the tunnel on it) and a checkered ring track with curbs (`Track_Road.glb` collides; the curbs in `Track_Curbs.glb` are drawn only). The controls are declared in `Gameplay/PlayerActions.cs`, `Gameplay/VehicleControls.cs` turns them into the car's `VehicleIntent`, and the Vehicles plugin (a Chaos-style rigid body vehicle: wheel suspension, torque curve and five-speed automatic gearbox, Ackermann steering, tyre friction) drives the car from the values in `Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`. The speed and the gear are drawn on the screen by `Gameplay/Speedometer.cs`. Tab switches the camera, L toggles the left headlight, Backspace resets the car. The engine comes from the `CSharpGameEngine.Sdk` package.
