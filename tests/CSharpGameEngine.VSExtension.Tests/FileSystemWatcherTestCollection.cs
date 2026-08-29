using Xunit;
namespace CSharpGameEngine.VSExtension.Tests;
[CollectionDefinition(Name,DisableParallelization=true)]
public sealed class FileSystemWatcherTestCollection {
    public const string Name="FileSystemWatcher";
}
