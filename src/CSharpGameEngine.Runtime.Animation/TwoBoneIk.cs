using System.Numerics;
namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// Analytic two-bone inverse kinematics (a thigh, a calf and a foot; an upper arm, a forearm and a hand). <see cref="Solve"/> turns the
/// upper and middle joints so the end joint reaches a target, keeps the bend on the side it already had (or a hint when the limb is
/// straight), keeps the end joint's own orientation in model space, and never stretches the limbs: a target out of reach is approached
/// along the line to it. Joint positions come from the pose's model matrices, so the skeleton's own axes do not matter.
/// </summary>
public static class TwoBoneIk {
    private const float Epsilon=1e-5f;
    /// <summary>
    /// Edits <paramref name="Pose"/> (local joint poses) so that joint <paramref name="End"/> is at <paramref name="Target"/> (a model-space point).
    /// <paramref name="Model"/> is scratch of at least <see cref="Skeleton.JointCount"/> matrices. <paramref name="BendHint"/> is the direction the
    /// middle joint bends toward when the limb starts out straight. Returns false and leaves the pose alone when a limb has no length.
    /// </summary>
    public static bool Solve(Skeleton Skeleton,Span<JointPose>Pose,Span<Matrix4x4>Model,int Upper,int Middle,int End,Vector3 Target,Vector3 BendHint){
        ArgumentNullException.ThrowIfNull(Skeleton);
        if(Skeleton.ParentOf(Middle)!=Upper||Skeleton.ParentOf(End)!=Middle){
            throw new ArgumentException("The three joints must form a chain: the middle joint is the upper joint's child and the end joint the middle joint's.");
        }
        if(!IsFinite(Target)){
            return false;
        }
        Skeleton.ComputeModel(Pose,Model);
        var A=Model[Upper].Translation;
        var B=Model[Middle].Translation;
        var C=Model[End].Translation;
        var Length1=Vector3.Distance(A,B);
        var Length2=Vector3.Distance(B,C);
        if(Length1<Epsilon||Length2<Epsilon){
            return false;
        }
        var ToTarget=Target-A;
        var Distance=ToTarget.Length();
        if(Distance<Epsilon){
            return false;
        }
        var Direction=ToTarget/Distance;
        var Reach=Math.Clamp(Distance,MathF.Abs(Length1-Length2)+Epsilon,Length1+Length2-Epsilon);
        // The bend: the part of the middle joint's offset from the A-C line that is across the new line, or the hint for a straight limb.
        var Bend=PerpendicularTo(B-A,Vector3.Normalize(C-A));
        if(Bend.LengthSquared()<Epsilon){
            Bend=BendHint;
        }
        Bend=PerpendicularTo(Bend,Direction);
        if(Bend.LengthSquared()<Epsilon){
            Bend=PerpendicularTo(Vector3.UnitY,Direction);
        }
        if(Bend.LengthSquared()<Epsilon){
            Bend=PerpendicularTo(Vector3.UnitX,Direction);
        }
        Bend=Vector3.Normalize(Bend);
        var Along=((Length1*Length1)-(Length2*Length2)+(Reach*Reach))/(2f*Reach);
        var Height=MathF.Sqrt(MathF.Max((Length1*Length1)-(Along*Along),0f));
        var NewB=A+(Direction*Along)+(Bend*Height);
        var NewC=A+(Direction*Reach);
        var Upper1=Vector3.Normalize(B-A);
        var Lower1=Vector3.Normalize(C-B);
        var FirstTurn=FromTo(Upper1,Vector3.Normalize(NewB-A));
        var SecondTurn=FromTo(Vector3.TransformNormal(Lower1,FirstTurn),Vector3.Normalize(NewC-NewB));
        var UpperRotation=RotationOf(Model[Upper]);
        var MiddleRotation=RotationOf(Model[Middle]);
        var EndRotation=RotationOf(Model[End]);
        var NewUpper=UpperRotation*FirstTurn;
        var NewMiddle=MiddleRotation*FirstTurn*SecondTurn;
        var UpperParent=Skeleton.ParentOf(Upper);
        var ParentRotation=RotationOf(UpperParent<0?Skeleton.RootTransform:Model[UpperParent]);
        Pose[Upper].Rotation=ToQuaternion(NewUpper*Matrix4x4.Transpose(ParentRotation));
        Pose[Middle].Rotation=ToQuaternion(NewMiddle*Matrix4x4.Transpose(NewUpper));
        Pose[End].Rotation=ToQuaternion(EndRotation*Matrix4x4.Transpose(NewMiddle));
        return true;
    }
    private static bool IsFinite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    private static Vector3 PerpendicularTo(Vector3 Vector,Vector3 Axis)=>Vector-(Axis*Vector3.Dot(Vector,Axis));
    /// <summary>The rotation (row-vector convention: <c>v * result</c>) taking the unit vector <paramref name="From"/> to the unit vector <paramref name="To"/> by the shortest arc.</summary>
    internal static Matrix4x4 FromTo(Vector3 From,Vector3 To){
        var Axis=Vector3.Cross(From,To);
        var Sine=Axis.Length();
        var Cosine=Vector3.Dot(From,To);
        if(Sine<Epsilon){
            if(Cosine>0f){
                return Matrix4x4.Identity;
            }
            var Other=MathF.Abs(From.X)<0.9f?Vector3.UnitX:Vector3.UnitY;
            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(From,Other)),MathF.PI);
        }
        return Matrix4x4.CreateFromAxisAngle(Axis/Sine,MathF.Atan2(Sine,Cosine));
    }
    /// <summary>The rotation part of <paramref name="Matrix"/> with the scale taken out of each row.</summary>
    internal static Matrix4x4 RotationOf(Matrix4x4 Matrix){
        var X=Vector3.Normalize(new Vector3(Matrix.M11,Matrix.M12,Matrix.M13));
        var Y=Vector3.Normalize(new Vector3(Matrix.M21,Matrix.M22,Matrix.M23));
        var Z=Vector3.Normalize(new Vector3(Matrix.M31,Matrix.M32,Matrix.M33));
        return new Matrix4x4(X.X,X.Y,X.Z,0f,Y.X,Y.Y,Y.Z,0f,Z.X,Z.Y,Z.Z,0f,0f,0f,0f,1f);
    }
    internal static Quaternion ToQuaternion(Matrix4x4 Rotation)=>Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Rotation));
}
