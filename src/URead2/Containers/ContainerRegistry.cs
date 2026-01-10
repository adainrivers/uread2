using URead2.Assets.Abstractions;
using URead2.Containers.Abstractions;
using URead2.Profiles.Abstractions;

namespace URead2.Containers;

/// <summary>
/// Registry that manages containers and provides access to their entries.
/// Thread-safe container discovery and access.
/// </summary>
public class ContainerRegistry
{
    private record MountedContainer(string Path, IContainerReader Reader);

    private readonly RuntimeConfig _config;
    private readonly IProfile _profile;
    private readonly List<MountedContainer> _containers = [];
    private readonly object _scanLock = new();
    private volatile bool _scanned;

    public ContainerRegistry(RuntimeConfig config, IProfile profile)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>
    /// Gets all entries from all containers. Re-reads container indexes on each call.
    /// For repeated access, cache the results with .ToList() to avoid re-parsing.
    /// </summary>
    public IEnumerable<IAssetEntry> GetEntries(Func<string, bool>? containerPathFilter = null)
    {
        EnsureScanned();

        var containers = containerPathFilter != null
            ? _containers.Where(c => containerPathFilter(c.Path))
            : _containers;

        return containers.SelectMany(c => c.Reader.ReadEntries(c.Path, _profile, _config.AesKey));
    }

    private void EnsureScanned()
    {
        if (_scanned)
            return;

        lock (_scanLock)
        {
            if (_scanned)
                return;

            foreach (var file in Directory.EnumerateFiles(_config.PaksPath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && _profile.PakReader != null)
                    _containers.Add(new MountedContainer(file, _profile.PakReader));

                if (file.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase) && _profile.IoStoreReader != null)
                    _containers.Add(new MountedContainer(file, _profile.IoStoreReader));
            }

            _scanned = true;
        }
    }
}
