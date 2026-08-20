namespace CSharpGameEngine.Core.Tests;

public sealed class StableIdSpec {
    [Fact]
    public void New_CreatesValidId(){
        var Id=StableId.New();

        Assert.True(Id.IsValid);
    }

    [Fact]
    public void New_CreatesUsuallyDifferentIds(){
        var First=StableId.New();
        var Second=StableId.New();

        Assert.NotEqual(First,Second);
    }

    [Fact]
    public void ToStringAndParse_RoundTrip(){
        var Original=StableId.New();
        var Text=Original.ToString();

        var Parsed=StableId.Parse(Text);

        Assert.Equal(Original,Parsed);
    }

    [Fact]
    public void TryParse_HandlesValidAndInvalidValues(){
        var Original=StableId.New();

        var Success=StableId.TryParse(Original.ToString(),out var Valid);
        var Failed=StableId.TryParse("not-a-stable-id",out var Invalid);

        Assert.True(Success);
        Assert.Equal(Original,Valid);
        Assert.False(Failed);
        Assert.Equal(default,Invalid);
    }

    [Fact]
    public void Default_IsInvalid(){
        StableId Value=default;

        Assert.False(Value.IsValid);
    }

    [Fact]
    public void EqualityAndHashCode_AreConsistent(){
        var Original=StableId.New();
        var Copy=StableId.Parse(Original.ToString());

        Assert.True(Original==Copy);
        Assert.Equal(Original.GetHashCode(),Copy.GetHashCode());
    }
}
