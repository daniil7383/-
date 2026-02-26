using System.Diagnostics.CodeAnalysis;
using System.Text;
using Content.Server._Sunrise.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Labels.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._Sunrise.Storage.EntitySystems;

/// <summary>
/// Автоматически создает ведомость для хранилища и обновляет ее при изменении содержимого
/// </summary>
public sealed class StorageManifestLabelSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly LabelSystem _label = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private readonly Dictionary<string, int> _itemCounts = new();
    private readonly List<string> _sortedNames = new();
    private readonly StringBuilder _textBuilder = new(256);
    private readonly Queue<EntityUid> _updateQueue = new();
    private readonly HashSet<EntityUid> _queuedUpdates = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StorageManifestLabelComponent, MapInitEvent>(OnMapInit, after: [typeof(StorageSystem), typeof(ContainerFillSystem)]);
        SubscribeLocalEvent<StorageManifestLabelComponent, EntInsertedIntoContainerMessage>(OnContainerInserted);
        SubscribeLocalEvent<StorageManifestLabelComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_updateQueue.TryDequeue(out var uid))
        {
            _queuedUpdates.Remove(uid);

            if (TerminatingOrDeleted(uid))
                continue;

            if (!TryComp<StorageManifestLabelComponent>(uid, out var manifest))
                continue;

            Entity<StorageManifestLabelComponent> storage = (uid, manifest);

            if (!TryComp<PaperLabelComponent>(storage, out var labelComp))
                continue;

            Entity<PaperLabelComponent> label = (storage.Owner, labelComp);

            if (!_label.TryGetLabel(label.AsNullable(), out Entity<PaperComponent>? paper))
                continue;

            UpdateManifest(paper.Value, storage);
        }
    }

    private void OnMapInit(Entity<StorageManifestLabelComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<PaperLabelComponent>(ent, out var labelComp))
            return;

        Entity<PaperLabelComponent> label = (ent.Owner, labelComp);

        if (!TryGetOrCreateManifestPaper(ent, label, out var paper))
            return;

        UpdateManifest(paper.Value, ent, updateName: true);
    }

    private void OnContainerInserted(Entity<StorageManifestLabelComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        OnContainerModified(ent, args);
    }

    private void OnContainerRemoved(Entity<StorageManifestLabelComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        OnContainerModified(ent, args);
    }

    private void OnContainerModified(Entity<StorageManifestLabelComponent> ent, ContainerModifiedMessage args)
    {
        if (!TryGetContentsContainer(ent, out var contentsContainer))
            return;

        if (!ReferenceEquals(args.Container, contentsContainer))
            return;

        if (_queuedUpdates.Add(ent.Owner))
            _updateQueue.Enqueue(ent.Owner);
    }

    private bool TryGetOrCreateManifestPaper(
        Entity<StorageManifestLabelComponent> ent,
        Entity<PaperLabelComponent> label,
        [NotNullWhen(true)]
        out Entity<PaperComponent>? paper)
    {

        if (_label.TryGetLabel(label.AsNullable(), out paper))
            return true;

        if (label.Comp.LabelSlot.Item != null)
            return false;

        var spawned = Spawn(ent.Comp.PaperPrototype, Transform(ent).Coordinates);
        if (!TryComp<PaperComponent>(spawned, out var paperComp))
        {
            Del(spawned);
            return false;
        }

        if (!_itemSlots.TryInsert(ent.Owner, label.Comp.LabelSlot, spawned, null))
        {
            Del(spawned);
            return false;
        }

        paper = (spawned, paperComp);
        return true;
    }

    private bool TryGetContentsContainer(Entity<StorageManifestLabelComponent> ent, [NotNullWhen(true)] out BaseContainer? container)
    {
        if (TryComp<EntityStorageComponent>(ent, out var entityStorage))
        {
            container = entityStorage.Contents;
            return true;
        }

        if (TryComp<StorageComponent>(ent, out var storage))
        {
            container = storage.Container;
            return true;
        }

        container = null;
        return false;
    }

    private void UpdateManifest(Entity<PaperComponent> ent, Entity<StorageManifestLabelComponent> storage, bool updateName = false)
    {
        _itemCounts.Clear();
        var totalItems = CollectContents(storage, _itemCounts);

        if (updateName)
            _metaData.SetEntityName(ent.Owner, _loc.GetString("storage-manifest-paper-name"));

        var text = BuildManifestText(totalItems, _itemCounts);
        _paper.SetContent(ent, text);
    }

    private int CollectContents(Entity<StorageManifestLabelComponent> ent, Dictionary<string, int> itemCounts)
    {
        if (!TryGetContentsContainer(ent, out var contentsContainer))
            return 0;

        var totalItems = 0;

        foreach (var item in contentsContainer.ContainedEntities)
        {
            AddItemCount(item, itemCounts);
            totalItems++;
        }

        return totalItems;
    }

    // maybe you can do it better
    private string BuildManifestText(int totalItems, Dictionary<string, int> itemCounts)
    {
        _textBuilder.Clear();
        _textBuilder.Append(_loc.GetString("storage-manifest-title"));
        _textBuilder.Append('\n');
        _textBuilder.Append(_loc.GetString("storage-manifest-total", ("count", totalItems)));

        if (itemCounts.Count == 0)
        {
            _textBuilder.Append('\n');
            _textBuilder.Append(_loc.GetString("storage-manifest-empty"));
            return _textBuilder.ToString();
        }

        _sortedNames.Clear();
        _sortedNames.AddRange(itemCounts.Keys);
        _sortedNames.Sort(StringComparer.Ordinal);

        foreach (var name in _sortedNames)
        {
            _textBuilder.Append('\n');
            _textBuilder.Append(_loc.GetString("storage-manifest-entry", ("name", name), ("count", itemCounts[name])));
        }

        return _textBuilder.ToString();
    }

    private void AddItemCount(EntityUid item, Dictionary<string, int> itemCounts)
    {
        var itemName = FormattedMessage.EscapeText(MetaData(item).EntityName);

        if (itemCounts.TryGetValue(itemName, out var current))
        {
            itemCounts[itemName] = current + 1;
            return;
        }

        itemCounts[itemName] = 1;
    }
}
