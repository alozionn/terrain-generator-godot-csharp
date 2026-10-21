#pragma warning disable CS0109
using Godot;

namespace TokisanGames;

/// <summary>
/// Missing from Terrain3D v1.1.0-rc1's generated C# bindings: the base wrapper that Terrain3DTextureAsset and
/// Terrain3DMeshAsset derive from. The native class exists; the subclasses declare every member they use.
/// Delete this file once a Terrain3D release ships it (the build will fail with a duplicate type).
/// </summary>
[Tool]
public partial class Terrain3DAssetResource : Resource
{
    protected Terrain3DAssetResource() { }

    public new class GDExtensionSignalName : Resource.SignalName { }
    public new class GDExtensionPropertyName : Resource.PropertyName { }
    public new class GDExtensionMethodName : Resource.MethodName { }
}
