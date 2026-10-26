using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Rendering.Server;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// グローバル距離場に対するADR-0078/M0110のWARPオラクル。クリップマップにはメッシュ距離場から作った最寄り面までの符号付き距離が保持される。
/// カメラと移動オブジェクトに追従し、変化したブリックだけを再計算する（結果は一度に作成した距離場と一致する）。M0111と同様にトレースも可能。
/// ランタイムと同様にカメラ相対でシーンを描画する：render = world - camera。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class GlobalDistanceFieldGpuIntegrationTests(ITestOutputHelper Output) {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const float Voxel=0.5f;
    private const int Resolution=32;
    private const int Levels=3;
    private static readonly Vector3 BoxHalf=new(1f,1f,1f);
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Field_HoldsTheSignedDistanceOfTheNearestSurfaceOnEveryLevel(){
        using var Scene=FieldScene.Create();
        var Camera=new Vector3(0.3f,1.6f,-6.2f);
        Scene.Render(Camera,[Matrix4x4.Identity,Matrix4x4.CreateScale(2f,1f,1f)*Matrix4x4.CreateRotationY(0.5f)*Matrix4x4.CreateTranslation(-6f,0f,3f)]);
        var Worst=0f;
        for(var Level=0;Level<Levels;Level++){
            var Field=Scene.Read(Level,Camera);
            foreach(var (Render,Value) in Field.Voxels()){
                // ボクセルは描画空間にあり、ボックスはワールドに配置する（render = world - camera）。
                var Position=Render+Camera;
                // 最初のボックスは軸平行なので正確な距離を得る。スケール／回転したものは上限評価のみで、真の距離を超えない。
                var Exact=BoxDistance(Position,BoxHalf);
                var Truncation=Field.VoxelSize*StageGlobalDistanceField.TruncationVoxels;
                var Expected=Math.Clamp(Exact,-Truncation,Truncation);
                var Second=Vector3.Transform(Position,Invert(Matrix4x4.CreateScale(2f,1f,1f)*Matrix4x4.CreateRotationY(0.5f)*Matrix4x4.CreateTranslation(-6f,0f,3f)));
                var SecondBound=BoxDistance(Second,BoxHalf);
                if(SecondBound<Truncation+0.1f){
                    // スケール付きボックス近傍の距離場は、保守的な距離（ローカル距離×最小スケール）で上限評価される。
                    Assert.True(Value<=MathF.Min(Expected,SecondBound)+0.06f,$"level {Level} at {Position}: {Value}");
                    if(SecondBound<-0.06f){ Assert.True(Value<0f); }
                    continue;
                }
                Worst=MathF.Max(Worst,MathF.Abs(Value-Expected));
                Assert.True(MathF.Abs(Value-Expected)<=0.06f,$"level {Level} at {Position}: {Value}, expected {Expected}");
                if(Exact<-0.06f){ Assert.True(Value<0f); }
                if(Exact>0.06f){ Assert.True(Value>0f); }
            }
        }
        Output.WriteLine($"largest error {Worst:F4} m");
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Field_FollowsTheCameraUpdatingOnlyNewBricksAndEqualsAFieldMadeAtOnce(){
        Matrix4x4[]Objects=[Matrix4x4.CreateTranslation(2f,0f,1f),Matrix4x4.CreateTranslation(-3f,0.5f,4f)];
        using var Moving=FieldScene.Create();
        Moving.Render(new Vector3(0f,1.6f,-4f),Objects);
        var Full=Moving.Renderer.GlobalDistanceFieldForTesting.LastUpdatedBricks.Take(Levels).ToArray();
        // 横へ5.3 m、上へ0.7 m移動させる。レベル0ではブリック全体が新しく覆われ、粗いレベルではブリックの一部が新たに覆われる。
        var Final=new Vector3(5.3f,2.3f,-4f);
        Moving.Render(Final,Objects);
        var Incremental=Moving.Renderer.GlobalDistanceFieldForTesting.LastUpdatedBricks.Take(Levels).ToArray();
        Output.WriteLine($"bricks: first frame {string.Join(",",Full)}, after the move {string.Join(",",Incremental)}");
        var BricksPerLevel=(Resolution/StageGlobalDistanceField.BrickSize)*(Resolution/StageGlobalDistanceField.BrickSize)*(Resolution/StageGlobalDistanceField.BrickSize);
        Assert.All(Full,Count=>Assert.Equal(BricksPerLevel,Count));
        Assert.True(Incremental[0]>0&&Incremental[0]<BricksPerLevel);
        using var Fresh=FieldScene.Create();
        Fresh.Render(Final,Objects);
        for(var Level=0;Level<Levels;Level++){
            AssertSameField(Fresh.Read(Level,Final).Values,Moving.Read(Level,Final).Values);
        }
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Field_FollowsAMovingObjectAndEqualsAFieldMadeAtOnce(){
        var Camera=new Vector3(0f,1.6f,-4f);
        using var Moving=FieldScene.Create();
        Moving.Render(Camera,[Matrix4x4.Identity,Matrix4x4.CreateTranslation(-3f,0f,4f)]);
        var Before=Moving.Read(0,Camera);
        Matrix4x4[]Moved=[Matrix4x4.CreateTranslation(3f,0f,0f),Matrix4x4.CreateTranslation(-3f,0f,4f)];
        Moving.Render(Camera,Moved);
        var After=Moving.Read(0,Camera);
        Assert.True(Before.At(-Camera)<0f);
        Assert.True(After.At(-Camera)>1f);
        Assert.True(After.At(new Vector3(3f,0f,0f)-Camera)<0f);
        var Updated=Moving.Renderer.GlobalDistanceFieldForTesting.LastUpdatedBricks[0];
        Output.WriteLine($"bricks after the move: {Updated}");
        Assert.InRange(Updated,1,63);
        using var Fresh=FieldScene.Create();
        Fresh.Render(Camera,Moved);
        for(var Level=0;Level<Levels;Level++){
            Assert.Equal(Fresh.Read(Level,Camera).Values,Moving.Read(Level,Camera).Values);
        }
        // オブジェクトを削除すると、以前あった位置もクリアする。
        Moving.Renderer.RemoveObject(0);
        Moving.Render(Camera,Moved);
        Assert.True(Moving.Read(0,Camera).At(new Vector3(3f,0f,0f)-Camera)>1f);
        Moving.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Trace_HitsTheSurfaceAndMissesEmptySpace(){
        using var Scene=FieldScene.Create();
        var Camera=new Vector3(0f,1.6f,-6f);
        Scene.Render(Camera,[Matrix4x4.Identity]);
        // 描画空間ではボックスは-camera位置にある。
        var Box=-Camera;
        (Vector3 Origin,Vector3 Direction,float Length)[]Rays=[
            (Vector3.Zero,Vector3.Normalize(Box-Vector3.Zero),20f),
            (Vector3.Zero,Vector3.Normalize(Box+new Vector3(0.5f,0.5f,0f)),20f),
            (Vector3.Zero,Vector3.UnitY,20f),
            (new Vector3(-5f,-1.6f,6f),Vector3.UnitX,20f),
        ];
        var Hits=Scene.Renderer.TraceGlobalDistanceFieldForTesting(Rays);
        for(var Index=0;Index<2;Index++){
            var Expected=RayBox(Rays[Index].Origin-Box,Rays[Index].Direction,BoxHalf);
            Assert.Equal(1f,Hits[Index].Z);
            Assert.InRange(Hits[Index].X,Expected-0.3f,Expected+0.3f);
        }
        Assert.Equal(0f,Hits[2].Z);
        Assert.Equal(20f,Hits[2].X);
        var Side=RayBox(Rays[3].Origin-Box,Rays[3].Direction,BoxHalf);
        Assert.Equal(1f,Hits[3].Z);
        Assert.InRange(Hits[3].X,Side-0.3f,Side+0.3f);
        Output.WriteLine($"hits {string.Join(" ",Hits.Select(Hit=>$"{Hit.X:F3}/{Hit.Y}"))}");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Field_ChangesNoPixelOfTheFrame(){
        var Camera=new Vector3(0.2f,1.6f,-5f);
        using var Without=FieldScene.Create();
        Without.Render(Camera,[Matrix4x4.Identity],Field:false,Capture:true);
        using var With=FieldScene.Create();
        With.Render(Camera,[Matrix4x4.Identity],Field:true,Capture:true);
        Assert.Equal(SHA256.HashData(Without.Renderer.LastCapturedFrame!.Pixels),SHA256.HashData(With.Renderer.LastCapturedFrame!.Pixels));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Field_IgnoresMeshesWithoutAFieldAndWorksFarFromTheWorldOrigin(){
        using var Near=FieldScene.Create();
        Near.Render(new Vector3(0f,1.6f,-4f),[Matrix4x4.Identity]);
        // 同じシーンを10^12ボクセル離れた位置（500 000 000 km）へ移しても、ボクセル格子は整数なのでカメラ周辺の距離場は同じ。
        using var Far=FieldScene.Create();
        Far.Render(new Vector3(0f,1.6f,-4f),[Matrix4x4.Identity],OriginVoxel:new LongVector3(1_000_000_000_000,0,-1_000_000_000_000));
        var A=Near.Read(0,new Vector3(0f,1.6f,-4f));
        var B=Far.Read(0,new Vector3(0f,1.6f,-4f),new LongVector3(1_000_000_000_000,0,-1_000_000_000_000));
        foreach(var Point in new[]{ Vector3.Zero,new Vector3(1.5f,0f,0f),new Vector3(-0.25f,0.75f,0.25f),new Vector3(2.5f,1f,-1.5f) }){
            Assert.Equal(A.At(Point),B.At(Point));
        }
        // 距離場のないメッシュは除外する。どの位置もトランケーション距離となる。
        using var Empty=FieldScene.Create(WithField:false);
        Empty.Render(new Vector3(0f,1.6f,-4f),[Matrix4x4.Identity]);
        Assert.All(Empty.Read(0,new Vector3(0f,1.6f,-4f)).Values,Value=>Assert.Equal(Voxel*StageGlobalDistanceField.TruncationVoxels,Value));
        Assert.Equal(0,Empty.Renderer.GlobalDistanceFieldForTesting.LastFieldObjects);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RenderServer_KeepsEachMeshsDistanceFieldForTheRendererAndAddedMeshes(){
        // Render Serverはデバイス復旧用に渡されたメッシュをコピーする。コピー時に距離場を落としていたため、ゲームのオブジェクトが
        // グローバル距離場へ1つも追加されなかった。
        var Box=ProceduralMeshFactory.CreateBox(new Vector3(2f,2f,2f));
        var Mesh=new StageMeshCreateInfo { Positions=Box.Positions,Normals=Box.Normals,Tangents=Box.Normals.Select(StageMeshGeometry.ArbitraryTangentFor).ToArray(),
            Uvs=new Vector2[Box.Positions.Length],Indices=Box.Indices,DistanceField=MeshDistanceFieldBuilder.Build(Box.Positions,Box.Indices) };
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Global Distance Field Server",Width=64,Height=64,Visible=false });
        using var Server=CSharpGameEngine.Graphics.D3D12.D3D12RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            ShaderArtifactPath,[Mesh],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Server.AddObject(Server.AddMesh(Mesh),0);
        Server.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DistanceField=new GlobalDistanceFieldSettings(Voxel,1,16,default,Vector3.Zero) });
        Server.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,1f,-5f)));
        Server.RenderFrame();
        Assert.Equal(2,Server.DistanceFieldMeshCount);
        Assert.Equal(2,Server.DistanceFieldObjectCount);
        Assert.Equal(0,Server.ErrorOrWorseMessageCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Settings_RejectUnusableValues(){
        Assert.False((GlobalDistanceFieldSettings.Default with { VoxelSizeMeters=float.NaN }).IsValid);
        Assert.False((GlobalDistanceFieldSettings.Default with { Resolution=60 }).IsValid);
        Assert.False((GlobalDistanceFieldSettings.Default with { ClipmapCount=5 }).IsValid);
        Assert.False((GlobalDistanceFieldSettings.Default with { OriginRemainder=new Vector3(float.NaN) }).IsValid);
        Assert.False((GlobalDistanceFieldSettings.Default with { OriginVoxel=new LongVector3(long.MaxValue,0,0) }).IsValid);
        Assert.True(GlobalDistanceFieldSettings.Default.IsValid);
        Assert.Equal(-1,StageGlobalDistanceField.FloorDivide(-1,8));
        Assert.Equal(-1,StageGlobalDistanceField.FloorDivide(-8,8));
        Assert.Equal(7,StageGlobalDistanceField.PositiveModulo(-1,8));
        using var Scene=FieldScene.Create();
        // 行列が有限値でないオブジェクトはクリップ全体を汚染しないよう除外する。距離場に入るのは1キロメートル先の2つ目だけ。
        Scene.Render(new Vector3(0f,1.6f,-4f),[new Matrix4x4(float.NaN,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1)]);
        Assert.Equal(1,Scene.Renderer.GlobalDistanceFieldForTesting.LastFieldObjects);
        Scene.AssertNoDebugLayerErrors();
    }
    /// <summary>
    /// 複数フレームに分けて更新した距離場は、一度に作成したものとfloat丸め誤差の範囲で一致する。
    /// 以前のフレームでは別のカメラ原点からボクセル中心とオブジェクトの描画空間行列を求めているため、ボクセルの最下位ビットに差が生じる場合がある。
    /// </summary>
    private static void AssertSameField(float[]Expected,float[]Actual){
        Assert.Equal(Expected.Length,Actual.Length);
        var Worst=0f;
        for(var Index=0;Index<Expected.Length;Index++){
            Worst=MathF.Max(Worst,MathF.Abs(Expected[Index]-Actual[Index]));
        }
        Assert.True(Worst<=1e-3f,$"largest difference {Worst}");
    }
    private static float BoxDistance(Vector3 Point,Vector3 HalfSize){
        var Q=Vector3.Abs(Point)-HalfSize;
        return Vector3.Max(Q,Vector3.Zero).Length()+MathF.Min(MathF.Max(Q.X,MathF.Max(Q.Y,Q.Z)),0f);
    }
    private static float RayBox(Vector3 Origin,Vector3 Direction,Vector3 HalfSize){
        var Near=float.MinValue;
        var Far=float.MaxValue;
        for(var Axis=0;Axis<3;Axis++){
            var O=Axis==0?Origin.X:Axis==1?Origin.Y:Origin.Z;
            var D=Axis==0?Direction.X:Axis==1?Direction.Y:Direction.Z;
            var H=Axis==0?HalfSize.X:Axis==1?HalfSize.Y:HalfSize.Z;
            if(MathF.Abs(D)<1e-8f){ continue; }
            var T1=(-H-O)/D;
            var T2=(H-O)/D;
            Near=MathF.Max(Near,MathF.Min(T1,T2));
            Far=MathF.Min(Far,MathF.Max(T1,T2));
        }
        return Near;
    }
    private static Matrix4x4 Invert(Matrix4x4 Matrix){
        Matrix4x4.Invert(Matrix,out var Inverse);
        return Inverse;
    }
    /// <summary>1レベル分の読み戻しデータと、そのテクセルを描画空間のボクセル中心へ対応付ける情報。</summary>
    private sealed record LevelField(float[]Values,int Size,LongVector3 ClipMinimum,LongVector3 Origin,Vector3 Remainder,float VoxelSize){
        internal float AtVoxel(LongVector3 Global)=>this.Values[(int)((StageGlobalDistanceField.PositiveModulo(Global.Z,this.Size)*this.Size+StageGlobalDistanceField.PositiveModulo(Global.Y,this.Size))*this.Size+StageGlobalDistanceField.PositiveModulo(Global.X,this.Size))];
        internal Vector3 Centre(LongVector3 Global)=>new Vector3(Global.X-this.Origin.X+0.5f,Global.Y-this.Origin.Y+0.5f,Global.Z-this.Origin.Z+0.5f)*this.VoxelSize-this.Remainder;
        /// <summary>描画空間の点を含むボクセルの値。</summary>
        internal float At(Vector3 Render){
            var Local=(Render+this.Remainder)/this.VoxelSize;
            return this.AtVoxel(new LongVector3(this.Origin.X+(long)MathF.Floor(Local.X),this.Origin.Y+(long)MathF.Floor(Local.Y),this.Origin.Z+(long)MathF.Floor(Local.Z)));
        }
        internal IEnumerable<(Vector3 Position,float Value)>Voxels(){
            for(var Z=0;Z<this.Size;Z++){
                for(var Y=0;Y<this.Size;Y++){
                    for(var X=0;X<this.Size;X++){
                        var Global=new LongVector3(this.ClipMinimum.X+X,this.ClipMinimum.Y+Y,this.ClipMinimum.Z+Z);
                        yield return (this.Centre(Global),this.AtVoxel(Global));
                    }
                }
            }
        }
    }
    private sealed class FieldScene:IDisposable {
        private readonly Win32Window Window;
        internal StageRenderer Renderer { get; }
        private FieldScene(Win32Window Window,StageRenderer Renderer){
            this.Window=Window;
            this.Renderer=Renderer;
        }
        internal static FieldScene Create(bool WithField=true){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Global Distance Field",Width=160,Height=90,Visible=false });
            var Box=ProceduralMeshFactory.CreateBox(new Vector3(2f,2f,2f));
            var Tangents=Box.Normals.Select(StageMeshGeometry.ArbitraryTangentFor).ToArray();
            var Mesh=new StageMeshCreateInfo { Positions=Box.Positions,Normals=Box.Normals,Tangents=Tangents,Uvs=new Vector2[Box.Positions.Length],Indices=Box.Indices,
                DistanceField=WithField?MeshDistanceFieldBuilder.Build(Box.Positions,Box.Indices):null };
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Mesh],[new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.6f,0.6f,0.6f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
                [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
            return new FieldScene(Window,Renderer);
        }
        /// <summary>ワールド位置へカメラを置いて描画する。オブジェクトはworld - cameraに配置し、距離場の原点はカメラのボクセルとする。</summary>
        internal void Render(Vector3 CameraWorld,Matrix4x4[]Objects,bool Field=true,bool Capture=false,LongVector3 OriginVoxel=default){
            var (Cell,Remainder)=Locate(CameraWorld,OriginVoxel);
            var View=Camera.CreateFirstPerson(Vector3.Zero);
            View.Pitch=-0.2f;
            this.Renderer.SetCamera(View);
            this.Renderer.SetAmbientLight(new Vector3(0.5f));
            this.Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with {
                DistanceField=Field?new GlobalDistanceFieldSettings(Voxel,Levels,Resolution,Cell,Remainder):null });
            for(var Index=0;Index<2;Index++){
                // テストで2つ目のオブジェクトだけを置く場合も、1キロメートル先に残す。すべてのクリップマップの範囲外となる。
                var World=Index<Objects.Length?Objects[Index]:Matrix4x4.CreateTranslation(1000f,0f,0f);
                this.Renderer.SetObjectTransform(Index,World*Matrix4x4.CreateTranslation(-CameraWorld));
            }
            this.Renderer.RenderFrame(CaptureForVerification:Capture);
        }
        internal LevelField Read(int Level,Vector3 CameraWorld,LongVector3 OriginVoxel=default){
            var (Cell,Remainder)=Locate(CameraWorld,OriginVoxel);
            var (Values,Size,ClipMinimum)=this.Renderer.ReadGlobalDistanceFieldForTesting(Level);
            var Scale=1L<<Level;
            var Origin=new LongVector3(StageGlobalDistanceField.FloorDivide(Cell.X,Scale),StageGlobalDistanceField.FloorDivide(Cell.Y,Scale),StageGlobalDistanceField.FloorDivide(Cell.Z,Scale));
            var LevelRemainder=Remainder+new Vector3(Cell.X-Origin.X*Scale,Cell.Y-Origin.Y*Scale,Cell.Z-Origin.Z*Scale)*Voxel;
            return new LevelField(Values,Size,ClipMinimum,Origin,LevelRemainder,Voxel*Scale);
        }
        /// <summary>ワールド格子上でのカメラのボクセル（<paramref name="OriginVoxel"/>からのオフセット）と、そこから角を越えた距離。</summary>
        private static (LongVector3 Voxel,Vector3 Remainder) Locate(Vector3 CameraWorld,LongVector3 OriginVoxel){
            var Cell=new Vector3(MathF.Floor(CameraWorld.X/Voxel),MathF.Floor(CameraWorld.Y/Voxel),MathF.Floor(CameraWorld.Z/Voxel));
            return (new LongVector3(OriginVoxel.X+(long)Cell.X,OriginVoxel.Y+(long)Cell.Y,OriginVoxel.Z+(long)Cell.Z),CameraWorld-Cell*Voxel);
        }
        internal void AssertNoDebugLayerErrors()=>Assert.Equal(0,this.Renderer.ErrorOrWorseMessageCount);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
