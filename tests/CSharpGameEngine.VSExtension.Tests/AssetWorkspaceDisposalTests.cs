using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetWorkspaceDisposalTests {
    [Fact]
    public void Dispose_CancelsAnActiveSearchAndCanBeCalledRepeatedly(){
        var Control=(IDisposable)FormatterServices.GetUninitializedObject(typeof(AssetsToolWindowControl));
        var Cancellation=new CancellationTokenSource();
        var Token=Cancellation.Token;
        var Field=typeof(AssetsToolWindowControl).GetField("SearchCancellation",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Field.SetValue(Control,Cancellation);
        Control.Dispose();
        Assert.True(Token.IsCancellationRequested);
        Control.Dispose();
    }
}
