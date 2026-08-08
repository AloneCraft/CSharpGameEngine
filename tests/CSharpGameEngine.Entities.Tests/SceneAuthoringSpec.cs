namespace CSharpGameEngine.Entities.Tests;

public class SceneAuthoringSpec
{
    [Fact]
    public void SceneNodeId_Equality_IsValueBased()
    {
        SceneNodeId a = new(42UL);
        SceneNodeId b = new(42UL);
        SceneNodeId c = new(43UL);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void SceneNodeSymbol_Equality_IsValueBased()
    {
        SceneNodeSymbol a = new(new SceneNodeId(1UL), "Root");
        SceneNodeSymbol b = new(new SceneNodeId(1UL), "Root");
        SceneNodeSymbol c = new(new SceneNodeId(1UL), "Player");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Parent_CanBeAttachedToEntity_AndReadBack()
    {
        World world = new();
        EntityArchetype rootArchetype = world.CreateArchetype<Position>();
        EntityArchetype childArchetype = world.CreateArchetype<Position, Parent>();
        Entity root = world.CreateEntity(rootArchetype);
        Entity child = world.CreateEntity(childArchetype);

        world.GetRef<Parent>(child) = new Parent { Value = root };

        Assert.Equal(root, world.GetRef<Parent>(child).Value);
    }

    [Fact]
    public void SceneNodeTag_CanBeUsedAsSoleArchetypeComponent_ForEmptyNodes()
    {
        World world = new();
        EntityArchetype archetype = world.CreateArchetype<SceneNodeTag>();

        Entity entity = world.CreateEntity(archetype);

        Assert.True(world.IsAlive(entity));
        Assert.True(world.Has<SceneNodeTag>(entity));
    }

    [Fact]
    public void SceneBuilder_Create_InvokesDefinitionCallback()
    {
        SceneBuilder builder = new();
        bool invoked = false;

        builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL), "Root"), _ => invoked = true);

        Assert.True(invoked);
    }

    [Fact]
    public void SceneBuilder_Create_NullDefinition_Throws()
    {
        SceneBuilder builder = new();

        Assert.Throws<ArgumentNullException>(
            () => builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL), "Root"), null!));
    }

    [Fact]
    public void SceneNodeBuilder_Symbol_ReturnsConstructorValue()
    {
        SceneNodeSymbol symbol = new(new SceneNodeId(7UL), "Root");
        SceneNodeSymbol? observed = null;

        new SceneBuilder().Create(symbol, node => observed = node.Symbol);

        Assert.Equal(symbol, observed);
    }

    [Fact]
    public void SceneNodeBuilder_Create_InvokesChildDefinitionCallback_WithChildSymbol()
    {
        SceneBuilder builder = new();
        SceneNodeSymbol childSymbol = new(new SceneNodeId(2UL), "Child");
        SceneNodeSymbol? observed = null;

        builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL), "Root"), node =>
        {
            node.Create(childSymbol, child => observed = child.Symbol);
        });

        Assert.Equal(childSymbol, observed);
    }

    [Fact]
    public void SceneNodeBuilder_Create_NullDefinition_Throws()
    {
        SceneBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.Create(
            new SceneNodeSymbol(new SceneNodeId(1UL), "Root"),
            node => node.Create(new SceneNodeSymbol(new SceneNodeId(2UL), "Child"), null!)));
    }

    [Fact]
    public void SceneNodeBuilder_Set_DoesNotThrow()
    {
        SceneBuilder builder = new();

        Exception? exception = Record.Exception(() =>
            builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL), "Root"), node => node.Set(new Position())));

        Assert.Null(exception);
    }
}
