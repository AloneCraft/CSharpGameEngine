using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.ShaderArtifacts;
using CSharpGameEngine.ShaderGraph;
using CSharpGameEngine.ShaderGraph.Cli;
return ShaderGraphCli.Run(args);
internal static class ShaderGraphCli {
    public static int Run(string[]Arguments) {
        try {
            if(Arguments.Length==0) {
                WriteUsage();
                return 2;
            }
            return Arguments[0] switch {
                "validate"=>Validate(Arguments),
                "build"=>Build(Arguments),
                "dump-hlsl"=>DumpHlsl(Arguments),
                "publish"=>Publish(Arguments),
                "extract-subgraph-internal"=>ExtractSubgraphInternal(Arguments),
                "migrate-internal"=>MigrateInternal(Arguments),
                _=>UnknownCommand(Arguments[0]),
            };
        } catch(Exception Exception) {
            Console.Error.WriteLine(Exception);
            return 1;
        }
    }
    private static int Validate(string[]Arguments) {
        if(Arguments.Length!=2) {
            WriteUsage();
            return 2;
        }
        var Resolved=ShaderGraphDependencyComposition.Resolve(Arguments[1]);
        var Document=Resolved.Composed;
        var Validation=ShaderGraphValidator.Validate(Document);
        foreach(var Diagnostic in Validation.Diagnostics) {
            Console.Error.WriteLine($"{Diagnostic.Code}: {Diagnostic.Message}");
        }
        if(!Validation.IsValid) {
            return 1;
        }
        _=HlslSyntaxGraphConverter.Export(Document);
        Console.WriteLine($"valid {Resolved.SemanticHash} dependencies={Resolved.DependencyHash}");
        return 0;
    }
    private static int Build(string[]Arguments) {
        if(Arguments.Length<3||Arguments.Length>5) {
            WriteUsage();
            return 2;
        }
        var GraphPath=Path.GetFullPath(Arguments[1]);
        var ArtifactPath=Path.GetFullPath(Arguments[2]);
        var HlslPath=Arguments.Length>=4?Path.GetFullPath(Arguments[3]):Path.ChangeExtension(ArtifactPath,"generated.hlsl");
        var Configuration=Arguments.Length==5?Arguments[4]:"Release";
        var Resolved=ShaderGraphDependencyComposition.Resolve(GraphPath);
        var Document=Resolved.Composed;
        var Validation=ShaderGraphValidator.Validate(Document);
        if(!Validation.IsValid) {
            foreach(var Diagnostic in Validation.Diagnostics) {
                Console.Error.WriteLine($"{Diagnostic.Code}: {Diagnostic.Message}");
            }
            return 1;
        }
        var Export=HlslSyntaxGraphConverter.ExportWithSourceMap(Document);
        var Source=Export.Hlsl;
        WriteAtomicText(HlslPath,Source);
        WriteAtomicText(HlslPath+".map.json",JsonSerializer.Serialize(Export.SourceMap,new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
        var Target=MapTarget(Document.Target.Kind);
        var CompilerVersion="1.1.0-"+(string.Equals(Configuration,"Debug",StringComparison.OrdinalIgnoreCase)?"Debug":"Release");
        if(CanReuseArtifact(ArtifactPath,Target,Resolved.SemanticHash,Resolved.DependencyHash,CompilerVersion)) {
            Console.WriteLine($"up-to-date {ArtifactPath} {Resolved.DependencyHash}");
            return 0;
        }
        var Entries=new List<ShaderArtifactEntry>();
        foreach(var Contract in ShaderArtifactAbi.GetEntries(Target)) {
            var Compile=HlslCompiler.Compile(new HlslCompileRequest {
                FilePath=HlslPath,
                EntryPoint=Contract.LogicalName,
                TargetProfile=Contract.Profile,
                EnableDebugInfo=string.Equals(Configuration,"Debug",StringComparison.OrdinalIgnoreCase),
            });
            foreach(var Diagnostic in Compile.Diagnostics) {
                var Mapping=Export.SourceMap.Entries.FirstOrDefault(Entry=>Diagnostic.Line>=Entry.StartLine&&Diagnostic.Line<=Entry.EndLine);
                Console.Error.WriteLine(Mapping is null?Diagnostic.ToString():$"{Diagnostic} [node {Mapping.NodeId}]");
            }
            if(!Compile.Success) {
                return 1;
            }
            var Reflection=JsonSerializer.SerializeToUtf8Bytes(new {
                entry=Contract.LogicalName,
                profile=Contract.Profile,
                stage=Contract.Stage.ToString(),
                abi=ShaderArtifactAbi.GetHash(Target),
            });
            Entries.Add(new ShaderArtifactEntry {
                LogicalName=Contract.LogicalName,
                Profile=Contract.Profile,
                Stage=Contract.Stage,
                Dxil=Compile.Bytecode,
                Reflection=Reflection,
                PsoKey=ComputePsoKey(Compile.Bytecode,ShaderArtifactAbi.GetHash(Target),Contract.LogicalName),
            });
        }
        var SemanticHash=Resolved.SemanticHash;
        var Artifact=new ShaderArtifactDocument {
            Target=Target,
            AbiHash=ShaderArtifactAbi.GetHash(Target),
            SemanticHash=SemanticHash,
            DependencyHash=Resolved.DependencyHash,
            CompilerProvenance=new ShaderCompilerProvenance("CSharpGameEngine.ShaderGraph.Cli",CompilerVersion,"1.9.2607.13"),
            Entries=Entries,
        };
        using var Buffer=new MemoryStream();
        ShaderArtifactSerializer.Write(Buffer,in Artifact,out var Error);
        if(!string.IsNullOrEmpty(Error)) {
            Console.Error.WriteLine(Error);
            return 1;
        }
        WriteAtomicBytes(ArtifactPath,Buffer.ToArray());
        Console.WriteLine($"built {ArtifactPath} {SemanticHash}");
        return 0;
    }
    internal static bool CanReuseArtifact(string ArtifactPath,ShaderArtifactTargetKind Target,string SemanticHash,string DependencyHash,string CompilerVersion) {
        if(!File.Exists(ArtifactPath)) { return false; }
        try {
            using var Stream=File.OpenRead(ArtifactPath);
            if(!ShaderArtifactSerializer.TryRead(Stream,Target,ShaderArtifactAbi.GetHash(Target),out var Artifact,out _)||Artifact is null) { return false; }
            var Contracts=ShaderArtifactAbi.GetEntries(Target);
            return Artifact.SemanticHash==SemanticHash
                &&Artifact.DependencyHash==DependencyHash
                &&Artifact.CompilerProvenance.CompilerName=="CSharpGameEngine.ShaderGraph.Cli"
                &&Artifact.CompilerProvenance.CompilerVersion==CompilerVersion
                &&Artifact.CompilerProvenance.DxcVersion=="1.9.2607.13"
                &&Artifact.Entries.Count==Contracts.Count
                &&Contracts.All(Contract=>Artifact.Entries.Any(Entry=>Entry.LogicalName==Contract.LogicalName&&Entry.Profile==Contract.Profile&&Entry.Stage==Contract.Stage&&Entry.Dxil.Length>0));
        } catch(IOException) { return false; }
    }
    private static int DumpHlsl(string[]Arguments) {
        if(Arguments.Length<2||Arguments.Length>3) {
            WriteUsage();
            return 2;
        }
        var Resolved=ShaderGraphDependencyComposition.Resolve(Arguments[1]);
        var Source=HlslSyntaxGraphConverter.Export(Resolved.Composed);
        if(Arguments.Length==3) {
            WriteAtomicText(Path.GetFullPath(Arguments[2]),Source);
        } else {
            Console.Write(Source);
        }
        return 0;
    }
    private static int ExtractSubgraphInternal(string[]Arguments) {
        if(Arguments.Length!=5) {
            WriteUsage();
            return 2;
        }
        var RootPath=Path.GetFullPath(Arguments[1]);
        var OutputPath=Path.GetFullPath(Arguments[2]);
        var Indices=ParseIndices(Arguments[4]);
        var Root=ReadGraph(RootPath);
        var RelativePath=Path.GetRelativePath(Path.GetDirectoryName(RootPath)!,OutputPath);
        var Updated=ShaderGraphDependencyComposition.ExtractSyntaxDeclarations(Root,Indices,Arguments[3],RelativePath,out var Dependency);
        WriteAtomicText(OutputPath,ShaderGraphCanonicalization.ToCanonicalJson(Dependency));
        WriteAtomicText(RootPath,ShaderGraphCanonicalization.ToCanonicalJson(Updated));
        Console.WriteLine($"extracted {Arguments[3]} declarations={string.Join(',',Indices.OrderBy(Index=>Index))} -> {OutputPath}");
        return 0;
    }
    private static HashSet<int>ParseIndices(string Value) {
        var Result=new HashSet<int>();
        foreach(var Part in Value.Split(',',StringSplitOptions.RemoveEmptyEntries)) {
            var Range=Part.Split('-');
            if(Range.Length==1&&int.TryParse(Range[0],out var Index)) {
                Result.Add(Index);
            } else if(Range.Length==2&&int.TryParse(Range[0],out var Start)&&int.TryParse(Range[1],out var End)&&Start<=End) {
                for(var Current=Start;Current<=End;Current++) { Result.Add(Current); }
            } else {
                throw new InvalidDataException($"Invalid declaration range: {Part}");
            }
        }
        return Result;
    }
    private static int Publish(string[]Arguments) {
        if(Arguments.Length!=3) {
            WriteUsage();
            return 2;
        }
        var SourcePath=Path.GetFullPath(Arguments[1]);
        var OutputPath=Path.GetFullPath(Arguments[2]);
        WriteAtomicBytes(OutputPath,File.ReadAllBytes(SourcePath));
        Console.WriteLine($"published {OutputPath}");
        return 0;
    }
    private static int MigrateInternal(string[]Arguments) {
        if(Arguments.Length!=4||!Enum.TryParse<ShaderGraphTargetKind>(Arguments[2],true,out var Target)) {
            WriteUsage();
            return 2;
        }
        var SourcePath=Path.GetFullPath(Arguments[1]);
        var OutputPath=Path.GetFullPath(Arguments[3]);
        var Source=File.ReadAllText(SourcePath);
        var Document=HlslSyntaxGraphConverter.Import(Source,SourcePath,Target);
        var Validation=ShaderGraphValidator.Validate(Document);
        if(!Validation.IsValid) {
            throw new InvalidDataException(string.Join(Environment.NewLine,Validation.Diagnostics.Select(Diagnostic=>Diagnostic.Message)));
        }
        WriteAtomicText(OutputPath,ShaderGraphCanonicalization.ToCanonicalJson(Document));
        Console.WriteLine($"migrated {OutputPath} nodes={Document.Subgraphs.Sum(Subgraph=>Subgraph.Nodes.Count)}");
        return 0;
    }
    private static ShaderGraphDocument ReadGraph(string PathValue) {
        var FullPath=Path.GetFullPath(PathValue);
        var Document=ShaderGraphCanonicalization.FromJson(File.ReadAllText(FullPath));
        return Document??throw new InvalidDataException($"Shader Graph '{FullPath}' is empty.");
    }
    private static ShaderArtifactTargetKind MapTarget(ShaderGraphTargetKind Target) {
        return Target switch {
            ShaderGraphTargetKind.Stage=>ShaderArtifactTargetKind.Stage,
            ShaderGraphTargetKind.Model=>ShaderArtifactTargetKind.Model,
            ShaderGraphTargetKind.Triangle=>ShaderArtifactTargetKind.Triangle,
            _=>throw new ArgumentOutOfRangeException(nameof(Target)),
        };
    }
    private static string ComputePsoKey(byte[]Dxil,string AbiHash,string EntryName) {
        using var Hash=SHA256.Create();
        var Metadata=Encoding.UTF8.GetBytes(AbiHash+":"+EntryName);
        var Bytes=new byte[Dxil.Length+Metadata.Length];
        Buffer.BlockCopy(Dxil,0,Bytes,0,Dxil.Length);
        Buffer.BlockCopy(Metadata,0,Bytes,Dxil.Length,Metadata.Length);
        return Convert.ToHexString(Hash.ComputeHash(Bytes));
    }
    private static void WriteAtomicText(string OutputPath,string TextValue) {
        WriteAtomicBytes(OutputPath,new UTF8Encoding(false).GetBytes(TextValue));
    }
    private static void WriteAtomicBytes(string OutputPath,byte[]Bytes) {
        var DirectoryPath=Path.GetDirectoryName(OutputPath);
        if(!string.IsNullOrEmpty(DirectoryPath)) {
            Directory.CreateDirectory(DirectoryPath);
        }
        if(File.Exists(OutputPath)&&File.ReadAllBytes(OutputPath).AsSpan().SequenceEqual(Bytes)) {
            return;
        }
        var TemporaryPath=OutputPath+".tmp."+Guid.NewGuid().ToString("N");
        try {
            File.WriteAllBytes(TemporaryPath,Bytes);
            File.Move(TemporaryPath,OutputPath,true);
        } finally {
            if(File.Exists(TemporaryPath)) {
                File.Delete(TemporaryPath);
            }
        }
    }
    private static int UnknownCommand(string Command) {
        Console.Error.WriteLine($"Unknown command: {Command}");
        WriteUsage();
        return 2;
    }
    private static void WriteUsage() {
        Console.Error.WriteLine("validate <graph>");
        Console.Error.WriteLine("build <graph> <artifact> [generated-hlsl] [Debug|Release]");
        Console.Error.WriteLine("dump-hlsl <graph> [output]");
        Console.Error.WriteLine("publish <artifact> <output>");
        Console.Error.WriteLine("extract-subgraph-internal <root-graph> <subgraph> <name> <indices>");
        Console.Error.WriteLine("migrate-internal <hlsl> <Stage|Model|Triangle> <graph>");
    }
}