extern alias PreviewHost;
using System.Numerics;
using CSharpGameEngine.Rendering;
using HostPlacement=PreviewHost::CSharpGameEngine.Runtime.Windows.StageModelAssets.Placement;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
namespace CSharpGameEngine.Windows.Tests.Runtime;
public sealed class SkeletalPreviewBoundsTests {
    [Fact]
    public void RestBoundsIncludeWeightedSkinningAndRigidNodeTransforms(){
        var Influences=new StageSkinCreateInfo {
            JointCount=2,
            JointIndices=[0,1,0,0,0,0,0,0,0,1,0,0,0,0,0,0],
            Weights=[0.25f,0.75f,0f,0f,0f,0f,0f,0f,0.25f,0.75f,0f,0f,0f,0f,0f,0f],
        };
        var Skinned=new StageMeshCreateInfo {
            Positions=[Vector3.Zero,new Vector3(1f,1f,0f)],Normals=[],Tangents=[],Uvs=[],Indices=[0,1,0],Skin=Influences,
        };
        var Rigid=new StageMeshCreateInfo {
            Positions=[new Vector3(2f,2f,0f)],Normals=[],Tangents=[],Uvs=[],Indices=[0,0,0],
        };
        HostPlacement[]Placements=[new(0,0,Matrix4x4.Identity),new(1,0,Matrix4x4.CreateTranslation(-5f,3f,0f))];
        Matrix4x4[]Palette=[Matrix4x4.Identity,Matrix4x4.CreateTranslation(10f,0f,0f)];
        var Bounds=RenderPreviewModelBounds.Calculate([Skinned,Rigid],Placements,Palette,true);
        Assert.InRange(Bounds.Min[0],-3.01f,-3f);
        Assert.InRange(Bounds.Min[1],-0.01f,0f);
        Assert.InRange(Bounds.Max[0],8.5f,8.51f);
        Assert.InRange(Bounds.Max[1],5f,5.01f);
        Assert.Equal(3,Bounds.VertexCount);
        Assert.Equal(2,Bounds.TriangleCount);
    }
}
