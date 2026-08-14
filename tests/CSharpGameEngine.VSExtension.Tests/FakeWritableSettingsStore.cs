using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.Settings;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="WritableSettingsStore"/>(抽象クラス、パラメータ無しpublicコンストラクタを持つ実物の
/// VS SDKアセンブリ)を<c>Dictionary</c>裏付けで実装したテスト用フェイク。<see cref="SceneEditorSettings"/>
/// が実際に使うメンバー(CollectionExists/CreateCollection/SetString/GetString/SetBoolean/
/// GetBoolean/PropertyExists)だけを実装し、残りは呼ばれない前提で<see cref="NotSupportedException"/>
/// を投げる。実VS無しで永続化ロジックをユニットテストするための土台(モックフレームワーク不要)。
/// </summary>
internal sealed class FakeWritableSettingsStore:WritableSettingsStore {
    private readonly HashSet<string>Collections=[];
    private readonly Dictionary<(string Collection,string Property),object>Values=[];

    public override bool CollectionExists(string CollectionPath)=>Collections.Contains(CollectionPath);

    public override void CreateCollection(string CollectionPath)=>Collections.Add(CollectionPath);

    public override bool PropertyExists(string CollectionPath,string PropertyName)=>
        Values.ContainsKey((CollectionPath,PropertyName));

    public override void SetString(string CollectionPath,string PropertyName,string Value)=>
        Values[(CollectionPath,PropertyName)]=Value;

    public override string GetString(string CollectionPath,string PropertyName)=>
        (string)Values[(CollectionPath,PropertyName)];

    public override string GetString(string CollectionPath,string PropertyName,string DefaultValue)=>
        Values.TryGetValue((CollectionPath,PropertyName),out object?Value)?(string)Value:DefaultValue;

    public override void SetBoolean(string CollectionPath,string PropertyName,bool Value)=>
        Values[(CollectionPath,PropertyName)]=Value;

    public override bool GetBoolean(string CollectionPath,string PropertyName)=>
        (bool)Values[(CollectionPath,PropertyName)];

    public override bool GetBoolean(string CollectionPath,string PropertyName,bool DefaultValue)=>
        Values.TryGetValue((CollectionPath,PropertyName),out object?Value)?(bool)Value:DefaultValue;

    // ---- SceneEditorSettingsが使わないメンバー。呼ばれたらテストの前提が崩れているのでthrowする ----

    public override bool DeleteCollection(string CollectionPath)=>throw new NotSupportedException();

    public override bool DeleteProperty(string CollectionPath,string PropertyName)=>
        throw new NotSupportedException();

    public override int GetSubCollectionCount(string CollectionPath)=>throw new NotSupportedException();

    public override int GetPropertyCount(string CollectionPath)=>throw new NotSupportedException();

    public override IEnumerable<string>GetSubCollectionNames(string CollectionPath)=>
        throw new NotSupportedException();

    public override IEnumerable<string>GetPropertyNames(string CollectionPath)=>
        throw new NotSupportedException();

    public override int GetInt32(string CollectionPath,string PropertyName)=>throw new NotSupportedException();

    public override int GetInt32(string CollectionPath,string PropertyName,int DefaultValue)=>
        throw new NotSupportedException();

    public override void SetInt32(string CollectionPath,string PropertyName,int Value)=>
        throw new NotSupportedException();

    public override uint GetUInt32(string CollectionPath,string PropertyName)=>throw new NotSupportedException();

    public override uint GetUInt32(string CollectionPath,string PropertyName,uint DefaultValue)=>
        throw new NotSupportedException();

    public override void SetUInt32(string CollectionPath,string PropertyName,uint Value)=>
        throw new NotSupportedException();

    public override long GetInt64(string CollectionPath,string PropertyName)=>throw new NotSupportedException();

    public override long GetInt64(string CollectionPath,string PropertyName,long DefaultValue)=>
        throw new NotSupportedException();

    public override void SetInt64(string CollectionPath,string PropertyName,long Value)=>
        throw new NotSupportedException();

    public override ulong GetUInt64(string CollectionPath,string PropertyName)=>throw new NotSupportedException();

    public override ulong GetUInt64(string CollectionPath,string PropertyName,ulong DefaultValue)=>
        throw new NotSupportedException();

    public override void SetUInt64(string CollectionPath,string PropertyName,ulong Value)=>
        throw new NotSupportedException();

    public override MemoryStream GetMemoryStream(string CollectionPath,string PropertyName)=>
        throw new NotSupportedException();

    public override void SetMemoryStream(string CollectionPath,string PropertyName,MemoryStream Value)=>
        throw new NotSupportedException();

    public override SettingsType GetPropertyType(string CollectionPath,string PropertyName)=>
        throw new NotSupportedException();

    public override DateTime GetLastWriteTime(string CollectionPath)=>throw new NotSupportedException();
}
