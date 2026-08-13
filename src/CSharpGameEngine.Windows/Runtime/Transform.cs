using System.Numerics;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Scene DSL?Runtime ECS???????????Transform?
/// Node.Children??????Parent???????????????<see cref="TransformMath"/>??????
/// </summary>
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

    /// <summary>Scene DSL?<c>new Transform()</c>?Unity???identity transform???????</summary>
    public Transform(){
        this.RotationW=1f;
        this.ScaleX=1f;
        this.ScaleY=1f;
        this.ScaleZ=1f;
    }
}

/// <summary>
/// Transform????????????????????
/// Editor?Runtime?????????Reparent??world pose????????????
/// </summary>
public static class TransformMath {
    public static Matrix4x4 ToMatrix(in Transform Transform)=>
        Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
        *Matrix4x4.CreateFromQuaternion(new Quaternion(Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
        *Matrix4x4.CreateTranslation(Transform.PositionX,Transform.PositionY,Transform.PositionZ);

    /// <summary>System.Numerics?row-vector??????Local * ParentWorld??????</summary>
    public static Matrix4x4 ToWorld(in Transform Local,in Matrix4x4 ParentWorld)=>ToMatrix(Local)*ParentWorld;

    /// <summary>
    /// world pose?????????????????<c>World * Inverse(NewParentWorld)</c>?????TRS??????
    /// ??????TRS???????????false??????????Source???????????
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
