using System.Buffers;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.World;

public readonly record struct WorldCellSelectionQuery(
    ReferenceFrameId Frame,
    LogicalVector3 Position,
    LogicalScalar Radius,
    int MinimumLevelOfDetail=0,
    int MaximumLevelOfDetail=int.MaxValue);

public enum WorldCellSelectionErrorCode:byte {
    None,
    InvalidFrame,
    UnknownFrame,
    InvalidRadius,
    InvalidLevelRange,
    EmptyDestination,
}

public readonly record struct WorldCellSelectionResult(
    WorldCellSelectionErrorCode ErrorCode,
    int SelectedCount,
    int VisitedNodeCount) {
    public bool Succeeded=>this.ErrorCode==WorldCellSelectionErrorCode.None;
}

/// <summary>不変のフレーム単位スパースセルインデックス。選択時にグローバル座標は構築しない。</summary>
public sealed class WorldCellSpatialIndex {
    private readonly Dictionary<ReferenceFrameId,FrameIndex>Frames;

    public WorldCellSpatialIndex(WorldPartitionManifest Manifest){
        ArgumentNullException.ThrowIfNull(Manifest);
        this.Frames=Manifest.Cells.Values
            .GroupBy(static Definition=>Definition.Frame)
            .ToDictionary(static Group=>Group.Key,static Group=>new FrameIndex(Group));
    }

    public WorldCellSelectionResult Select(in WorldCellSelectionQuery Query,Span<WorldCellId>Destination){
        if(!Query.Frame.IsValid){ return new(WorldCellSelectionErrorCode.InvalidFrame,0,0); }
        if(Query.Radius<LogicalScalar.Zero){ return new(WorldCellSelectionErrorCode.InvalidRadius,0,0); }
        if(Query.MinimumLevelOfDetail<0||Query.MaximumLevelOfDetail<Query.MinimumLevelOfDetail){
            return new(WorldCellSelectionErrorCode.InvalidLevelRange,0,0);
        }
        if(Destination.IsEmpty){ return new(WorldCellSelectionErrorCode.EmptyDestination,0,0); }
        if(!this.Frames.TryGetValue(Query.Frame,out var Frame)){
            return new(WorldCellSelectionErrorCode.UnknownFrame,0,0);
        }
        return Frame.Select(Query,Destination);
    }

    private sealed class FrameIndex {
        private static readonly AxisComparer[]Comparers=[new(0),new(1),new(2)];
        private readonly Node Root;

        internal FrameIndex(IEnumerable<WorldCellDefinition>Definitions){
            Entry[]Entries=Definitions.Select(static Definition=>new Entry(Definition)).ToArray();
            this.Root=Build(Entries,0,Entries.Length,0);
        }

        internal WorldCellSelectionResult Select(in WorldCellSelectionQuery Query,Span<WorldCellId>Destination){
            var Distances=ArrayPool<LogicalScalar>.Shared.Rent(Destination.Length);
            var Levels=ArrayPool<int>.Shared.Rent(Destination.Length);
            try {
                var Selected=0;
                var Visited=0;
                Visit(this.Root,Query,Destination,Distances,Levels,ref Selected,ref Visited);
                return new(WorldCellSelectionErrorCode.None,Selected,Visited);
            } finally {
                ArrayPool<LogicalScalar>.Shared.Return(Distances);
                ArrayPool<int>.Shared.Return(Levels);
            }
        }

        private static void Visit(Node Node,in WorldCellSelectionQuery Query,Span<WorldCellId>Destination,
            LogicalScalar[]Distances,int[]Levels,ref int Selected,ref int Visited){
            Visited++;
            if(!TryDistance(Node.Minimum,Node.Maximum,Query.Position,Query.Radius,out _)){ return; }
            if(Node.Entry is { } Entry){
                if(Entry.Level<Query.MinimumLevelOfDetail||Entry.Level>Query.MaximumLevelOfDetail
                    ||!TryDistance(Entry.Minimum,Entry.Maximum,Query.Position,Query.Radius,out var Distance)){ return; }
                Insert(Entry.Id,Entry.Level,Distance,Destination,Distances,Levels,ref Selected);
                return;
            }
            Visit(Node.Left!,Query,Destination,Distances,Levels,ref Selected,ref Visited);
            Visit(Node.Right!,Query,Destination,Distances,Levels,ref Selected,ref Visited);
        }

        private static void Insert(WorldCellId Id,int Level,LogicalScalar Distance,Span<WorldCellId>Destination,
            LogicalScalar[]Distances,int[]Levels,ref int Selected){
            var InsertAt=0;
            while(InsertAt<Selected&&Compare(Distances[InsertAt],Levels[InsertAt],Destination[InsertAt],Distance,Level,Id)<=0){ InsertAt++; }
            if(InsertAt>=Destination.Length){ return; }
            var NewCount=Math.Min(Selected+1,Destination.Length);
            for(var Index=NewCount-1;Index>InsertAt;Index--){
                Destination[Index]=Destination[Index-1];
                Distances[Index]=Distances[Index-1];
                Levels[Index]=Levels[Index-1];
            }
            Destination[InsertAt]=Id;
            Distances[InsertAt]=Distance;
            Levels[InsertAt]=Level;
            Selected=NewCount;
        }

