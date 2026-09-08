using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World;
public readonly record struct FramePose(LogicalVector3 Position,LogicalQuaternion Rotation,LogicalVector3 Scale) {
    public static FramePose Identity=>new(LogicalVector3.Zero,LogicalQuaternion.Identity,LogicalVector3.One);
    internal bool TryCanonicalize(out FramePose Result){
        Result=default;
        if(this.Scale.R.IsZero||this.Scale.U.IsZero||this.Scale.F.IsZero||!this.Rotation.TryNormalize(out var Rotation)){ return false; }
        Result=new FramePose(this.Position,Rotation,this.Scale);
        return true;
    }
    public bool TryTransformPoint(LogicalVector3 Point,out LogicalVector3 Result){
        Result=default;
        if(!TryScale(Point,this.Scale,out var Scaled)||!this.Rotation.TryRotate(Scaled,out var Rotated)){ return false; }
        try { Result=Rotated+this.Position;return true; }catch(OverflowException){ return false; }
    }
    public bool TryTransformVector(LogicalVector3 Vector,out LogicalVector3 Result){
        Result=default;
        return TryScale(Vector,this.Scale,out var Scaled)&&this.Rotation.TryRotate(Scaled,out Result);
    }
    public bool TryInverseTransformPoint(LogicalVector3 Point,out LogicalVector3 Result){
        Result=default;
        LogicalVector3 Relative;
        try { Relative=Point-this.Position; }catch(OverflowException){ return false; }
        if(!this.Rotation.Conjugate().TryRotate(Relative,out var Unrotated)){ return false; }
        return TryDivide(Unrotated,this.Scale,out Result);
    }
    public bool TryInverseTransformVector(LogicalVector3 Vector,out LogicalVector3 Result){
        Result=default;
        return this.Rotation.Conjugate().TryRotate(Vector,out var Unrotated)&&TryDivide(Unrotated,this.Scale,out Result);
    }
    private static bool TryScale(LogicalVector3 Left,LogicalVector3 Right,out LogicalVector3 Result){
        Result=default;
        if(!LogicalScalar.TryMultiply(Left.R,Right.R,out var R)||!LogicalScalar.TryMultiply(Left.U,Right.U,out var U)||!LogicalScalar.TryMultiply(Left.F,Right.F,out var F)){ return false; }
        Result=new LogicalVector3(R,U,F);
        return true;
    }
    private static bool TryDivide(LogicalVector3 Left,LogicalVector3 Right,out LogicalVector3 Result){
        Result=default;
        if(!LogicalScalar.TryDivide(Left.R,Right.R,out var R)||!LogicalScalar.TryDivide(Left.U,Right.U,out var U)||!LogicalScalar.TryDivide(Left.F,Right.F,out var F)){ return false; }
        Result=new LogicalVector3(R,U,F);
        return true;
    }
}
public enum ReferenceFrameErrorCode { None,InvalidId,DuplicateFrame,MissingFrame,MissingParent,MultipleParentAssignment,ConflictingCommands,Cycle,DepthExceeded,HasChildren,InvalidPose,RootMutation,StaleTick }
public readonly record struct ReferenceFrameError(ReferenceFrameErrorCode Code,ReferenceFrameId Frame,string Message) {
    public static ReferenceFrameError None=>new(ReferenceFrameErrorCode.None,default,string.Empty);
}
public sealed class ReferenceFrameSnapshot {
    private readonly IReadOnlyDictionary<ReferenceFrameId,FrameNode>Nodes;
    internal ReferenceFrameSnapshot(ReferenceFrameId Root,ulong Tick,ulong Version,IReadOnlyDictionary<ReferenceFrameId,FrameNode>Nodes){ this.Root=Root;this.Tick=Tick;this.Version=Version;this.Nodes=Nodes; }
    public ReferenceFrameId Root{get;}
    public ulong Tick{get;}
    public ulong Version{get;}
    public int Count=>this.Nodes.Count;
    public bool Contains(ReferenceFrameId Frame)=>this.Nodes.ContainsKey(Frame);
    public FramePose GetPose(ReferenceFrameId Frame)=>this.Nodes.TryGetValue(Frame,out var Node)?Node.Pose:throw new KeyNotFoundException($"Unknown reference frame: {Frame}");
    public bool TryGetParent(ReferenceFrameId Frame,out ReferenceFrameId Parent){
        Parent=default;
        if(!this.Nodes.TryGetValue(Frame,out var Node)||Node.Parent is not { } Value){ return false; }
        Parent=Value;
        return true;
    }
    public bool TryTransformPoint(LogicalVector3 Point,ReferenceFrameId Source,ReferenceFrameId Target,out LogicalVector3 Result){
        Result=default;
        if(!this.Nodes.ContainsKey(Source)||!this.Nodes.ContainsKey(Target)){ return false; }
        Span<ReferenceFrameId>SourcePath=stackalloc ReferenceFrameId[ReferenceFrameGraph.MaximumDepth+1];
        Span<ReferenceFrameId>TargetPath=stackalloc ReferenceFrameId[ReferenceFrameGraph.MaximumDepth+1];
        if(!this.TryBuildPath(Source,SourcePath,out var SourceCount)||!this.TryBuildPath(Target,TargetPath,out var TargetCount)){ return false; }
        var SourceCommonIndex=SourceCount-1;
        var TargetCommonIndex=TargetCount-1;
        while(SourceCommonIndex>0&&TargetCommonIndex>0&&SourcePath[SourceCommonIndex-1]==TargetPath[TargetCommonIndex-1]){
            SourceCommonIndex--;
            TargetCommonIndex--;
        }
        var RelativePoint=Point;
        for(var Index=0;Index<SourceCommonIndex;Index++){
            if(!this.Nodes[SourcePath[Index]].Pose.TryTransformPoint(RelativePoint,out RelativePoint)){ return false; }
        }
        for(var Index=TargetCommonIndex-1;Index>=0;Index--){
            if(!this.Nodes[TargetPath[Index]].Pose.TryInverseTransformPoint(RelativePoint,out RelativePoint)){ return false; }
        }
        Result=RelativePoint;
        return true;
    }
    public bool TryTransformAffine(LogicalAffineTransform Affine,ReferenceFrameId Source,ReferenceFrameId Target,out LogicalAffineTransform Result){
        Result=default;
        if(!this.Nodes.ContainsKey(Source)||!this.Nodes.ContainsKey(Target)){ return false; }
        Span<ReferenceFrameId>SourcePath=stackalloc ReferenceFrameId[ReferenceFrameGraph.MaximumDepth+1];
        Span<ReferenceFrameId>TargetPath=stackalloc ReferenceFrameId[ReferenceFrameGraph.MaximumDepth+1];
        if(!this.TryBuildPath(Source,SourcePath,out var SourceCount)||!this.TryBuildPath(Target,TargetPath,out var TargetCount)){ return false; }
        var SourceCommonIndex=SourceCount-1;
        var TargetCommonIndex=TargetCount-1;
        while(SourceCommonIndex>0&&TargetCommonIndex>0&&SourcePath[SourceCommonIndex-1]==TargetPath[TargetCommonIndex-1]){
            SourceCommonIndex--;
            TargetCommonIndex--;
        }
        var Relative=Affine;
        for(var Index=0;Index<SourceCommonIndex;Index++){
            if(!TryApply(this.Nodes[SourcePath[Index]].Pose,Relative,false,out Relative)){ return false; }
        }
        for(var Index=TargetCommonIndex-1;Index>=0;Index--){
            if(!TryApply(this.Nodes[TargetPath[Index]].Pose,Relative,true,out Relative)){ return false; }
        }
        Result=Relative;
        return true;
    }
    private bool TryBuildPath(ReferenceFrameId Frame,Span<ReferenceFrameId>Path,out int Count){
        Count=0;
        while(true){
            if(Count>=Path.Length||!this.Nodes.TryGetValue(Frame,out var Node)){ return false; }
            Path[Count++]=Frame;
            if(Frame==this.Root){ return true; }
            if(Node.Parent is not { } Parent){ return false; }
            Frame=Parent;
        }
    }
    private static bool TryApply(FramePose Pose,LogicalAffineTransform Affine,bool Inverse,out LogicalAffineTransform Result){
        Result=default;
        if(Inverse){
            if(!Pose.TryInverseTransformVector(Affine.RAxis,out var RAxis)
                ||!Pose.TryInverseTransformVector(Affine.UAxis,out var UAxis)
                ||!Pose.TryInverseTransformVector(Affine.FAxis,out var FAxis)
                ||!Pose.TryInverseTransformPoint(Affine.Translation,out var Translation)){ return false; }
            Result=new LogicalAffineTransform(RAxis,UAxis,FAxis,Translation);
            return true;
        }
        if(!Pose.TryTransformVector(Affine.RAxis,out var ForwardRAxis)
            ||!Pose.TryTransformVector(Affine.UAxis,out var ForwardUAxis)
            ||!Pose.TryTransformVector(Affine.FAxis,out var ForwardFAxis)
            ||!Pose.TryTransformPoint(Affine.Translation,out var ForwardTranslation)){ return false; }
        Result=new LogicalAffineTransform(ForwardRAxis,ForwardUAxis,ForwardFAxis,ForwardTranslation);
        return true;
    }
    internal readonly record struct FrameNode(ReferenceFrameId?Parent,FramePose Pose);
}
public sealed class ReferenceFrameGraph {
    public const int MaximumDepth=32;
    private readonly ReferenceFrameId Root;
    private readonly Dictionary<ReferenceFrameId,ReferenceFrameSnapshot.FrameNode>Nodes=[];
    private readonly List<AddCommand>AddCommands=[];
    private readonly List<PoseCommand>PoseCommands=[];
    private readonly List<ParentCommand>ParentCommands=[];
    private readonly List<ReferenceFrameId>RemoveCommands=[];
    private ulong Version;
    public ReferenceFrameGraph(ReferenceFrameId Root){
        if(!Root.IsValid){ throw new ArgumentException("A valid root frame ID is required.",nameof(Root)); }
        this.Root=Root;
        this.Nodes.Add(Root,new ReferenceFrameSnapshot.FrameNode(null,FramePose.Identity));
        this.Current=this.CreateSnapshot(0);
    }
    public ReferenceFrameSnapshot Current{get;private set;}
    public void Add(ReferenceFrameId Frame,ReferenceFrameId Parent,FramePose Pose)=>this.AddCommands.Add(new AddCommand(Frame,Parent,Pose));
    public void SetPose(ReferenceFrameId Frame,FramePose Pose)=>this.PoseCommands.Add(new PoseCommand(Frame,Pose));
    public void Reparent(ReferenceFrameId Frame,ReferenceFrameId Parent)=>this.ParentCommands.Add(new ParentCommand(Frame,Parent));
    public void Remove(ReferenceFrameId Frame)=>this.RemoveCommands.Add(Frame);
    public bool TryCommit(ulong Tick,out ReferenceFrameSnapshot Snapshot,out ReferenceFrameError Error){
        var Candidate=new Dictionary<ReferenceFrameId,ReferenceFrameSnapshot.FrameNode>(this.Nodes);
        try {
            if(Tick<=this.Current.Tick){ Snapshot=this.Current;Error=CreateError(ReferenceFrameErrorCode.StaleTick,this.Root,"Snapshot ticks must increase monotonically.");return false; }
            if(!this.TryApply(Candidate,out Error)){ Snapshot=this.Current;return false; }
            this.Nodes.Clear();
            foreach(var Pair in Candidate){ this.Nodes.Add(Pair.Key,Pair.Value); }
            this.Version++;
            this.Current=this.CreateSnapshot(Tick);
            Snapshot=this.Current;
            Error=ReferenceFrameError.None;
            return true;
        } finally {
            this.AddCommands.Clear();
            this.PoseCommands.Clear();
            this.ParentCommands.Clear();
            this.RemoveCommands.Clear();
        }
    }
    private bool TryApply(Dictionary<ReferenceFrameId,ReferenceFrameSnapshot.FrameNode>Candidate,out ReferenceFrameError Error){
        var DuplicateAdd=this.AddCommands.GroupBy(static Command=>Command.Frame).FirstOrDefault(static Group=>Group.Count()>1);
        if(DuplicateAdd is not null){ Error=CreateError(ReferenceFrameErrorCode.DuplicateFrame,DuplicateAdd.Key,"Frame is added more than once in one tick.");return false; }
        var RemoveSet=this.RemoveCommands.ToHashSet();
        if(RemoveSet.Count!=this.RemoveCommands.Count){ Error=CreateError(ReferenceFrameErrorCode.ConflictingCommands,this.RemoveCommands[0],"Frame is removed more than once in one tick.");return false; }
        foreach(var Command in this.AddCommands){
            if(!Command.Frame.IsValid||!Command.Parent.IsValid){ Error=CreateError(ReferenceFrameErrorCode.InvalidId,Command.Frame,"Frame and parent IDs must be valid.");return false; }
            if(Command.Frame==this.Root){ Error=CreateError(ReferenceFrameErrorCode.RootMutation,Command.Frame,"The root frame cannot be added.");return false; }
            if(Candidate.ContainsKey(Command.Frame)){ Error=CreateError(ReferenceFrameErrorCode.DuplicateFrame,Command.Frame,"Frame already exists.");return false; }
            if(RemoveSet.Contains(Command.Frame)||this.ParentCommands.Any(Parent=>Parent.Frame==Command.Frame)){ Error=CreateError(ReferenceFrameErrorCode.MultipleParentAssignment,Command.Frame,"A newly added frame cannot receive another parent command in the same tick.");return false; }
            if(!Command.Pose.TryCanonicalize(out var Pose)){ Error=CreateError(ReferenceFrameErrorCode.InvalidPose,Command.Frame,"Frame pose has a zero scale or invalid rotation.");return false; }
            Candidate.Add(Command.Frame,new ReferenceFrameSnapshot.FrameNode(Command.Parent,Pose));
        }
        foreach(var Duplicate in this.PoseCommands.GroupBy(static Command=>Command.Frame).Where(static Group=>Group.Count()>1)){ Error=CreateError(ReferenceFrameErrorCode.ConflictingCommands,Duplicate.Key,"Frame pose is assigned more than once in one tick.");return false; }
        foreach(var Command in this.PoseCommands){
            if(Command.Frame==this.Root){ Error=CreateError(ReferenceFrameErrorCode.RootMutation,Command.Frame,"The root frame pose is immutable.");return false; }
            if(RemoveSet.Contains(Command.Frame)){ Error=CreateError(ReferenceFrameErrorCode.ConflictingCommands,Command.Frame,"A removed frame cannot receive a pose in the same tick.");return false; }
            if(!Candidate.TryGetValue(Command.Frame,out var Node)){ Error=CreateError(ReferenceFrameErrorCode.MissingFrame,Command.Frame,"Pose target does not exist.");return false; }
            if(!Command.Pose.TryCanonicalize(out var Pose)){ Error=CreateError(ReferenceFrameErrorCode.InvalidPose,Command.Frame,"Frame pose has a zero scale or invalid rotation.");return false; }
            Candidate[Command.Frame]=Node with {Pose=Pose};
        }
        foreach(var Duplicate in this.ParentCommands.GroupBy(static Command=>Command.Frame).Where(static Group=>Group.Count()>1)){ Error=CreateError(ReferenceFrameErrorCode.MultipleParentAssignment,Duplicate.Key,"Frame parent is assigned more than once in one tick.");return false; }
        foreach(var Command in this.ParentCommands){
            if(Command.Frame==this.Root){ Error=CreateError(ReferenceFrameErrorCode.RootMutation,Command.Frame,"The root frame cannot be reparented.");return false; }
            if(RemoveSet.Contains(Command.Frame)){ Error=CreateError(ReferenceFrameErrorCode.ConflictingCommands,Command.Frame,"A removed frame cannot be reparented in the same tick.");return false; }
            if(!Candidate.TryGetValue(Command.Frame,out var Node)){ Error=CreateError(ReferenceFrameErrorCode.MissingFrame,Command.Frame,"Reparent target does not exist.");return false; }
            Candidate[Command.Frame]=Node with {Parent=Command.Parent};
        }
        foreach(var Frame in RemoveSet){
            if(Frame==this.Root){ Error=CreateError(ReferenceFrameErrorCode.RootMutation,Frame,"The root frame cannot be removed.");return false; }
            if(!Candidate.ContainsKey(Frame)){ Error=CreateError(ReferenceFrameErrorCode.MissingFrame,Frame,"Remove target does not exist.");return false; }
        }
        foreach(var Pair in Candidate){
            if(RemoveSet.Contains(Pair.Key)){ continue; }
            if(Pair.Value.Parent is { } Parent&&RemoveSet.Contains(Parent)){ Error=CreateError(ReferenceFrameErrorCode.HasChildren,Parent,"A frame with surviving children cannot be removed.");return false; }
        }
        foreach(var Frame in RemoveSet){ Candidate.Remove(Frame); }
        foreach(var Pair in Candidate){
            if(Pair.Key==this.Root){ continue; }
            if(Pair.Value.Parent is not { } Parent||!Candidate.ContainsKey(Parent)){ Error=CreateError(ReferenceFrameErrorCode.MissingParent,Pair.Key,"Frame parent does not exist.");return false; }
            var Seen=new HashSet<ReferenceFrameId>{Pair.Key};
            var Current=Pair.Key;
            var Depth=0;
            while(Current!=this.Root){
                if(!Candidate.TryGetValue(Current,out var Node)||Node.Parent is not { } Next){ Error=CreateError(ReferenceFrameErrorCode.MissingParent,Current,"Frame chain does not reach the root.");return false; }
                if(!Seen.Add(Next)){ Error=CreateError(ReferenceFrameErrorCode.Cycle,Pair.Key,"Frame chain contains a cycle.");return false; }
                if(++Depth>MaximumDepth){ Error=CreateError(ReferenceFrameErrorCode.DepthExceeded,Pair.Key,$"Frame depth exceeds {MaximumDepth}.");return false; }
                Current=Next;
            }
        }
        Error=ReferenceFrameError.None;
        return true;
    }
    private ReferenceFrameSnapshot CreateSnapshot(ulong Tick)=>new(this.Root,Tick,this.Version,new Dictionary<ReferenceFrameId,ReferenceFrameSnapshot.FrameNode>(this.Nodes));
    private static ReferenceFrameError CreateError(ReferenceFrameErrorCode Code,ReferenceFrameId Frame,string Message)=>new(Code,Frame,Message);
    private readonly record struct AddCommand(ReferenceFrameId Frame,ReferenceFrameId Parent,FramePose Pose);
    private readonly record struct PoseCommand(ReferenceFrameId Frame,FramePose Pose);
    private readonly record struct ParentCommand(ReferenceFrameId Frame,ReferenceFrameId Parent);
}
