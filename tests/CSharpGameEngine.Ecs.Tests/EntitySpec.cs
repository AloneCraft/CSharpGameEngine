namespace CSharpGameEngine.Ecs.Tests;

public sealed class EntitySpec
{
    [Fact]
    public void Equals_ComparesIdAndGeneration()
    {
        Entity first = new(1, 0);
        Entity second = new(1, 0);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DiffersByGenerationAlone()
    {
        Entity first = new(1, 0);
        Entity second = new(1, 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_DiffersByIdAlone()
    {
        Entity first = new(1, 0);
        Entity second = new(2, 0);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EqualityOperators_MatchEquals()
    {
        Entity first = new(3, 2);
        Entity second = new(3, 2);
        Entity third = new(3, 3);

        Assert.True(first == second);
        Assert.False(first == third);
        Assert.True(first != third);
    }
}
