using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// ADR-0077 stage 4: <c>[InputActions] partial struct</c>の宣言から、入力マップのContext・ActionのID・毎フレームの
/// <c>Read</c>を生成する。IDは<c>StableId.FromName</c>で型名とメンバー名から決める(GUIDを書かせない)。
/// 値(<c>Stick</c>/<c>Look</c>/<c>Button</c>/<c>Axis</c>)の意味は<c>CSharpGameEngine.Input</c>の宣言型にあり、ここは割り当てを
/// <c>InputBinding</c>へ写すだけ。生成されたコードはランタイム型を完全修飾名で参照する(CodeAnalysisは参照しない: ADR-0001)。
/// </summary>
internal static class InputActionsEmitter {
    internal const string AttributeMetadataName="CSharpGameEngine.Input.InputActionsAttribute";
    private const string InputNamespace="CSharpGameEngine.Input";
    private const string IdNamespace="9d6c1a52-4e0b-5b8a-8f43-5a1f0c7d2e19";
    internal const string AggregatorHintName="GameSystem.InputActions.g.cs";
    private const float StickDeadZone=0.2f;
    private const float TriggerDeadZone=0.1f;

    internal sealed class Output(string Fqn,string HintName,string Source,ImmutableArray<Diagnostic>Diagnostics):IEquatable<Output> {
        internal readonly string Fqn=Fqn;
        internal readonly string HintName=HintName;
        internal readonly string Source=Source;
        internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;
        private readonly string DiagnosticKey=string.Join("|",Diagnostics.Select(static D=>D.Id+D.GetMessage()+D.Location.SourceSpan));
        public bool Equals(Output?Other)=>Other is not null&&this.Fqn==Other.Fqn&&this.Source==Other.Source&&this.DiagnosticKey==Other.DiagnosticKey;
        public override bool Equals(object?Obj)=>this.Equals(Obj as Output);
        public override int GetHashCode()=>(this.Fqn.GetHashCode()*397)^this.Source.GetHashCode();
    }

    private enum FieldKind { Stick,Look,Button,Pointer,Axis }

    /// <summary>メンバーに付ける <c>[Gamepad]</c> 1つ。control、1つだけ使う場合の stick axis、および既定値適用後の dead zone、scale、inversion（M0108）。</summary>
    private sealed class PadBinding(string Pad,string?Component,float DeadZone,float Scale,bool Invert) {
        internal readonly string Pad=Pad;
        internal readonly string?Component=Component;
        internal readonly float DeadZone=DeadZone;
        internal readonly float Scale=Scale;
        internal readonly bool Invert=Invert;
    }

