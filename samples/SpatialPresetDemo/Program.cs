// Demonstrates SpatialPresetSelection.props: build/run this project with -p:SpatialPreset=Unity,
// -p:SpatialPreset=Unreal, or -p:SpatialPreset=Blender to see the same RUF direction map to a
// different physical XYZ triple per preset, entirely at compile time (see ProjectSpatial.cs).

using CSharpGameEngine.Spatial.Abstractions;
using SpatialPresetDemo;

RufVector forward = ProjectSpatial.Forward;
XyzVector forwardXyz = ProjectSpatial.ToXyz(forward);
RufVector roundTripped = ProjectSpatial.FromXyz(forwardXyz);

Console.WriteLine($"SpatialPreset          = {ProjectSpatial.PresetName}");
Console.WriteLine($"Forward (semantic RUF)  = {forward}");
Console.WriteLine($"Forward (physical XYZ)  = {forwardXyz}");
Console.WriteLine($"Forward round-tripped   = {roundTripped}");
