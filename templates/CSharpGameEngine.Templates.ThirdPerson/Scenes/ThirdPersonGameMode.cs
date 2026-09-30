using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
namespace CgeThirdPerson.Scenes;
/// <summary>UEのBP_ThirdPersonGameMode相当。ウィンドウと描画セッションを起動し、レベルは<see cref="Lvl_ThirdPerson"/>が宣言する。</summary>
[StartupScene(Order=0)]
internal partial class ThirdPersonGameMode:IScene {
    public Node Root=>new(){
        Name="GameMode",
        Components=[
            new AppHost {
                Title="Third Person",
                Width=1280,
                Height=720,
            },
            new Level {
                ShaderArtifactPath="Shaders/Stage.cgeshaderartifact",
            },
        ],
    };
}