    internal static Output Transform(GeneratorAttributeSyntaxContext Context,System.Threading.CancellationToken Token){
        var Type=(INamedTypeSymbol)Context.TargetSymbol;
        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();
        var Fqn=Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var HintName="GameSystem.InputActions."+Sanitize(Type.ToDisplayString())+".g.cs";
        var TypeLocation=Type.Locations.FirstOrDefault()??Location.None;

        var IsPartial=Type.DeclaringSyntaxReferences.Any(static Reference=>Reference.GetSyntax() is TypeDeclarationSyntax Declaration&&Declaration.Modifiers.Any(static Modifier=>Modifier.Text=="partial"));
        if(Type.TypeKind!=TypeKind.Struct||Type.IsGenericType||Type.ContainingType is not null||!IsPartial){
            Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.MustBePartialStruct,TypeLocation,Type.Name));
            return new Output(Fqn,HintName,string.Empty,Diagnostics.ToImmutable());
        }

        List<FieldModel>Fields=[];
        foreach(var Member in Type.GetMembers()){
            if(Member is not IFieldSymbol { IsStatic:false,IsImplicitlyDeclared:false } Field){ continue; }
            var Location=Field.Locations.FirstOrDefault()??TypeLocation;
            var Kind=KindOf(Field.Type);
            if(Kind is null||Field.DeclaredAccessibility!=Accessibility.Public){
                Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.UnsupportedField,Location,Field.Name,Type.Name));
                continue;
            }

            var Model=new FieldModel(Field.Name,Kind.Value);
            var Valid=true;
            foreach(var Attribute in Field.GetAttributes()){
                var Name=Attribute.AttributeClass?.ToDisplayString();
                if(Name==InputNamespace+".KeysAttribute"){
                    var Keys=ReadKeys(Attribute);
                    if(Kind==FieldKind.Look||Kind==FieldKind.Pointer||Kind==FieldKind.Stick&&Keys.Count!=4||(Kind==FieldKind.Button||Kind==FieldKind.Axis)&&Keys.Count==0){
                        Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.BindingDoesNotFit,Location,Field.Name,"Keys"));
                        Valid=false;
                    } else {
                        Model.Keys.Add(Keys);
                    }
                } else if(Name==InputNamespace+".NegativeKeysAttribute"){
                    var Keys=ReadKeys(Attribute);
                    if(Kind!=FieldKind.Axis||Keys.Count==0){
                        Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.BindingDoesNotFit,Location,Field.Name,"NegativeKeys"));
                        Valid=false;
                    } else {
                        Model.NegativeKeys.Add(Keys);
                    }
                } else if(Name==InputNamespace+".GamepadAttribute"&&Attribute.ConstructorArguments.Length>=1){
                    var Pad=EnumName(Attribute.ConstructorArguments[0]);
                    var Component=Attribute.ConstructorArguments.Length>=2?EnumName(Attribute.ConstructorArguments[1]):null;
                    var IsStick=Pad is "LeftStick" or "RightStick";
                    var IsTrigger=Pad is "LeftTrigger" or "RightTrigger";
                    var IsButton=Pad is "South" or "East" or "West" or "North" or "LeftShoulder" or "RightShoulder" or "Back" or "Start";
                    var Fits=Pad is not null&&(Attribute.ConstructorArguments.Length<2||Component is not null)&&Kind switch {
                        FieldKind.Stick=>IsStick&&Component is null,
                        FieldKind.Look=>IsStick,
                        FieldKind.Button=>IsButton,
                        FieldKind.Axis=>IsTrigger||IsStick&&Component is not null||IsButton,
                        _=>false,
                    };
                    if(Fits&&(IsTrigger||IsButton)&&Component is not null){ Fits=false; }
                    if(!Fits){
                        Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.BindingDoesNotFit,Location,Field.Name,"Gamepad("+Pad+(Component is null?string.Empty:","+Component)+")"));
                        Valid=false;
                    } else if(ReadPad(Attribute,Pad!,Component,IsButton,Location,Field.Name,Diagnostics) is { } Binding){
                        Model.Pads.Add(Binding);
                    } else {
                        Valid=false;
                    }
                } else if(Name==InputNamespace+".MouseAttribute"){
                    var IsMouseButton=Attribute.ConstructorArguments.Length==1&&Attribute.ConstructorArguments[0].Type?.Name=="MouseButton";
                    if(IsMouseButton&&Kind==FieldKind.Button&&EnumName(Attribute.ConstructorArguments[0]) is { } MouseButtonName){
                        Model.MouseButtons.Add(MouseButtonName);
                    } else if(!IsMouseButton&&Kind==FieldKind.Look&&Attribute.ConstructorArguments.Length==1&&EnumName(Attribute.ConstructorArguments[0]) is { } MouseAxisName){
                        Model.MouseAxes.Add(MouseAxisName);
                    } else {
                        Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.BindingDoesNotFit,Location,Field.Name,"Mouse"));
                        Valid=false;
                    }
                } else if(Name==InputNamespace+".PointerAttribute"){
                    if(Kind!=FieldKind.Pointer){
                        Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.BindingDoesNotFit,Location,Field.Name,"Pointer"));
                        Valid=false;
                    } else {
                        Model.HasPointer=true;
                    }
                }
            }

            if(Valid&&Model.Keys.Count==0&&Model.NegativeKeys.Count==0&&Model.Pads.Count==0&&Model.MouseAxes.Count==0&&Model.MouseButtons.Count==0&&!Model.HasPointer){
                Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.NoBinding,Location,Field.Name,Type.Name));
                Valid=false;
            }

            if(Valid){ Fields.Add(Model); }
        }

        if(Diagnostics.Count>0){
            return new Output(Fqn,HintName,string.Empty,Diagnostics.ToImmutable());
        }

        return new Output(Fqn,HintName,BuildSource(Type,Fqn,Fields),Diagnostics.ToImmutable());
    }

    /// <summary>
    /// <c>[Gamepad]</c> に記述された dead zone、scale、inversion を読み取ります（M0108）。dead zone が 0..1 の範囲外、scale が0より大きい有限値でない（NaN を含む。記述済み値を「未記述」と誤認しません）、または button にいずれかの値が指定されている場合は INPUT005 とし、null を返します。dead zone が省略された場合、stick は0.2、trigger は0.1です。
    /// </summary>
    private static PadBinding?ReadPad(AttributeData Attribute,string Pad,string?Component,bool IsButton,Location Location,string FieldName,ImmutableArray<Diagnostic>.Builder Diagnostics){
        float? DeadZone=null;
        var Scale=1f;
        var Invert=false;
        var Failed=false;
        foreach(var Argument in Attribute.NamedArguments){
            var Text=Argument.Key;
            if(IsButton){
                Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.InvalidBindingParameter,Location,FieldName,Text,"a button has no dead zone, scale or inversion"));
                Failed=true;
            } else if(Text=="DeadZone"&&Argument.Value.Value is float Dead){
                if(!(Dead>=0f&&Dead<1f)){
                    Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.InvalidBindingParameter,Location,FieldName,"DeadZone="+Dead.ToString("R",CultureInfo.InvariantCulture),"it must be at least 0 and below 1"));
                    Failed=true;
                } else {
                    DeadZone=Dead;
                }
            } else if(Text=="Scale"&&Argument.Value.Value is float Factor){
                if(!(Factor>0f&&!float.IsInfinity(Factor))){
                    Diagnostics.Add(Diagnostic.Create(InputActionsDiagnostics.InvalidBindingParameter,Location,FieldName,"Scale="+Factor.ToString("R",CultureInfo.InvariantCulture),"it must be a finite number above 0 (use Invert to flip the sign)"));
                    Failed=true;
                } else {
                    Scale=Factor;
                }
            } else if(Text=="Invert"&&Argument.Value.Value is bool Flip){
                Invert=Flip;
            }
        }

        if(Failed){ return null; }
        var IsTrigger=Pad is "LeftTrigger" or "RightTrigger";
        return new PadBinding(Pad,Component,IsButton?0f:DeadZone??(IsTrigger?TriggerDeadZone:StickDeadZone),Scale,Invert);
    }

    internal static void Emit(SourceProductionContext Context,ImmutableArray<Output>Outputs){
        List<Output>Valid=[];
        foreach(var Output in Outputs.OrderBy(static O=>O.Fqn,StringComparer.Ordinal)){
            foreach(var Diagnostic in Output.Diagnostics){ Context.ReportDiagnostic(Diagnostic); }
            if(Output.Source.Length>0){
                Context.AddSource(Output.HintName,SourceText.From(Output.Source,Encoding.UTF8));
                Valid.Add(Output);
            }
        }

        if(Valid.Count==0){ return; }
        List<string>Lines=[
            "// <auto-generated/>",
            "#nullable enable",
            "namespace CSharpGameEngine.Generated;",
            "/// <summary>Every [InputActions] struct of the project as one input map (ADR-0077).</summary>",
            "internal static class GeneratedInputActions {",
            "    internal static global::CSharpGameEngine.Input.InputServer CreateServer() {",
            "        var Contexts = new global::CSharpGameEngine.Input.InputContext[] {",
        ];
        foreach(var Output in Valid){ Lines.Add($"            {Output.Fqn}.CreateContext(),"); }
        Lines.Add("        };");
        Lines.Add("        var Server = new global::CSharpGameEngine.Input.InputServer(new global::CSharpGameEngine.Input.InputActionMap(Contexts));");
        Lines.Add("        foreach (var Context in Contexts) { Server.SetContextActive(Context.Id, true, default); }");
        Lines.Add("        return Server;");
        Lines.Add("    }");
        Lines.Add("}");
        Context.AddSource(AggregatorHintName,SourceText.From(string.Join("\n",Lines),Encoding.UTF8));
    }

    private static string BuildSource(INamedTypeSymbol Type,string Fqn,List<FieldModel>Fields){
        const string Core="global::CSharpGameEngine.Core";
        const string Input="global::CSharpGameEngine.Input";
        List<string>Lines=["// <auto-generated/>","#nullable enable"];
        var Namespace=Type.ContainingNamespace.IsGlobalNamespace?null:Type.ContainingNamespace.ToDisplayString();
        if(Namespace is not null){ Lines.Add($"namespace {Namespace};"); }
        Lines.Add($"{AccessibilityText(Type.DeclaredAccessibility)}partial struct {Type.Name} {{");
        var TypeKey=Type.ToDisplayString();
        Lines.Add($"    private static readonly {Core}.StableId IdNamespace = {Core}.StableId.Parse(\"{IdNamespace}\");");
        Lines.Add($"    internal static readonly {Core}.InputContextId ContextId = new({Core}.StableId.FromName(IdNamespace, \"{TypeKey}\"));");
        foreach(var Field in Fields){
            foreach(var Suffix in Field.ActionSuffixes()){
                Lines.Add($"    private static readonly {Core}.InputActionId Id_{Field.Name}{Suffix} = new({Core}.StableId.FromName(IdNamespace, \"{TypeKey}.{Field.Name}{Suffix}\"));");
            }
        }

        Lines.Add($"    internal static {Input}.InputContext CreateContext() {{");
        Lines.Add($"        return new {Input}.InputContext(ContextId, \"{TypeKey}\", 0, new {Input}.InputBinding[] {{");
        foreach(var Field in Fields){
            foreach(var Binding in Field.Bindings()){ Lines.Add("            "+Binding+","); }
        }
        Lines.Add("        });");
        Lines.Add("    }");

        Lines.Add($"    internal static {Fqn} Read({Input}.InputServer Server, float DeltaSeconds, {Input}.Pointer CursorPointer) {{");
        Lines.Add("        var Frame = Server.CurrentFrame;");
        Lines.Add($"        var Result = new {Fqn}();");
        foreach(var Field in Fields){ Lines.AddRange(Field.ReadLines(Core,Input)); }
        Lines.Add("        return Result;");
        Lines.Add("    }");
        Lines.Add("}");
        return string.Join("\n",Lines);
    }

    private sealed class FieldModel(string Name,FieldKind Kind) {
        internal readonly string Name=Name;
        internal readonly FieldKind Kind=Kind;
        internal readonly List<List<int>>Keys=[];
        internal readonly List<List<int>>NegativeKeys=[];
        internal readonly List<PadBinding>Pads=[];
        internal readonly List<string>MouseAxes=[];
        internal readonly List<string>MouseButtons=[];
        internal bool HasPointer;

        internal IEnumerable<string>ActionSuffixes(){
            if(this.Kind==FieldKind.Pointer){
                yield break;
            }
            if(this.Kind==FieldKind.Look){
                if(this.MouseAxes.Count>0){ yield return "_Mouse"; }
                if(this.Pads.Count>0){ yield return "_Stick"; }
            } else {
                yield return string.Empty;
            }
        }

        /// <summary>生成コード用の float リテラル。suffix を付けた round-trip 形式です。</summary>
        private static string Float(float Value)=>Value.ToString("R",CultureInfo.InvariantCulture)+"f";

        /// <summary>pad binding の <c>DeadZone</c> と <c>Scale</c> の引数（scale が1の場合は省略）。</summary>
        private static string Shaping(PadBinding Pad)=>", DeadZone: "+Float(Pad.DeadZone)+(Pad.Scale==1f?string.Empty:", Scale: "+Float(Pad.Scale));

        internal IEnumerable<string>Bindings(){
            const string Core="global::CSharpGameEngine.Core";
            const string Input="global::CSharpGameEngine.Input";
            const string Adapter="global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter";
            string Key(int Code)=> $"new {Core}.InputControl({Core}.InputDeviceKind.Keyboard, {Adapter}.KeyCode(0x{Code:X2}))";
            string Negate="new "+Input+".InputNegateModifier()";
            string Swizzle="new "+Input+".InputSwizzleModifier()";
            string[] InvertItems(PadBinding Pad)=>Pad.Invert?new[] { Negate }:Array.Empty<string>();
            string Modifiers(params string[] Items)=>Items.Length==0?string.Empty:$", Modifiers: new {Input}.InputModifier[] {{ {string.Join(", ",Items)} }}";
            string PadButtonCode(string Pad)=>Pad switch { "South"=>"Gamepad_FaceButton_Bottom","East"=>"Gamepad_FaceButton_Right","West"=>"Gamepad_FaceButton_Left","LeftShoulder"=>"Gamepad_LeftShoulder","RightShoulder"=>"Gamepad_RightShoulder","Back"=>"Gamepad_Special_Left","Start"=>"Gamepad_Special_Right",_=>"Gamepad_FaceButton_Top" };
            string PadButton(PadBinding Pad)=> $"new {Core}.InputControl({Core}.InputDeviceKind.Gamepad, \"{PadButtonCode(Pad.Pad)}\")";
            if(this.Kind==FieldKind.Stick){
                foreach(var Set in this.Keys){
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Set[0])}, Modifiers: new {Input}.InputModifier[] {{ {Swizzle} }})";
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Set[1])}, Modifiers: new {Input}.InputModifier[] {{ {Swizzle}, {Negate} }})";
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Set[2])}, Modifiers: new {Input}.InputModifier[] {{ {Negate} }})";
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Set[3])})";
                }
                foreach(var Pad in this.Pads){
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {Adapter}.Gamepad{Pad.Pad}{Shaping(Pad)}{Modifiers(InvertItems(Pad))})";
                }
            } else if(this.Kind==FieldKind.Look){
                foreach(var Mouse in this.MouseAxes){
                    // マウスの delta は、上へ動かすと上を見るよう Y を反転します。DeltaX と DeltaY はそれぞれ1軸分を保持します。
                    string[] Items=Mouse switch {
                        "DeltaX"=>new[] { $"new {Input}.InputMaskModifier(X: false, Y: true)" },
                        "DeltaY"=>new[] { $"new {Input}.InputMaskModifier(X: true, Y: false)",$"new {Input}.InputNegateModifier(X: false, Y: true)" },
                        _=>new[] { $"new {Input}.InputNegateModifier(X: false, Y: true)" },
                    };
                    yield return $"new {Input}.InputBinding(Id_{this.Name}_Mouse, new {Core}.InputControl({Core}.InputDeviceKind.Mouse, \"Delta\", {Core}.InputControlKind.RelativeAxis2D){Modifiers(Items)})";
                }
                foreach(var Pad in this.Pads){
                    if(Pad.Component is null){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}_Stick, {Adapter}.Gamepad{Pad.Pad}{Shaping(Pad)}{Modifiers(InvertItems(Pad))})";
                    } else {
                        // stick の1軸。対応する control を使い、Y の場合は値が look の Y 軸へ移動します。
                        var AxisItems=new List<string>();
                        if(Pad.Component=="Y"){ AxisItems.Add(Swizzle); }
                        if(Pad.Invert){ AxisItems.Add(Negate); }
                        yield return $"new {Input}.InputBinding(Id_{this.Name}_Stick, {Adapter}.Gamepad{Pad.Pad.Replace("Stick",string.Empty)}{Pad.Component}{Shaping(Pad)}{Modifiers(AxisItems.ToArray())})";
                    }
                }
            } else if(this.Kind==FieldKind.Axis){
                foreach(var Set in this.Keys){
                    foreach(var Code in Set){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Code)})";
                    }
                }
                foreach(var Set in this.NegativeKeys){
                    foreach(var Code in Set){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Code)}{Modifiers(Negate)})";
                    }
                }
                foreach(var Pad in this.Pads){
                    var Invert=Pad.Invert?Modifiers(Negate):string.Empty;
                    if(Pad.Pad is "LeftTrigger" or "RightTrigger"){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {Adapter}.Gamepad{Pad.Pad}{Shaping(Pad)}{Invert})";
                    } else if(Pad.Component is not null){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {Adapter}.Gamepad{Pad.Pad.Replace("Stick",string.Empty)}{Pad.Component}{Shaping(Pad)}{Invert})";
                    } else {
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {PadButton(Pad)})";
                    }
                }
            } else {
                foreach(var Set in this.Keys){
                    foreach(var Code in Set){
                        yield return $"new {Input}.InputBinding(Id_{this.Name}, {Key(Code)})";
                    }
                }
                foreach(var MouseButton in this.MouseButtons){
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, new {Core}.InputControl({Core}.InputDeviceKind.Mouse, \"{MouseButton}\"))";
                }
                foreach(var Pad in this.Pads){
                    yield return $"new {Input}.InputBinding(Id_{this.Name}, {PadButton(Pad)})";
                }
            }
        }

        internal IEnumerable<string>ReadLines(string Core,string Input){
            if(this.Kind==FieldKind.Pointer){
                yield return $"        Result.{this.Name} = CursorPointer;";
                yield break;
            }
            if(this.Kind==FieldKind.Stick){
                yield return $"        {{ var V = Server.ReadValue(Id_{this.Name}); Result.{this.Name} = new {Input}.Stick(V.X, V.Y); }}";
            } else if(this.Kind==FieldKind.Axis){
                yield return $"        {{ var V = Server.ReadValue(Id_{this.Name}); Result.{this.Name} = {Input}.Axis.FromSum(V.X); }}";
            } else if(this.Kind==FieldKind.Look){
                var Mouse=this.MouseAxes.Count>0?$"Server.ReadValue(Id_{this.Name}_Mouse)":$"{Core}.InputValue.Zero";
                var Stick=this.Pads.Count>0?$"Server.ReadValue(Id_{this.Name}_Stick)":$"{Core}.InputValue.Zero";
                yield return "        {";
                yield return $"            var M = {Mouse};";
                yield return $"            var S = {Stick};";
                yield return $"            Result.{this.Name} = new {Input}.Look(M.X * {Input}.Look.MouseTurnsPerCount + S.X * {Input}.Look.StickTurnsPerSecond * DeltaSeconds, M.Y * {Input}.Look.MouseTurnsPerCount + S.Y * {Input}.Look.StickTurnsPerSecond * DeltaSeconds);";
                yield return "        }";
            } else {
                yield return "        {";
                yield return "            var Pressed = false; var Released = false;";
                yield return $"            foreach (var Action in Frame) {{ if (Action.ActionId != Id_{this.Name}) continue; if (Action.Phase == {Core}.InputActionPhase.Started) Pressed = true; else if (Action.Phase == {Core}.InputActionPhase.Canceled) Released = true; }}";
                yield return $"            var Held = !Server.ReadValue(Id_{this.Name}).IsZero;";
                yield return $"            Result.{this.Name} = new {Input}.Button(Held, Pressed, Released, Server.TrackHold(Id_{this.Name}, Held, Pressed, Released, DeltaSeconds));";
                yield return "        }";
            }
        }
    }

    private static FieldKind?KindOf(ITypeSymbol Type)=>Type.ToDisplayString() switch {
        InputNamespace+".Stick"=>FieldKind.Stick,
        InputNamespace+".Look"=>FieldKind.Look,
        InputNamespace+".Button"=>FieldKind.Button,
        InputNamespace+".Pointer"=>FieldKind.Pointer,
        InputNamespace+".Axis"=>FieldKind.Axis,
        _=>null,
    };

    private static List<int>ReadKeys(AttributeData Attribute){
        List<int>Codes=[];
        if(Attribute.ConstructorArguments.Length==1&&Attribute.ConstructorArguments[0].Kind==TypedConstantKind.Array){
            foreach(var Value in Attribute.ConstructorArguments[0].Values){
                if(Value.Value is int Code){ Codes.Add(Code); }
            }
        } else {
            foreach(var Argument in Attribute.ConstructorArguments){
                if(Argument.Value is int Code){ Codes.Add(Code); }
            }
        }

        return Codes;
    }

    private static string?EnumName(TypedConstant Constant){
        if(Constant.Type is not INamedTypeSymbol Enum||Constant.Value is null){ return null; }
        foreach(var Member in Enum.GetMembers().OfType<IFieldSymbol>()){
            if(Member.HasConstantValue&&Equals(Member.ConstantValue,Constant.Value)){ return Member.Name; }
        }

        return null;
    }

    private static string AccessibilityText(Accessibility Accessibility)=>Accessibility switch {
        Accessibility.Public=>"public ",
        Accessibility.Internal=>"internal ",
        _=>string.Empty,
    };

    private static string Sanitize(string Name){
        var Builder=new StringBuilder();
        foreach(var Character in Name){ Builder.Append(char.IsLetterOrDigit(Character)?Character:'_'); }
        return Builder.ToString();
    }
}

