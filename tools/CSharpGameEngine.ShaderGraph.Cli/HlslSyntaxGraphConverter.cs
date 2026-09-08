using System.Collections;
using System.Globalization;
using System.Reflection;
using CSharpGameEngine.ShaderGraph;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;
namespace CSharpGameEngine.ShaderGraph.Cli;
internal sealed class HlslSyntaxGraphExportResult {
    internal required string Hlsl { get;init; }
    internal required ShaderGraphSourceMap SourceMap { get;init; }
}
internal static class HlslSyntaxGraphConverter {
    private const string TranslationUnitType="UnityShaderParser.HLSL.TranslationUnitNode";
    private static readonly HashSet<string>IgnoredProperties=new(StringComparer.Ordinal) {
        "Span","OriginalSpan","Tokens","Children","Parent","Diagnostics","Code","SourceText",
    };
    public static ShaderGraphDocument Import(string Source,string FilePath,ShaderGraphTargetKind Target) {
        var Diagnostics=new List<Diagnostic>();
        var Tokens=HLSLLexer.Lex(Source,Path.GetDirectoryName(FilePath)??string.Empty,Path.GetFileName(FilePath),false,out Diagnostics);
        var Config=new HLSLParserConfig {
            BasePath=Path.GetDirectoryName(FilePath)??string.Empty,
            FileName=Path.GetFileName(FilePath),
            ThrowExceptionOnError=false,
        };
        var Includes=new List<string>();
        var Nodes=HLSLParser.ParseTopLevelDeclarations(Tokens,Config,out Diagnostics,out Includes);
        if(Diagnostics.Count>0) {
            throw new InvalidDataException(string.Join(Environment.NewLine,Diagnostics.Select(Diagnostic=>Diagnostic.ToString())));
        }
        if(Includes.Count>0) {
            throw new InvalidDataException("HLSL includes are not supported by the one-shot Graph migration.");
        }
        var RootSubgraphId=StableId.New();
        var RootNodeId=StableId.New();
        var Subgraph=new ShaderGraphSubgraph {
            Id=RootSubgraphId,
            Name=Target+"Program",
        };
        var RootNode=CreateContractNode(RootNodeId,TranslationUnitType);
        RootNode.Name="Translation Unit";
        Subgraph.Nodes.Add(RootNode);
        var NodeIds=new Dictionary<HLSLSyntaxNode,StableId>(ReferenceComparer.Instance);
        var Positions=new Dictionary<StableId,(int Depth,int Order)>();
        var Order=0;
        for(var Index=0;Index<Nodes.Count;Index++) {
            var ChildId=SerializeNode(Nodes[Index],Subgraph,NodeIds,Positions,1,ref Order);
            var Port=$"Declarations[{Index:D4}]";
            RootNode.Arguments.Add(new ShaderGraphArgument { Name=HlslSyntaxGraphContract.ChildPortPrefix+Port,Value=typeof(HLSLSyntaxNode).FullName! });
            Subgraph.Edges.Add(new ShaderGraphEdge {
                FromNodeId=ChildId,
                FromPort=HlslSyntaxGraphContract.SyntaxOutputPort,
                ToNodeId=RootNodeId,
                ToPort=Port,
            });
        }
        Positions[RootNodeId]=(0,0);
        var Layout=new ShaderGraphEditorLayout();
        foreach(var Pair in Positions.OrderBy(Pair=>Pair.Value.Depth).ThenBy(Pair=>Pair.Value.Order)) {
            Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=Pair.Key,X=Pair.Value.Depth*280,Y=Pair.Value.Order*90 });
        }
        return new ShaderGraphDocument {
            Version=1,
            Target=new ShaderGraphTarget { Kind=Target,Name=Target.ToString() },
            RootSubgraphId=RootSubgraphId,
            Subgraphs={ Subgraph },
            Layout=Layout,
        };
    }
    public static string Export(ShaderGraphDocument Document)=>ExportWithSourceMap(Document).Hlsl;
    internal static HlslSyntaxGraphExportResult ExportWithSourceMap(ShaderGraphDocument Document) {
        var Validation=ShaderGraphValidator.Validate(Document);
        if(!Validation.IsValid) {
            throw new InvalidDataException(string.Join(Environment.NewLine,Validation.Diagnostics.Select(Diagnostic=>Diagnostic.Message)));
        }
        var Root=Document.Subgraphs.Single(Subgraph=>Subgraph.Id==Document.RootSubgraphId);
        var NodeById=Root.Nodes.ToDictionary(Node=>Node.Id);
        var TranslationUnit=Root.Nodes.Single(Node=>Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)==TranslationUnitType);
        var Instances=new Dictionary<StableId,HLSLSyntaxNode>();
        foreach(var Node in Root.Nodes.Where(Node=>Node.Id!=TranslationUnit.Id)) {
            Instances.Add(Node.Id,CreateSyntaxNode(Node));
        }
        ResetCollectionProperties(Instances.Values);
        ApplyScalarProperties(Root.Nodes.Where(Node=>Node.Id!=TranslationUnit.Id),Instances);
        ApplyChildProperties(Root,NodeById,TranslationUnit.Id,Instances);
        var TopLevel=Root.Edges.Where(Edge=>Edge.ToNodeId==TranslationUnit.Id).OrderBy(Edge=>ParseIndex(Edge.ToPort)).Select(Edge=>(NodeId:Edge.FromNodeId,Node:Instances[Edge.FromNodeId])).ToArray();
        var Printer=new HLSLPrinter();
        var SourceMap=new ShaderGraphSourceMap();
        foreach(var Item in TopLevel) {
            var StartLine=CountLines(Printer.Text);
            Item.Node.Accept(Printer);
            var EndLine=Math.Max(StartLine,CountLines(Printer.Text));
            SourceMap.Entries.Add(new ShaderGraphSourceMapEntry { NodeId=Item.NodeId.ToString(),StartLine=StartLine,EndLine=EndLine });
        }
        return new HlslSyntaxGraphExportResult { Hlsl=Printer.Text,SourceMap=SourceMap };
    }
    private static int CountLines(string Text) {
        if(string.IsNullOrEmpty(Text)) { return 1; }
        var Lines=1;
        foreach(var Character in Text) { if(Character=='\n') { Lines++; } }
        return Lines;
    }    private static StableId SerializeNode(HLSLSyntaxNode SyntaxNode,ShaderGraphSubgraph Subgraph,Dictionary<HLSLSyntaxNode,StableId>NodeIds,Dictionary<StableId,(int Depth,int Order)>Positions,int Depth,ref int Order) {
        if(NodeIds.TryGetValue(SyntaxNode,out var Existing)) {
            return Existing;
        }
        var Id=StableId.New();
        NodeIds.Add(SyntaxNode,Id);
        Positions[Id]=(Depth,Order++);
        var Type=SyntaxNode.GetType();
        var Node=CreateContractNode(Id,Type.FullName!);
        Node.Name=GetDisplayName(SyntaxNode);
        Subgraph.Nodes.Add(Node);
        foreach(var Property in Type.GetProperties(BindingFlags.Public|BindingFlags.Instance).OrderBy(Property=>Property.Name,StringComparer.Ordinal)) {
            if(IgnoredProperties.Contains(Property.Name)||!Property.CanRead) {
                continue;
            }
            object? Value;
            try {
                Value=Property.GetValue(SyntaxNode);
            } catch(TargetInvocationException) {
                continue;
            }
            if(Value is null) {
                continue;
            }
            if(Value is HLSLSyntaxNode Child) {
                AddChild(Property.Name,Property.PropertyType,Child,Node,Subgraph,NodeIds,Positions,Depth,ref Order);
                continue;
            }
            if(TryGetSyntaxElementType(Property.PropertyType,out var ElementType)&&Value is IEnumerable Children) {
                var Index=0;
                foreach(var Item in Children) {
                    if(Item is HLSLSyntaxNode ChildNode) {
                        AddChild($"{Property.Name}[{Index:D4}]",ElementType,ChildNode,Node,Subgraph,NodeIds,Positions,Depth,ref Order);
                        Index++;
                    }
                }
                continue;
            }
            if(IsScalarType(Property.PropertyType)) {
                Node.Arguments.Add(new ShaderGraphArgument { Name=HlslSyntaxGraphContract.PropertyPrefix+Property.Name,Value=SerializeScalar(Value,Property.PropertyType) });
                continue;
            }
            if(TryGetScalarElementType(Property.PropertyType,out var ScalarElementType)&&Value is IEnumerable ScalarValues) {
                var Index=0;
                foreach(var Item in ScalarValues) {
                    if(Item is not null) {
                        Node.Arguments.Add(new ShaderGraphArgument { Name=$"{HlslSyntaxGraphContract.PropertyPrefix}{Property.Name}[{Index:D4}]",Value=SerializeScalar(Item,ScalarElementType) });
                        Index++;
                    }
                }
            }
        }
        return Id;
    }
    private static void AddChild(string Port,Type ExpectedType,HLSLSyntaxNode Child,ShaderGraphNode ParentNode,ShaderGraphSubgraph Subgraph,Dictionary<HLSLSyntaxNode,StableId>NodeIds,Dictionary<StableId,(int Depth,int Order)>Positions,int ParentDepth,ref int Order) {
        var ChildId=SerializeNode(Child,Subgraph,NodeIds,Positions,ParentDepth+1,ref Order);
        ParentNode.Arguments.Add(new ShaderGraphArgument { Name=HlslSyntaxGraphContract.ChildPortPrefix+Port,Value=ExpectedType.FullName! });
        Subgraph.Edges.Add(new ShaderGraphEdge {
            FromNodeId=ChildId,
            FromPort=HlslSyntaxGraphContract.SyntaxOutputPort,
            ToNodeId=ParentNode.Id,
            ToPort=Port,
        });
    }
    private static ShaderGraphNode CreateContractNode(StableId Id,string ClrType) {
        var SyntaxType=System.Type.GetType(ClrType)??typeof(HLSLSyntaxNode).Assembly.GetType(ClrType);
        var BaseTypes=new List<string>();
        for(var Current=SyntaxType?.BaseType;Current is not null;Current=Current.BaseType) {
            if(Current.FullName is not null) {
                BaseTypes.Add(Current.FullName);
            }
        }
        return new ShaderGraphNode {
            Id=Id,
            Type=HlslSyntaxGraphContract.NodeTypePrefix+ClrType.Substring("UnityShaderParser.HLSL.".Length),
            Arguments={
                new ShaderGraphArgument { Name=HlslSyntaxGraphContract.SchemaVersionArgument,Value="1" },
                new ShaderGraphArgument { Name=HlslSyntaxGraphContract.ClrTypeArgument,Value=ClrType },
                new ShaderGraphArgument { Name=HlslSyntaxGraphContract.BaseTypesArgument,Value=string.Join(";",BaseTypes) },
            },
        };
    }
    private static HLSLSyntaxNode CreateSyntaxNode(ShaderGraphNode Node) {
        var ClrType=Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)??throw new InvalidDataException("Missing HLSL CLR type.");
        var Type=typeof(HLSLSyntaxNode).Assembly.GetType(ClrType)??throw new InvalidDataException($"Unknown HLSL syntax type '{ClrType}'.");
        var TokenType=typeof(Token<TokenKind>);
        var EmptyTokens=Activator.CreateInstance(typeof(List<>).MakeGenericType(TokenType))!;
        var Instance=Activator.CreateInstance(Type,new[]{EmptyTokens}) as HLSLSyntaxNode;
        return Instance??throw new InvalidDataException($"Cannot construct HLSL syntax type '{ClrType}'.");
    }
