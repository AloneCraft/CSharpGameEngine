namespace CSharpGameEngine.Core.Tests;

public sealed class StableIdSpec
{
    [Fact]
    public void New_CreatesValidId()
    {
        StableId id = StableId.New();

        Assert.True(id.IsValid);
    }

    [Fact]
    public void New_CreatesUsuallyDifferentIds()
    {
        StableId first = StableId.New();
        StableId second = StableId.New();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToStringAndParse_RoundTrip()
    {
        StableId original = StableId.New();
        string text = original.ToString();

        StableId parsed = StableId.Parse(text);

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void TryParse_HandlesValidAndInvalidValues()
    {
        StableId original = StableId.New();

        bool success = StableId.TryParse(original.ToString(), out StableId valid);
        bool failed = StableId.TryParse("not-a-stable-id", out StableId invalid);

        Assert.True(success);
        Assert.Equal(original, valid);
        Assert.False(failed);
        Assert.Equal(default, invalid);
    }

    [Fact]
    public void Default_IsInvalid()
    {
        StableId value = default;

        Assert.False(value.IsValid);
    }

    [Fact]
    public void EqualityAndHashCode_AreConsistent()
    {
        StableId original = StableId.New();
        StableId copy = StableId.Parse(original.ToString());

        Assert.True(original == copy);
        Assert.Equal(original.GetHashCode(), copy.GetHashCode());
    }
}
