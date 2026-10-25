using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;
public static class NpcPreviewBuilder
{
    public static Node3D BuildLoose(byte[] data,string dependencyRoot)
    {
        using var reader=new GltfDocument();using var state=new GltfState();var result=reader.AppendFromBuffer(data,dependencyRoot,state);if(result!=Error.Ok)throw new InvalidDataException("Godot rejected the loose compatible model: "+result);return reader.GenerateScene(state) as Node3D??throw new InvalidDataException("The loose model produced no 3D scene.");
    }
    static readonly Assembly client = typeof(LibreKO.CharacterPreview).Assembly;
    static JsonObject? index;
    static MethodInfo VisualMethod(string type, string method) => client.GetType("LibreKO.Presentation." + type)!.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!;
    public static Node3D? Build(JsonObject prototype)
    {
        var modelId = FieldValueParser.ReadInt32(prototype["ModelId"]); if (modelId <= 0) return null;
        index ??= Godot.FileAccess.FileExists("res://assets/npcs/index.json") ? ContentMount.Json("assets/npcs/index.json") : new();
        var stem = index[modelId.ToString()]?.ToString(); if (string.IsNullOrEmpty(stem)) return null;
        return BuildFromStem(stem,prototype);
    }
    public static Node3D? BuildFromStem(string stem,JsonObject prototype)
    {
        var modelId=FieldValueParser.ReadInt32(prototype["ModelId"]);
        SourcePaths.ValidateRelative(stem); var path = "res://assets/npcs/" + stem + ".glb";
        if (!ResourceLoader.Exists(path)) return null;
        var scene = ResourceLoader.Load<PackedScene>(path); if (scene == null) return null;
        ulong? animationId=null;var body = new NpcPreviewRoot { Name = "NpcPreview" };body.Released=()=>{if(animationId is {} id)ForgetAnimation(id);};var size = FieldValueParser.ReadInt32(prototype["Size"]); var scale = size > 0 ? size / 100f : 1f;
        try
        {
        var visual = new Node3D { Name = "Model", Scale = Vector3.One * scale }; body.AddChild(visual);
        var instance = scene.Instantiate<Node3D>(); instance.RotationDegrees = new(0, 180, 0); visual.AddChild(instance);
        body.PrimaryAnimation=Find<AnimationPlayer>(instance);
        // Audited visual-only client helpers: no entity system, AI or network context is constructed.
        VisualMethod("ModelNodes", "ForceDoubleSided").Invoke(null, [instance]);
        VisualMethod("ModelNodes", "AttachBuiltInPlugs").Invoke(null, [body, stem]);
        VisualMethod("ModelNodes", "AttachCharacterFxPlugs").Invoke(null, [body, stem]);
        if (body.PrimaryAnimation is { } animation)
        {
            animationId=animation.GetInstanceId();
            VisualMethod("ModelAnimation", "RegisterAnimationMetadata").Invoke(null, [animation, path.GetBaseName() + ".anim.json"]);
            var clip = new[] { "basic", "breath", "base", "stand", "wait", "idle" }.FirstOrDefault(name => animation.HasAnimation(name)) ?? animation.GetAnimationList().FirstOrDefault();
            if (clip != null) { var copy = (Animation)animation.GetAnimation(clip).Duplicate(); copy.LoopMode = Animation.LoopModeEnum.Linear; var libraryName = clip.Contains('/') ? clip[..clip.IndexOf('/')] : ""; var name = clip.Contains('/') ? clip[(clip.IndexOf('/') + 1)..] : clip; var library = (AnimationLibrary)animation.GetAnimationLibrary(libraryName).Duplicate(); library.RemoveAnimation(name); library.AddAnimation(name, copy); animation.RemoveAnimationLibrary(libraryName); animation.AddAnimationLibrary(libraryName, library); animation.Play(clip); animation.Advance(0); }
        }
        AttachWeapons(body, prototype, scale);
        if (prototype["IsMonster"]?.GetValue<bool>() != true && Godot.FileAccess.FileExists("res://assets/npcs/role_fx.json"))
        {
            var role = ContentMount.Json("assets/npcs/role_fx.json")["npcs"]?[prototype["Id"]?.ToString() ?? ""]?.ToString();
            if (!string.IsNullOrEmpty(role)) LibreKO.Fx.Spawn(role, body, new(0, Math.Max(1.8f, LibreKO.Presentation.ModelMetrics.TopY(body) * scale + .3f) + 1.4f - LibreKO.Fx.AuthoredCentreY(role), 0));
        }
        body.SetMeta("model_id", modelId); body.SetMeta("prototype_id", FieldValueParser.ReadInt32(prototype["Id"])); body.SetMeta("is_monster", prototype["IsMonster"]?.GetValue<bool>() ?? false);
        return body;
        }
        catch { body.Free(); throw; }
    }
    static void ForgetAnimation(ulong id)
    {
        var type=client.GetType("LibreKO.Presentation.ModelNodes")!;
        foreach(var name in new[]{"AnimationMetaByIndex","AnimationMetaByName"})
            if(type.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) is System.Collections.IDictionary entries)entries.Remove(id);
    }
    static void AttachWeapons(Node3D body, JsonObject prototype, float scale)
    {
        if(FieldValueParser.ReadInt32(prototype["WeaponType1"])<=0&&FieldValueParser.ReadInt32(prototype["WeaponType2"])<=0)return;
        if (Find<Skeleton3D>(body) is not { } skeleton || FieldValueParser.ReadInt32(prototype["NpcType"]) == LibreKO.Domain.NpcTypes.FixedPose) return;
        var catalog = client.GetType("LibreKO.WeaponCatalog")!; var entries = (System.Collections.IDictionary)catalog.GetProperty("Index", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resolve = catalog.GetMethod("ResolveBaseId", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (field, right) in new[] { ("WeaponType1", true), ("WeaponType2", false) })
        {
            var itemId = FieldValueParser.ReadInt32(prototype[field]); if (itemId <= 0 || !LibreKO.Domain.NpcPlugSlots.Holds(LibreKO.Domain.NpcPlugSlots.For(FieldValueParser.ReadInt32(prototype["ModelId"])), right)) continue;
            var baseId = (int)resolve.Invoke(null, [itemId])!; if (entries[baseId] is not { } info) continue;
            var bone = LibreKO.WeaponMount.Bone(skeleton, right, itemId); if (bone < 0) continue;
            var mesh = PreviewEffects.Weapon(itemId); if (mesh == null) continue;
            var type = info.GetType(); var pose = (LibreKO.PlugPose)type.GetProperty("Pose")!.GetValue(info)!; var version = (int)type.GetField("Version")!.GetValue(info)!;
            var raceBlocks = (LibreKO.PlugPose[])type.GetField("RaceBlocks")!.GetValue(info)!;
            var wearer = new LibreKO.HandWearer(0, true, FieldValueParser.ReadInt32(prototype["Id"]), scale, LibreKO.Domain.NpcPlugSlots.For(FieldValueParser.ReadInt32(prototype["ModelId"])));
            var attachment = new BoneAttachment3D { Name = right ? "PreviewWeaponRight" : "PreviewWeaponLeft" }; skeleton.AddChild(attachment); attachment.BoneIdx = bone;
            mesh.Transform = LibreKO.WeaponMount.HandPlugLocal(pose, version, raceBlocks, right, wearer, LibreKO.WeaponMount.BindHeight(skeleton)); attachment.AddChild(mesh);
            if (skeleton.GetNodeOrNull<Node3D>("plug_" + LibreKO.Domain.NpcPlugSlots.PlugIndex(right)) is { } builtIn) builtIn.Visible = false;
        }
    }
    public static T? Find<T>(Node node) where T : Node
    { if (node is T found) return found; foreach (var child in node.GetChildren()) if (Find<T>(child) is { } result) return result; return null; }
}