        private static int Compare(LogicalScalar LeftDistance,int LeftLevel,WorldCellId LeftId,LogicalScalar RightDistance,int RightLevel,WorldCellId RightId){
            var Distance=LeftDistance.CompareTo(RightDistance);
            if(Distance!=0){ return Distance; }
            var Level=LeftLevel.CompareTo(RightLevel);
            return Level!=0?Level:LeftId.CompareTo(RightId);
        }

        private static bool TryDistance(LogicalVector3 Minimum,LogicalVector3 Maximum,LogicalVector3 Point,LogicalScalar Radius,out LogicalScalar Distance){
            Distance=LogicalScalar.Zero;
            return TryAxisDistance(Minimum.R,Maximum.R,Point.R,Radius,ref Distance)
                &&TryAxisDistance(Minimum.U,Maximum.U,Point.U,Radius,ref Distance)
                &&TryAxisDistance(Minimum.F,Maximum.F,Point.F,Radius,ref Distance);
        }

        private static bool TryAxisDistance(LogicalScalar Minimum,LogicalScalar Maximum,LogicalScalar Point,LogicalScalar Radius,ref LogicalScalar Distance){
            LogicalScalar Gap;
            if(Point<Minimum){ if(!LogicalScalar.TrySubtract(Minimum,Point,out Gap)){ return false; } }
            else if(Point>Maximum){ if(!LogicalScalar.TrySubtract(Point,Maximum,out Gap)){ return false; } }
            else { Gap=LogicalScalar.Zero; }
            if(Gap>Radius){ return false; }
            if(Gap>Distance){ Distance=Gap; }
            return true;
        }

        private static Node Build(Entry[]Entries,int Start,int Length,int Depth){
            if(Length==1){ return new Node(Entries[Start]); }
            Array.Sort(Entries,Start,Length,Comparers[Depth%3]);
            var LeftLength=Length/2;
            var Left=Build(Entries,Start,LeftLength,Depth+1);
            var Right=Build(Entries,Start+LeftLength,Length-LeftLength,Depth+1);
            return new Node(Left,Right);
        }

        private sealed class AxisComparer(int Axis):IComparer<Entry> {
            public int Compare(Entry?Left,Entry?Right){
                if(ReferenceEquals(Left,Right)){ return 0; }
                if(Left is null){ return -1; }
                if(Right is null){ return 1; }
                var Comparison=Get(Left.Center,Axis).CompareTo(Get(Right.Center,Axis));
                return Comparison!=0?Comparison:Left.Id.CompareTo(Right.Id);
            }
            private static LogicalScalar Get(LogicalVector3 Value,int Axis)=>Axis switch { 0=>Value.R,1=>Value.U,_=>Value.F };
        }

        private sealed class Entry {
            internal Entry(WorldCellDefinition Definition){
                if(!Definition.Bounds.TryGetMinMax(out this.Minimum,out this.Maximum)){ throw new ArgumentException("Cell bounds overflow LogicalScalar.",nameof(Definition)); }
                this.Id=Definition.Id;
                this.Center=Definition.Bounds.Center;
                this.Level=Definition.LevelOfDetail;
            }
            internal readonly WorldCellId Id;
            internal readonly LogicalVector3 Center;
            internal readonly LogicalVector3 Minimum;
            internal readonly LogicalVector3 Maximum;
            internal readonly int Level;
        }

        private sealed class Node {
            internal Node(Entry Entry){ this.Entry=Entry;this.Minimum=Entry.Minimum;this.Maximum=Entry.Maximum; }
            internal Node(Node Left,Node Right){
                this.Left=Left;this.Right=Right;
                this.Minimum=Min(Left.Minimum,Right.Minimum);
                this.Maximum=Max(Left.Maximum,Right.Maximum);
            }
            internal readonly Entry?Entry;
            internal readonly Node?Left;
            internal readonly Node?Right;
            internal readonly LogicalVector3 Minimum;
            internal readonly LogicalVector3 Maximum;
            private static LogicalVector3 Min(LogicalVector3 Left,LogicalVector3 Right)=>new(
                Left.R<Right.R?Left.R:Right.R,Left.U<Right.U?Left.U:Right.U,Left.F<Right.F?Left.F:Right.F);
            private static LogicalVector3 Max(LogicalVector3 Left,LogicalVector3 Right)=>new(
                Left.R>Right.R?Left.R:Right.R,Left.U>Right.U?Left.U:Right.U,Left.F>Right.F?Left.F:Right.F);
        }
    }
}
