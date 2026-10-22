using System.Numerics;
using CSharpGameEngine.Entities;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// Where one mesh is drawn (ADR-0078/M0107): UE's OwnerNoSee / OnlyOwnerSee and CastShadow of a primitive component. A mesh with
/// <see cref="VisibleToCamera"/> off is not in the camera's picture but still casts its shadow (the first-person character's body); one with
/// <see cref="CastsShadow"/> off is seen but casts none (the first-person arms). Entities without the component are drawn and cast shadows as usual.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,10)]
public struct MeshVisibility() {
    public bool VisibleToCamera=true;
    public bool CastsShadow=true;
}
/// <summary>
/// UE's first person rendering of a primitive (FirstPersonPrimitiveType.FIRST_PERSON, ADR-0078/M0107): the mesh is drawn as if the camera's horizontal
/// field of view were <see cref="FieldOfViewDegrees"/> instead of the view's own <see cref="DefaultFieldOfViewDegrees"/>, and scaled by <see cref="Scale"/>
/// toward the camera so it does not poke into walls. Both are a change of the mesh's camera-space position (<see cref="FirstPersonViewWarp"/>), so the
/// renderer needs no second projection.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,11)]
public struct FirstPersonView() {
    /// <summary>UE's FirstPersonFieldOfView (the horizontal angle): 70 in the FirstPerson template.</summary>
    public float FieldOfViewDegrees=70f;
    /// <summary>The horizontal field of view the camera is drawn with (the camera manager's DefaultFOV): 90 in the FirstPerson template.</summary>
    public float DefaultFieldOfViewDegrees=90f;
    /// <summary>UE's FirstPersonScale: 0.6 in the FirstPerson template.</summary>
    public float Scale=0.6f;
}
/// <summary>The camera-space change that gives a mesh the first-person field of view and scale.</summary>
public static class FirstPersonViewWarp {
    /// <summary>
    /// The factor by which the mesh's screen position is spread: <c>tan(default / 2) / tan(first person / 2)</c>. Viewed through the camera's own projection, a
    /// point at camera-space (x, y, z) must appear where the narrower first-person projection would put it, which is x and y times this, at the same depth.
    /// Returns 1 for a field of view that is not between 0 and 180 degrees.
    /// </summary>
    public static float Spread(in FirstPersonView View){
        if(!(View.FieldOfViewDegrees>0f&&View.FieldOfViewDegrees<180f)||!(View.DefaultFieldOfViewDegrees>0f&&View.DefaultFieldOfViewDegrees<180f)){
            return 1f;
        }
        return MathF.Tan(View.DefaultFieldOfViewDegrees*MathF.PI/360f)/MathF.Tan(View.FieldOfViewDegrees*MathF.PI/360f);
    }
    /// <summary>
    /// The matrix (row vectors) that, multiplied after an object's camera-relative world matrix, makes it a first-person primitive: in the camera's frame
    /// x and y are multiplied by <see cref="Spread"/> and everything by <see cref="FirstPersonView.Scale"/> about the eye. Identity for a scale that is not positive.
    /// </summary>
    public static Matrix4x4 Matrix(in RenderCamera Camera,in FirstPersonView View){
        if(!(View.Scale>0f)||!float.IsFinite(View.Scale)){
            return Matrix4x4.Identity;
        }
        var Spread=FirstPersonViewWarp.Spread(View);
        var Eye=Camera.GetEyePosition();
        var Rotation=Camera.GetView();
        Rotation.M41=0f;
        Rotation.M42=0f;
        Rotation.M43=0f;
        if(!Matrix4x4.Invert(Rotation,out var Inverse)){
            return Matrix4x4.Identity;
        }
        var Scale=Matrix4x4.CreateScale(View.Scale*Spread,View.Scale*Spread,View.Scale);
        return Matrix4x4.CreateTranslation(-Eye)*Rotation*Scale*Inverse*Matrix4x4.CreateTranslation(Eye);
    }
}
