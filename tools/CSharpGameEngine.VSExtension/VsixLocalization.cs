using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

internal static class VsixLocalization {
    private static CultureInfo CapturedUICulture=CultureInfo.GetCultureInfo("en-US");
    internal static CultureInfo UICulture=>CapturedUICulture;
    internal static void Capture(CultureInfo Culture){
        if(Culture is null){
            throw new ArgumentNullException(nameof(Culture));
        }
        CapturedUICulture=CultureInfo.ReadOnly((CultureInfo)Culture.Clone());
    }
}
internal static class VsixText {
    private static readonly ResourceManager Resources=new(
        "CSharpGameEngine.VSExtension.VsixStrings",typeof(VsixText).Assembly);
    private static readonly HashSet<string>LoggedMissingKeys=new(StringComparer.Ordinal);
    private static readonly object MissingKeysLock=new();
    internal static string Get(string Key)=>Get(Key,VsixLocalization.UICulture);
    internal static string Get(string Key,CultureInfo Culture){
        string?Value=Resources.GetString(Key,Culture)
            ??Resources.GetString(Key,CultureInfo.GetCultureInfo("en-US"));
        if(Value is not null){
            return Value;
        }
        LogMissingKeyOnce(Key);
        return $"[{Key}]";
    }
    internal static string Format(string Key,params object?[]Arguments)=>
        Format(VsixLocalization.UICulture,Key,Arguments);
    internal static string Format(CultureInfo Culture,string Key,params object?[]Arguments)=>
        string.Format(Culture,Get(Key,Culture),Arguments);
    private static void LogMissingKeyOnce(string Key){
        lock(MissingKeysLock){
            if(!LoggedMissingKeys.Add(Key)){
                return;
            }
        }
        try {
            ActivityLog.LogWarning(nameof(VsixText),$"Missing VSIX localization resource: {Key}");
        } catch(Exception){
            // Unit tests and early package loading may not have a Visual Studio Activity Log service.
        }
    }
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension:MarkupExtension {
    public LocExtension(){
    }
    public LocExtension(string Key){
        this.Key=Key;
    }
    public string Key{get;set;}=string.Empty;
    public override object ProvideValue(IServiceProvider ServiceProvider)=>VsixText.Get(this.Key);
}