internal static class InputActionsDiagnostics {
    private const string Category="CSharpGameEngine.Input.CodeGen";

    internal static readonly DiagnosticDescriptor MustBePartialStruct=new(
        id:"INPUT001",
        title:"An [InputActions] type must be a non-generic, non-nested partial struct",
        messageFormat:"'{0}' has [InputActions] but is not a non-generic, non-nested partial struct",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnsupportedField=new(
        id:"INPUT002",
        title:"An [InputActions] member must be a public Stick, Look, Button, Axis or Pointer field",
        messageFormat:"Member '{0}' of '{1}' must be a public instance field of type Stick, Look, Button, Axis or Pointer",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor BindingDoesNotFit=new(
        id:"INPUT003",
        title:"A binding does not fit the member's type",
        messageFormat:"Member '{0}' cannot use {1}: Stick takes 4 keys or a gamepad stick, Look takes the mouse delta (whole, DeltaX or DeltaY) or a gamepad stick (whole or one axis), Button takes keys, mouse buttons or a gamepad button, Axis takes Keys/NegativeKeys, a gamepad trigger, one axis of a gamepad stick or a gamepad button",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor NoBinding=new(
        id:"INPUT004",
        title:"An [InputActions] member has no binding",
        messageFormat:"Member '{0}' of '{1}' has no [Keys], [Gamepad] or [Mouse] attribute, so it can never change",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor InvalidBindingParameter=new(
        id:"INPUT005",
        title:"A [Gamepad] dead zone, scale or inversion is not valid",
        messageFormat:"Member '{0}' has {1} on a [Gamepad]: {2}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
}