private static void ResetCollectionProperties(IEnumerable<HLSLSyntaxNode> Instances) {
        foreach(var Instance in Instances) {
            foreach(var Property in Instance.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance)) {
                if(IgnoredProperties.Contains(Property.Name)||!Property.CanRead||Property.GetIndexParameters().Length!=0) {
                    continue;
                }
                if(!TryGetSyntaxElementType(Property.PropertyType,out var ElementType)&&!TryGetScalarElementType(Property.PropertyType,out ElementType)) {
                    continue;
                }
                try {
                    var List=Property.GetValue(Instance) as IList;
                    if(List is null&&Property.CanWrite) {
                        List=Activator.CreateInstance(typeof(List<>).MakeGenericType(ElementType)) as IList;
                        Property.SetValue(Instance,List);
                    }
                    List?.Clear();
                } catch(TargetInvocationException) {
                }
            }
        }
    }
    private static void ApplyScalarProperties(IEnumerable<ShaderGraphNode>Nodes,IReadOnlyDictionary<StableId,HLSLSyntaxNode>Instances) {
        foreach(var Node in Nodes) {
            var Instance=Instances[Node.Id];
            var Type=Instance.GetType();
            foreach(var Argument in Node.Arguments.Where(Argument=>Argument.Name.StartsWith(HlslSyntaxGraphContract.PropertyPrefix,StringComparison.Ordinal))) {
                var Name=Argument.Name.Substring(HlslSyntaxGraphContract.PropertyPrefix.Length);
                var Bracket=Name.IndexOf('[');
                var PropertyName=Bracket<0?Name:Name.Substring(0,Bracket);
                var Property=Type.GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance);
                if(Property is null) {
                    continue;
                }
                if(Bracket<0&&Property.CanWrite&&IsScalarType(Property.PropertyType)) {
                    Property.SetValue(Instance,DeserializeScalar(Argument.Value,Property.PropertyType));
                    continue;
                }
                if(Bracket>=0&&TryGetScalarElementType(Property.PropertyType,out var ElementType)) {
                    var List=Property.GetValue(Instance) as IList;
                    if(List is null&&Property.CanWrite) {
                        List=Activator.CreateInstance(typeof(List<>).MakeGenericType(ElementType)) as IList;
                        Property.SetValue(Instance,List);
                    }
                    List?.Add(DeserializeScalar(Argument.Value,ElementType));
                }
            }
        }
    }
    private static void ApplyChildProperties(ShaderGraphSubgraph Root,IReadOnlyDictionary<StableId,ShaderGraphNode>NodeById,StableId TranslationUnitId,IReadOnlyDictionary<StableId,HLSLSyntaxNode>Instances) {
        foreach(var Group in Root.Edges.Where(Edge=>Edge.ToNodeId!=TranslationUnitId).GroupBy(Edge=>(Edge.ToNodeId,Property:RemoveIndex(Edge.ToPort)))) {
            var Parent=Instances[Group.Key.ToNodeId];
            var Property=Parent.GetType().GetProperty(Group.Key.Property,BindingFlags.Public|BindingFlags.Instance)??throw new InvalidDataException($"Missing HLSL child property '{Group.Key.Property}'.");
            var Ordered=Group.OrderBy(Edge=>ParseIndex(Edge.ToPort)).ToArray();
            if(typeof(HLSLSyntaxNode).IsAssignableFrom(Property.PropertyType)) {
                Property.SetValue(Parent,Instances[Ordered[0].FromNodeId]);
                continue;
            }
            if(!TryGetSyntaxElementType(Property.PropertyType,out var ElementType)) {
                throw new InvalidDataException($"HLSL child property '{Property.Name}' is not a syntax collection.");
            }
            var List=Property.GetValue(Parent) as IList;
            if(List is null&&Property.CanWrite) {
                List=Activator.CreateInstance(typeof(List<>).MakeGenericType(ElementType)) as IList;
                Property.SetValue(Parent,List);
            }
            if(List is null) {
                throw new InvalidDataException($"HLSL child list '{Property.Name}' is unavailable.");
            }
            foreach(var Edge in Ordered) {
                List.Add(Instances[Edge.FromNodeId]);
            }
        }
    }
    private static bool IsScalarType(Type Type) {
        var Effective=Nullable.GetUnderlyingType(Type)??Type;
        return Effective.IsEnum||Effective==typeof(string)||Effective==typeof(bool)||Effective==typeof(byte)||Effective==typeof(int)||Effective==typeof(uint)||Effective==typeof(float);
    }
    private static bool TryGetSyntaxElementType(Type Type,out Type ElementType) {
        return TryGetElementType(Type,typeof(HLSLSyntaxNode),out ElementType);
    }
    private static bool TryGetScalarElementType(Type Type,out Type ElementType) {
        if(TryGetElementType(Type,typeof(object),out ElementType)&&IsScalarType(ElementType)) {
            return true;
        }
        ElementType=typeof(object);
        return false;
    }
    private static bool TryGetElementType(Type Type,Type RequiredBase,out Type ElementType) {
        var Enumerable=Type.GetInterfaces().Concat(new[]{Type}).FirstOrDefault(Candidate=>Candidate.IsGenericType&&Candidate.GetGenericTypeDefinition()==typeof(IEnumerable<>));
        if(Enumerable is not null) {
            var Candidate=Enumerable.GetGenericArguments()[0];
            if(RequiredBase==typeof(object)||RequiredBase.IsAssignableFrom(Candidate)) {
                ElementType=Candidate;
                return true;
            }
        }
        ElementType=typeof(object);
        return false;
    }
    private static string SerializeScalar(object Value,Type Type) {
        var Effective=Nullable.GetUnderlyingType(Type)??Type;
        if(Effective.IsEnum) {
            return Enum.GetName(Effective,Value)??Convert.ToInt64(Value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        return Convert.ToString(Value,CultureInfo.InvariantCulture)??string.Empty;
    }
    private static object? DeserializeScalar(string Value,Type Type) {
        var NullableType=Nullable.GetUnderlyingType(Type);
        var Effective=NullableType??Type;
        if(Effective==typeof(string)) {
            return Value;
        }
        if(Effective.IsEnum) {
            return Enum.Parse(Effective,Value,false);
        }
        if(Effective==typeof(bool)) {
            return bool.Parse(Value);
        }
        if(Effective==typeof(byte)) {
            return byte.Parse(Value,CultureInfo.InvariantCulture);
        }
        if(Effective==typeof(int)) {
            return int.Parse(Value,CultureInfo.InvariantCulture);
        }
        if(Effective==typeof(uint)) {
            return uint.Parse(Value,CultureInfo.InvariantCulture);
        }
        if(Effective==typeof(float)) {
            return float.Parse(Value,CultureInfo.InvariantCulture);
        }
        return null;
    }
    private static string GetDisplayName(HLSLSyntaxNode Node) {
        var Type=Node.GetType();
        var Identifier=Type.GetProperties(BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(Property=>Property.PropertyType==typeof(IdentifierNode))?.GetValue(Node) as IdentifierNode;
        return Identifier is null?Type.Name:Type.Name+": "+Identifier.GetPrettyPrintedCode();
    }
    private static string RemoveIndex(string Port) {
        var Bracket=Port.IndexOf('[');
        return Bracket<0?Port:Port.Substring(0,Bracket);
    }
    private static int ParseIndex(string Port) {
        var Open=Port.IndexOf('[');
        var Close=Port.IndexOf(']');
        return Open>=0&&Close>Open&&int.TryParse(Port.Substring(Open+1,Close-Open-1),NumberStyles.None,CultureInfo.InvariantCulture,out var Index)?Index:0;
    }
    private sealed class ReferenceComparer:IEqualityComparer<HLSLSyntaxNode> {
        public static readonly ReferenceComparer Instance=new();
        public bool Equals(HLSLSyntaxNode? Left,HLSLSyntaxNode? Right) {
            return ReferenceEquals(Left,Right);
        }
        public int GetHashCode(HLSLSyntaxNode Value) {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
        }
    }
}
