using Content.Shared._Sunrise.Misc;
using Content.Shared.Xenoarchaeology.Artifact.XAE.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Xenoarchaeology.Artifact.XAE;

/// <summary>
/// System for xeno artifact effect that make artifact pass through other objects.
/// </summary>
public sealed class XAERemoveCollisionSystem : BaseXAESystem<XAERemoveCollisionComponent>
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    /// <inheritdoc />
    protected override void OnActivated(Entity<XAERemoveCollisionComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        // Sunrise-start чтобы игроки не могли сделать себе тело, которое проходит сквозь стенки
        if (HasComp<XenoArtifactThrowingAutoInjectorMarkComponent>(args.Artifact.Owner))
            return;
        // Sunrise-end

        if (!TryComp<FixturesComponent>(args.Artifact, out var fixtures))
            return;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            _physics.SetHard(args.Artifact, fixture, false, fixtures);
        }
    }
}
