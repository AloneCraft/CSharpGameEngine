using CSharpGameEngine.Entities;
using System.Numerics;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Scene DSLとRuntime ECSの両方で使うローカルTransform。
/// Node.Childrenで宣言した子は自動付与のParentで親を参照し、親子の合成は<see cref="TransformMath"/>で行う。
/// </summary>
[SceneComponentIcon("Assets/Editor/ComponentIcons/Transform.svg")]
public struct Transform {
    public float PositionX;
    public float PositionY;
    public float PositionZ;

    public float RotationX;
    public float RotationY;
    public float RotationZ;
    public float RotationW;

    public float ScaleX;
    public float ScaleY;
    public float ScaleZ;

    /// <summary>Scene DSLの<c>new Transform()</c>がUnityと同じidentity transformになるための既定値。</summary>
    public Transform(){
        this.RotationW=1f;
        this.ScaleX=1f;
        this.ScaleY=1f;
        this.ScaleZ=1f;
    }
}

/// <summary>
/// Transformの行列合成・分解ユーティリティ。
/// EditorとRuntimeが同じ式を共有し、Reparent時のworld pose維持にも同じ計算を使う。
/// </summary>
public static class TransformMath {
    public static Matrix4x4 ToMatrix(in Transform Transform)=>
        Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
        *Matrix4x4.CreateFromQuaternion(new Quaternion(Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
        *Matrix4x4.CreateTranslation(Transform.PositionX,Transform.PositionY,Transform.PositionZ);

    /// <summary>System.Numericsのrow-vector規約に合わせ、Local * ParentWorldの順で合成する。</summary>
    public static Matrix4x4 ToWorld(in Transform Local,in Matrix4x4 ParentWorld)=>ToMatrix(Local)*ParentWorld;

    /// <summary>
    /// world poseを維持したまま親を差し替えるための新しいlocalを、<c>World * Inverse(NewParentWorld)</c>の分解でTRSへ戻す。
    /// せん断等でTRSに分解できない場合はfalseを返し、呼び出し側はSourceを書き換えないこと。
    /// </summary>
    public static bool TryCalculateLocal(in Matrix4x4 World,in Matrix4x4 NewParentWorld,out Transform Local){
        if(!Matrix4x4.Invert(NewParentWorld,out Matrix4x4 InverseParent)
            ||!Matrix4x4.Decompose(World*InverseParent,out Vector3 Scale,out Quaternion Rotation,out Vector3 Position)){
            Local=default;
            return false;
        }

        Local=new Transform {
            PositionX=Position.X,
            PositionY=Position.Y,
            PositionZ=Position.Z,
            RotationX=Rotation.X,
            RotationY=Rotation.Y,
            RotationZ=Rotation.Z,
            RotationW=Rotation.W,
            ScaleX=Scale.X,
            ScaleY=Scale.Y,
            ScaleZ=Scale.Z,
        };
        return true;
    }
}
