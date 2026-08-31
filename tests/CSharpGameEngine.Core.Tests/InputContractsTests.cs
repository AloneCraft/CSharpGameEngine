using System.Reflection;
namespace CSharpGameEngine.Core.Tests;
public sealed class InputContractsTests {
    [Fact]
    public void FromName_IsStableAndNamespaceScoped(){
        var NamespaceId=StableId.Parse("88a446a1-9974-4efd-b37e-cf44e33718a4");
        var First=StableId.FromName(NamespaceId,"keyboard/main");
        var Second=StableId.FromName(NamespaceId,"keyboard/main");
        var Other=StableId.FromName(NamespaceId,"mouse/main");
        Assert.Equal(First,Second);
        Assert.NotEqual(First,Other);
        Assert.True(First.IsValid);
    }
    [Fact]
    public void PortableContracts_ContainNoNativeTypes(){
        Type[]Types=[typeof(MonotonicTimestamp),typeof(WindowId),typeof(InputDeviceId),typeof(InputContextId),typeof(InputActionId),typeof(InputControl),typeof(InputValue),typeof(InputEvent),typeof(InputActionValue)];
        foreach(var TypeValue in Types){
            Assert.DoesNotContain(TypeValue.GetProperties(BindingFlags.Public|BindingFlags.Instance),static Property=>Property.PropertyType==typeof(nint)||Property.PropertyType==typeof(nuint));
            Assert.DoesNotContain(TypeValue.GetFields(BindingFlags.Public|BindingFlags.Instance),static Field=>Field.FieldType==typeof(nint)||Field.FieldType==typeof(nuint));
        }
    }
    [Fact]
    public void Timestamp_OrdersByTick(){
        Assert.True(new MonotonicTimestamp(10)<new MonotonicTimestamp(11));
        Assert.Equal(0,new MonotonicTimestamp(10).CompareTo(new MonotonicTimestamp(10)));
    }
}
