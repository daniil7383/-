using Content.Server._Sunrise.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._Sunrise.Storage.Components;


[RegisterComponent, Access(typeof(StorageManifestLabelSystem))]
public sealed partial class StorageManifestLabelComponent : Component
{
    [DataField]
    public EntProtoId PaperPrototype = "Paper";
}
