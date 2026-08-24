using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Scenes;

/// <summary>
/// アプリケーションと描画セッションの起動設定。ステージの構成は
/// <see cref="FirstPersonStageScene"/>を正本とし、このSceneには埋め込まない。
/// </summary>
[StartupScene(Order=0)]
internal partial class MainScene:IScene {
    public Node Root=>new(){
        Name="Application",
        Components=[
            new AppHost {
                Title="C# Game Engine - FirstPerson Stage",
                Width=1280,
                Height=720,
            },
            new Level {
                ShaderPath="Shaders/Stage.hlsl",
            },
        ],
    };
}
