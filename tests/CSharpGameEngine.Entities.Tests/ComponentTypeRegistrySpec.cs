namespace CSharpGameEngine.Entities.Tests;

public class ComponentTypeRegistrySpec
{
    [Fact]
    public void SameType_AlwaysReturnsSameId()
    {
        int first = ComponentType<Position>.Id;
        int second = ComponentType<Position>.Id;

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentTypes_ReturnDifferentIds()
    {
        Assert.NotEqual(ComponentType<Position>.Id, ComponentType<Velocity>.Id);
    }

    [Fact]
    public void CreateColumn_ReturnsColumnOfRegisteredType()
    {
        int id = ComponentType<Health>.Id;

        IComponentColumn column = ComponentTypeRegistry.CreateColumn(id);

        Assert.IsType<ComponentColumn<Health>>(column);
    }

    [Fact]
    public void GetClrType_ReturnsOriginalType()
    {
        int id = ComponentType<Health>.Id;

        Assert.Equal(typeof(Health), ComponentTypeRegistry.GetClrType(id));
    }
}
