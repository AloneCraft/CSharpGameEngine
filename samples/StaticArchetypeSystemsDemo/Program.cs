var world = new World();
var state = new State(world) { Enabled = true };

Archetype0.Create(ref world, new Component0());
Archetype1.Create(ref world, new Component1());
Archetype2.Create(ref world, new Component2());
Archetype01.Create(ref world, new Component0(), new Component1());

var system0 = new System0();
var system1 = new System1();

for (var frame = 0; frame < 5; frame++)
{
    GeneratedGameLoop.RunFrame(ref state, ref system0, ref system1);
    Console.WriteLine($"frame {frame}: 0={world.ListArchetype0.Count} 1={world.ListArchetype1.Count} 2={world.ListArchetype2.Count} 01={world.ListArchetype01.Count}");
}
