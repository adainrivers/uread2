using URead2.Containers.Abstractions;
using URead2.Profiles.Engine;

namespace URead2.Profiles.Games.DuneAwakening;

/// <summary>
/// Profile for Dune Awakening.
/// Uses custom pak header format with correct IndexOffset/IndexSize at -261.
/// </summary>
public class DuneAwakeningProfile : UE_BaseProfile
{
    protected internal DuneAwakeningProfile() { }
    public override IContainerReader? PakReader { get; } = new DunePakReader();
}
