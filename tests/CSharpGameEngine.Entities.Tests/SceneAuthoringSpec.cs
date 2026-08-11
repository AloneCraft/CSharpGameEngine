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

        Entity root = world.Create(new Position());
        Entity child = world.Create(new Position(), new Parent { Value = root });

        Assert.Equal(root, world.Archetype2.GetParentRefUnchecked(0).Value);
        Assert.Equal(child, world.Archetype2.GetEntityRefUnchecked(0));
    }

    [Fact]
    public void SceneNodeTag_CanBeUsedAsSoleArchetypeComponent_ForEmptyNodes()
    {
        World world = new();

        Entity entity = world.Create(new SceneNodeTag());

        Assert.Equal(1, world.Archetype3.Count);
        Assert.Equal(entity, world.Archetype3.GetEntityRefUnchecked(0));
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

