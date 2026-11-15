using System.Text;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// T3Dから、Blueprintの処理を読める疑似コードにする(ADR-0078/M0102)。グラフごとに、入口(イベント・入力・関数の入口)から
/// 実行ピン(exec)をたどって文を並べ、データのピンは、つながった純粋なノード(実行ピンの無いノード)を式として展開する。
/// 分岐(if・Sequence・FlipFlop・Switch など)は、実行の出力ピンの名前ごとに字下げしたブロックにする。
/// アニメーションの状態機械は、状態・遷移・遷移の条件式を、アニメーションのグラフは、出力から入力ポーズへさかのぼる木を書く。
/// Blueprint以外の資産(入力の割り当て、カーブ、車輪の設定など)は、最上位のオブジェクトの属性を書く。
/// </summary>
public static class BlueprintLogicWriter {
    private const int MaxExpressionDepth=12;
    /// <summary>入力の割り当て・カーブ・車輪の設定などのデータ資産は、値そのものが仕様なので、長くても切らない上限にする。</summary>
    private const int MaxValueLength=20000;
    public static int Run(string[]Arguments){
        if(Arguments.Length!=1||!Directory.Exists(Arguments[0])){
            Console.Error.WriteLine("Usage: CSharpGameEngine.Unreal.SceneImporter logic <directory with .t3d files>");
            return 2;
        }
        var Count=0;
        foreach(var File in Directory.EnumerateFiles(Arguments[0],"*.t3d").OrderBy(static Path=>Path,StringComparer.OrdinalIgnoreCase)){
            var Text=System.IO.File.ReadAllText(File,Encoding.UTF8);
            var Output=System.IO.Path.ChangeExtension(File,".logic.txt");
            System.IO.File.WriteAllText(Output,Write(T3dDocument.Parse(Text)),new UTF8Encoding(false));
            Count++;
        }
        Console.WriteLine($"Blueprint logic: {Count} file(s) written to {Arguments[0]}.");
        return 0;
    }
    public static string Write(T3dDocument Document){
        var Builder=new StringBuilder();
        var Written=new HashSet<string>(StringComparer.Ordinal);
        foreach(var Root in Document.Roots){
            Builder.Append("# ").Append(Root.Class).Append(' ').Append(Root.Name).Append('\n');
            WriteProperties(Builder,Root,"  ");
            // コンパイラが作る中継のグラフ(入力のイベントやインターフェースの関数から ExecuteUbergraph へ渡すだけ)は省く。
            // 同じグラフが2つの根(Blueprintと生成されたクラス)から見えることがあるので、パスで1回にする。
            var Graphs=Descendants(Root).Where(IsGraph).Where(static Graph=>!IsUbergraphStub(Graph)).Where(Graph=>Written.Add(Graph.Path)).ToList();
            foreach(var Graph in Graphs){
                WriteGraph(Builder,Graph);
            }
            if(Graphs.Count==0){
                foreach(var Child in Root.Children){
                    Builder.Append("  - ").Append(Child.Class).Append(' ').Append(Child.Name).Append('\n');
                    WriteProperties(Builder,Child,"      ");
                }
            }
            Builder.Append('\n');
        }
        return Builder.ToString();
    }
    private static IEnumerable<T3dObject>Descendants(T3dObject Object){
        foreach(var Child in Object.Children){
            yield return Child;
            foreach(var Grand in Descendants(Child)){
                yield return Grand;
            }
        }
    }
    private static bool IsUbergraphStub(T3dObject Graph)=>Graph.Children.Any(static Node=>Node.Property("FunctionReference").Contains("MemberName=\"ExecuteUbergraph_",StringComparison.Ordinal));
    private static bool IsGraph(T3dObject Object)=>Object.Class.EndsWith("Graph",StringComparison.Ordinal)&&Object.Children.Any(static Child=>Child.Pins.Count>0);
    /// <summary>見た目と編集の都合だけの属性。処理の解析には要らない。</summary>
    private static readonly HashSet<string>NoiseProperties=new(StringComparer.Ordinal){
        "NodePosX","NodePosY","NodeGuid","NodeWidth","NodeHeight","bCommentBubbleVisible","bCommentBubblePinned","CommentColor",
        "ErrorType","ErrorMsg","AdvancedPinDisplay","EnabledState","bHasCompilerMessage","ThumbnailInfo","BlueprintGuid","bIsNewlyCreated",
        "GraphGuid","bAllowDeletion","bAllowRenaming","CategorySorting","bCommentBubbleVisible_InDetailsPanel","bCanRenameNode","bDisplayAsDisabled",
    };
    private static void WriteProperties(StringBuilder Builder,T3dObject Object,string Indent){
        foreach(var (Key,Value) in Object.Properties){
            if(NoiseProperties.Contains(Key)){
                continue;
            }
            Builder.Append(Indent).Append(Key).Append(" = ").Append(Trim(Value)).Append('\n');
        }
    }
    private static string Trim(string Value)=>Value.Length>MaxValueLength?Value[..MaxValueLength]+" …":Value;
    private static void WriteGraph(StringBuilder Builder,T3dObject Graph){
        Builder.Append("\n## ").Append(Graph.Class).Append(' ').Append(Graph.Name).Append(" (").Append(Graph.Path).Append(")\n");
        var Nodes=Graph.Children.Where(static Child=>Child.Pins.Count>0||Child.Class.Contains("Comment",StringComparison.Ordinal)).ToList();
        var ByName=Nodes.ToDictionary(static Node=>Node.Name,StringComparer.Ordinal);
        var Context=new GraphContext(ByName);
        foreach(var Comment in Nodes.Where(static Node=>Node.Class.Contains("Comment",StringComparison.Ordinal))){
            var Text=Comment.Property("NodeComment").Trim('"');
            if(Text.Length>0){
                Builder.Append("  // ").Append(Text.Replace("\\n"," ")).Append('\n');
            }
        }
        if(Graph.Class=="AnimationStateMachineGraph"){
            WriteStateMachine(Builder,Graph,Context);
            return;
        }
        if(Graph.Class is "AnimationGraph" or "AnimationStateGraph" or "AnimationCustomTransitionGraph"){
            WritePoseTree(Builder,Nodes,Context);
            return;
        }
        if(Graph.Class=="AnimationTransitionGraph"){
            var Result=Nodes.FirstOrDefault(static Node=>Node.Class=="AnimGraphNode_TransitionResult");
            if(Result is not null){
                Builder.Append("  can enter = ").Append(Context.Expression(Result.Pins.FirstOrDefault(static Pin=>!Pin.IsOutput),0)).Append('\n');
            }
            return;
        }
        var Entries=Nodes.Where(static Node=>Node.Pins.Any(static Pin=>Pin.IsExec&&Pin.IsOutput)&&!Node.Pins.Any(static Pin=>Pin.IsExec&&!Pin.IsOutput)).ToList();
        foreach(var Entry in Entries){
            Builder.Append("\n  ").Append(Context.Label(Entry)).Append(EntryOutputs(Entry)).Append(":\n");
            Context.WriteFlow(Builder,Entry,"    ",[]);
        }
        var Orphans=Nodes.Where(static Node=>Node.Pins.Count>0&&!Node.Pins.Any(static Pin=>Pin.IsExec)&&!Node.Pins.Any(static Pin=>Pin.IsOutput&&Pin.Links.Count>0)&&Node.Pins.Any(static Pin=>!Pin.IsOutput&&Pin.Links.Count>0)).ToList();
        foreach(var Orphan in Orphans){
            // 出力がどこにもつながらない純粋なノード(結果を返すだけのグラフの最後など)。
            Builder.Append("  result ").Append(Context.Label(Orphan)).Append(' ').Append(Context.Arguments(Orphan,0)).Append('\n');
        }
    }
    /// <summary>入口が渡すデータのピン(入力の値、関数の引数など)。</summary>
    private static string EntryOutputs(T3dObject Entry){
        var Outputs=Entry.Pins.Where(static Pin=>Pin.IsOutput&&!Pin.IsExec&&!Pin.Hidden).Select(static Pin=>Pin.Name).ToList();
        return Outputs.Count==0?string.Empty:" ("+string.Join(", ",Outputs)+")";
    }
    private static void WriteStateMachine(StringBuilder Builder,T3dObject Graph,GraphContext Context){
        var States=Graph.Children.Where(static Child=>Child.Class is "AnimStateNode" or "AnimStateConduitNode" or "AnimStateAliasNode").ToList();
        foreach(var State in States){
            Builder.Append("  state ").Append(StateName(State)).Append(" (").Append(State.Class).Append(")\n");
            if(State.Class=="AnimStateAliasNode"){
                // 別名の状態は、列挙した状態(または全状態)のどれからでも遷移できる入口。
                var Aliased=T3dDocument.SplitTopLevel(State.Property("AliasedStateNodes").Trim('(',')'))
                    .Select(Path=>Graph.Children.FirstOrDefault(Child=>Child.Name==T3dDocument.ShortName(Path)))
                    .Where(static Node=>Node is not null).Select(static Node=>StateName(Node!));
                Builder.Append("    from any of = [").Append(string.Join(", ",Aliased)).Append("] global=").Append(State.Property("bGlobalAlias")).Append('\n');
            }
        }
        var Entry=Graph.Children.FirstOrDefault(static Child=>Child.Class=="AnimStateEntryNode");
        if(Entry is not null){
            var Target=Entry.Pins.Where(static Pin=>Pin.IsOutput).SelectMany(static Pin=>Pin.Links).Select(Link=>Context.Node(Link.Node)).FirstOrDefault(static Node=>Node is not null);
            Builder.Append("  entry -> ").Append(Target is null?"?":StateName(Target)).Append('\n');
        }
        foreach(var Transition in Graph.Children.Where(static Child=>Child.Class=="AnimStateTransitionNode")){
            var From=Transition.Pins.Where(static Pin=>!Pin.IsOutput).SelectMany(static Pin=>Pin.Links).Select(Link=>Context.Node(Link.Node)).FirstOrDefault(static Node=>Node is not null);
            var To=Transition.Pins.Where(static Pin=>Pin.IsOutput).SelectMany(static Pin=>Pin.Links).Select(Link=>Context.Node(Link.Node)).FirstOrDefault(static Node=>Node is not null);
            Builder.Append("  transition ").Append(From is null?"?":StateName(From)).Append(" -> ").Append(To is null?"?":StateName(To));
            foreach(var Key in new[]{"PriorityOrder","CrossfadeDuration","BlendMode","LogicType","Bidirectional","bAutomaticRuleBasedOnSequencePlayerInState","AutomaticRuleTriggerTime"}){
                var Value=Transition.Property(Key);
                if(Value.Length>0){
                    Builder.Append(' ').Append(Key).Append('=').Append(Value);
                }
            }
            Builder.Append(" (rule graph: ").Append(T3dDocument.ShortName(Transition.Property("BoundGraph"))).Append(")\n");
        }
    }
    private static string StateName(T3dObject State){
        var Alias=State.Property("StateAliasName").Trim('"');
        if(Alias.Length>0){
            return "alias "+Alias;
        }
        var Bound=State.Property("BoundGraph");
        return Bound.Length>0?T3dDocument.ShortName(Bound):State.Name;
    }
    /// <summary>アニメーションのグラフ: 出力(Root・StateResult)から入力のポーズへさかのぼり、各ノードの設定(Node=…)を添える。</summary>
    private static void WritePoseTree(StringBuilder Builder,List<T3dObject>Nodes,GraphContext Context){
        var Roots=Nodes.Where(static Node=>Node.Class is "AnimGraphNode_Root" or "AnimGraphNode_StateResult" or "AnimGraphNode_CustomTransitionResult").ToList();
        if(Roots.Count==0){
            Roots=Nodes.Where(static Node=>!Node.Pins.Any(static Pin=>Pin.IsOutput&&Pin.Links.Count>0)).ToList();
        }
        foreach(var Root in Roots){
            WritePoseNode(Builder,Root,Context,"  ",[]);
        }
    }
    private static void WritePoseNode(StringBuilder Builder,T3dObject Node,GraphContext Context,string Indent,HashSet<string>Visited){
        Builder.Append(Indent).Append(Node.Class.Replace("AnimGraphNode_","")).Append(' ').Append(Node.Name);
        var Settings=Node.Property("Node");
        if(Settings.Length>0){
            Builder.Append("  Node=").Append(Trim(Settings));
        }
        foreach(var Key in new[]{"EditorStateMachineGraph","SlotName","BoundGraph"}){
            var Value=Node.Property(Key);
            if(Value.Length>0){
                Builder.Append("  ").Append(Key).Append('=').Append(T3dDocument.ShortName(Value));
            }
        }
        Builder.Append('\n');
        if(!Visited.Add(Node.Name)){
            return;
        }
        foreach(var Pin in Node.Pins.Where(static Pin=>!Pin.IsOutput)){
            if(Pin.Links.Count==0){
                continue;
            }
            foreach(var (LinkedNode,_) in Pin.Links){
                var Source=Context.Node(LinkedNode);
                if(Source is null){
                    continue;
                }
                if(Pin.Category=="struct"&&Pin.SubCategoryObject.Contains("PoseLink",StringComparison.Ordinal)){
                    Builder.Append(Indent).Append("  <- ").Append(Pin.Name).Append(":\n");
                    WritePoseNode(Builder,Source,Context,Indent+"    ",Visited);
                }
                else {
                    Builder.Append(Indent).Append("  ").Append(Pin.Name).Append(" = ").Append(Context.Expression(Pin,0)).Append('\n');
                }
            }
        }
    }
    private sealed class GraphContext(Dictionary<string,T3dObject>ByName) {
        public T3dObject?Node(string Name)=>ByName.TryGetValue(Name,out var Node)?Node:null;
        public string Label(T3dObject Node){
            string Short(string Key)=>T3dDocument.ShortName(Node.Property(Key));
            switch(Node.Class){
                case "K2Node_Event":
                    var Custom=Node.Property("CustomFunctionName").Trim('"');
                    return "event "+(Custom.Length>0?Custom:FunctionName(Node.Property("EventReference")));
                case "K2Node_CustomEvent":
                    return "custom event "+Node.Property("CustomFunctionName").Trim('"');
                case "K2Node_EnhancedInputAction":
                    return "input "+Short("InputAction");
                case "K2Node_InputKey":
                    return "key "+Node.Property("InputKey");
                case "K2Node_InputAction":
                    return "input "+Node.Property("InputActionName").Trim('"');
                case "K2Node_InputTouch":
                    return "touch";
                case "K2Node_ComponentBoundEvent":
                    return "event "+Node.Property("ComponentPropertyName")+"."+Node.Property("DelegatePropertyName");
                case "K2Node_ActorBoundEvent":
                    return "event "+Node.Property("DelegatePropertyName");
                case "K2Node_FunctionEntry":
                    return "function "+FunctionName(Node.Property("FunctionReference"));
                case "K2Node_FunctionResult":
                    return "return";
                case "K2Node_CallFunction":
                case "K2Node_CallParentFunction":
                case "K2Node_CallArrayFunction":
                case "K2Node_CommutativeAssociativeBinaryOperator":
                case "K2Node_PromotableOperator":
                case "K2Node_CallMaterialParameterCollectionFunction":
                case "K2Node_AnimGetter":
                    return FunctionName(Node.Property("FunctionReference"));
                case "K2Node_VariableGet":
                case "K2Node_VariableSet":
                    return FunctionName(Node.Property("VariableReference"));
                case "K2Node_IfThenElse":
                    return "if";
                case "K2Node_ExecutionSequence":
                    return "sequence";
                case "K2Node_MacroInstance":
                    return "macro "+MacroName(Node.Property("MacroGraphReference"));
                case "K2Node_Timeline":
                    return "timeline "+Node.Property("TimelineName").Trim('"');
                case "K2Node_DynamicCast":
                    return "cast<"+Short("TargetType")+">";
                case "K2Node_Self":
                    return "self";
                case "K2Node_MakeStruct":
                    return "make "+Short("StructType");
                case "K2Node_BreakStruct":
                    return "break "+Short("StructType");
                case "K2Node_SpawnActorFromClass":
                    return "SpawnActor";
                case "K2Node_CreateWidget":
                    return "CreateWidget";
                case "K2Node_GetSubsystem":
                    return "GetSubsystem<"+Short("CustomClass")+">";
                case "K2Node_Select":
                    return "select";
                case "K2Node_SwitchEnum":
                    return "switch "+Short("Enum");
                case "K2Node_SwitchInteger":
                case "K2Node_SwitchString":
                case "K2Node_SwitchName":
                    return "switch";
                case "K2Node_EnumEquality":
                    return "==";
                case "K2Node_GetArrayItem":
                    return "array[]";
                case "K2Node_MakeArray":
                    return "array";
                case "K2Node_Literal":
                    return Short("ObjectRef");
                case "K2Node_GetClassDefaults":
                    return "defaults";
                case "K2Node_AddComponent":
                    return "AddComponent "+Short("TemplateType");
                default:
                    return Node.Class.Replace("K2Node_","").Replace("AnimGraphNode_","");
            }
        }
        private static string FunctionName(string Reference){
            var Fields=T3dDocument.ParseStruct(Reference);
            var Member=Fields.TryGetValue("MemberName",out var Name)?Name.Trim('"'):"?";
            if(Fields.TryGetValue("MemberParent",out var Parent)&&Parent.Length>0){
                var Owner=T3dDocument.ShortName(Parent);
                if(Owner is not ("KismetMathLibrary" or "KismetSystemLibrary" or "KismetStringLibrary" or "KismetArrayLibrary" or "GameplayStatics")){
                    return Owner+"::"+Member;
                }
            }
            return Member;
        }
        private static string MacroName(string Reference){
            var Fields=T3dDocument.ParseStruct(Reference);
            return Fields.TryGetValue("MacroGraph",out var Graph)?T3dDocument.ShortName(Graph):"?";
        }
        private static bool IsPure(T3dObject Node)=>!Node.Pins.Any(static Pin=>Pin.IsExec);
        private static bool IsBranching(T3dObject Node)=>Node.Pins.Count(static Pin=>Pin.IsExec&&Pin.IsOutput)>1;
        /// <summary>
        /// <paramref name="Start"/>(書き済みの入口・文)の先の実行の流れを書く。分岐は出力ピンごとに字下げする。
        /// 一度書いたノードへ戻るときは、その旨だけを書いて止める。
        /// </summary>
        public void WriteFlow(StringBuilder Builder,T3dObject Start,string Indent,HashSet<string>Visited){
            var Current=Start;
            var First=true;
            while(Current is not null){
                if(!First){
                    if(!Visited.Add(Current.Name)){
                        Builder.Append(Indent).Append("-> (continues at ").Append(this.Label(Current)).Append(")\n");
                        return;
                    }
                    // 中継点(Knot)は線の見た目だけなので、文にしない。
                    if(Current.Class!="K2Node_Knot"){
                        Builder.Append(Indent).Append(this.Statement(Current)).Append('\n');
                    }
                }
                else {
                    Visited.Add(Current.Name);
                }
                First=false;
                var ExecOutputs=Current.Pins.Where(static Pin=>Pin.IsExec&&Pin.IsOutput&&Pin.Links.Count>0).ToList();
                if(ExecOutputs.Count==0){
                    return;
                }
                if(ExecOutputs.Count==1&&!IsBranching(Current)&&ExecOutputs[0].Links.Count==1){
                    Current=this.Node(ExecOutputs[0].Links[0].Node);
                    continue;
                }
                foreach(var Pin in ExecOutputs){
                    Builder.Append(Indent).Append("[").Append(Pin.Name).Append("]:\n");
                    foreach(var (Next,_) in Pin.Links){
                        var Target=this.Node(Next);
                        if(Target is null){
                            continue;
                        }
                        if(!Visited.Add(Target.Name)){
                            Builder.Append(Indent).Append("  -> (continues at ").Append(this.Label(Target)).Append(")\n");
                            continue;
                        }
                        if(Target.Class!="K2Node_Knot"){
                            Builder.Append(Indent).Append("  ").Append(this.Statement(Target)).Append('\n');
                        }
                        var Branch=new HashSet<string>(Visited,StringComparer.Ordinal);
                        // WriteFlowは最初のノード(ここで書いたTarget)を書かずに、その先を書く。
                        this.WriteFlow(Builder,Target,Indent+"  ",Branch);
                        foreach(var Name in Branch){
                            Visited.Add(Name);
                        }
                    }
                }
                return;
            }
        }
        private string Statement(T3dObject Node){
            if(Node.Class=="K2Node_VariableSet"){
                var Value=Node.Pins.FirstOrDefault(static Pin=>!Pin.IsOutput&&!Pin.IsExec&&Pin.Name!="self");
                return this.Target(Node)+this.Label(Node)+" = "+(Value is null?"?":this.Expression(Value,0));
            }
            if(Node.Class=="K2Node_IfThenElse"){
                var Condition=Node.Pins.FirstOrDefault(static Pin=>Pin.Name=="Condition");
                return "if ("+(Condition is null?"?":this.Expression(Condition,0))+")";
            }
            return this.Target(Node)+this.Label(Node)+this.Arguments(Node,0);
        }
        private string Target(T3dObject Node){
            var Self=Node.Pins.FirstOrDefault(static Pin=>Pin.Name=="self"&&!Pin.IsOutput);
            return Self is not null&&Self.Links.Count>0?this.Expression(Self,1)+".":string.Empty;
        }
        public string Arguments(T3dObject Node,int Depth){
            var Parts=new List<string>();
            foreach(var Pin in Node.Pins){
                if(Pin.IsOutput||Pin.IsExec||Pin.Hidden||Pin.Name is "self" or "WorldContextObject"){
                    continue;
                }
                var Value=this.Expression(Pin,Depth+1);
                if(Value.Length==0){
                    continue;
                }
                Parts.Add(Pin.Name+"="+Value);
            }
            return "("+string.Join(", ",Parts)+")";
        }
        /// <summary>入力ピンの値。つながっていれば、相手のノードを式にする(純粋なら展開、実行ノードなら「ノード.ピン」)。</summary>
        public string Expression(T3dPin?Pin,int Depth){
            if(Pin is null){
                return "?";
            }
            if(Pin.Links.Count==0){
                return Pin.DefaultValue;
            }
            var (SourceName,SourcePinId)=Pin.Links[0];
            var Source=this.Node(SourceName);
            if(Source is null){
                return SourceName;
            }
            var SourcePin=Source.FindPin(SourcePinId);
            var PinName=SourcePin?.Name??"?";
            if(Source.Class=="K2Node_Knot"){
                return this.Expression(Source.Pins.FirstOrDefault(static Candidate=>!Candidate.IsOutput),Depth);
            }
            if(Depth>MaxExpressionDepth){
                return this.Label(Source)+"(…)";
            }
            if(!IsPure(Source)){
                return "{"+this.Label(Source)+"}."+PinName;
            }
            if(Source.Class is "K2Node_VariableGet" or "K2Node_Self" or "K2Node_Literal"){
                return this.Target(Source)+this.Label(Source);
            }
            var Outputs=Source.Pins.Count(static Candidate=>Candidate.IsOutput&&!Candidate.Hidden);
            var Call=this.Target(Source)+this.Label(Source)+this.Arguments(Source,Depth);
            return Outputs>1?Call+"."+PinName:Call;
        }
    }
}
