using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

[Collection(MockedVsCollection.Name)]
public sealed class RestoredToolWindowSessionTests {
    public RestoredToolWindowSessionTests(MockedVsFixture Fixture){ }

    [Fact]
    public void ProviderConfiguredAfterPaneRestore_IsReevaluatedForEveryScenePane(){
        RunOnUiThread(()=>{
            SceneDesignerToolWindowPane.ClearSessionProvider();
            InspectorToolWindowPane.ClearSessionProvider();
            ComponentPaletteToolWindowPane.ClearSessionProvider();

            _=new SceneDesignerToolWindowPane();
            _=new InspectorToolWindowPane();
            _=new ComponentPaletteToolWindowPane();

            int SceneDesignerCalls=0;
            int InspectorCalls=0;
            int ComponentPaletteCalls=0;
            SceneDesignerToolWindowPane.ConfigureSessionProvider(()=>{
                SceneDesignerCalls++;
                return null;
            });
            InspectorToolWindowPane.ConfigureSessionProvider(()=>{
                InspectorCalls++;
                return null;
            });
            ComponentPaletteToolWindowPane.ConfigureSessionProvider(()=>{
                ComponentPaletteCalls++;
                return null;
            });

            Assert.Equal(1,SceneDesignerCalls);
            Assert.Equal(1,InspectorCalls);
            Assert.Equal(1,ComponentPaletteCalls);

            SceneDesignerToolWindowPane.ClearSessionProvider();
            InspectorToolWindowPane.ClearSessionProvider();
            ComponentPaletteToolWindowPane.ClearSessionProvider();
        });
    }

    [Fact]
    public async Task ClearingProviders_IsSafeDuringOffUiThreadPackageShutdown(){
        await Task.Run(()=>{
            SceneDesignerToolWindowPane.ClearSessionProvider();
            InspectorToolWindowPane.ClearSessionProvider();
            ComponentPaletteToolWindowPane.ClearSessionProvider();
        });
    }

    private static void RunOnUiThread(Action Action)=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Action();
        });
}
