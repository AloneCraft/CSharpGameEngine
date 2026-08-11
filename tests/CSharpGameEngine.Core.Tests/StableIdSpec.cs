namespace CSharpGameEngine.Core.Tests;

public sealed class StableIdSpec {
    [Fact]
    public void New_CreatesValidId(){
        StableId Id=StableId.New();

        Assert.True(Id.IsValid);
    }

    [Fact]
    public void New_CreatesUsuallyDifferentIds(){
        StableId First=StableId.New();
        StableId Second=StableId.New();

        Assert.NotEqual(First,Second);
    }

    [Fact]
    public void ToStringAndParse_RoundTrip(){
        StableId Original=StableId.New();
        var Text=Original.ToString();

        StableId Parsed=StableId.Parse(Text);

        Assert.Equal(Original,Parsed);
    }

    [Fact]
    public void TryParse_HandlesValidAndInvalidValues(){
        StableId Original=StableId.New();

        var Success=StableId.TryParse(Original.ToString(),out StableId Valid);
        var Failed=StableId.TryParse("not-a-stable-id",out StableId Invalid);

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
        StableId Original=StableId.New();
        StableId Copy=StableId.Parse(Original.ToString());

        Assert.True(Original==Copy);
        Assert.Equal(Original.GetHashCode(),Copy.GetHashCode());
    }
}
