using URead2.Assets.Abstractions;
using URead2.Containers.Abstractions;
using URead2.Profiles.Abstractions;

namespace URead2.Containers;

/// <summary>
/// Registry that manages containers and provides access to their entries.
/// Mounts all containers at startup with memory-mapped files for efficient access.
/// Thread-safe for concurrent reads after mounting.
/// </summary>
public class ContainerRegistry : IDisposable
{
    private readonly RuntimeConfig _config;
    private readonly IProfile _profile;

    private readonly object _mountLock = new();
    private volatile bool _mounted;
    private bool _disposed;

    // Mounted containers by data path (.ucas or .pak)
    private readonly Dictionary<string, MountedContainer> _mountedContainers = new(StringComparer.OrdinalIgnoreCase);

    // All entries cached
    private List<IAssetEntry>? _allEntries;

    // Entries indexed by path for fast lookup
    private Dictionary<string, IAssetEntry>? _entriesByPath;

    public ContainerRegistry(RuntimeConfig config, IProfile profile)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>
    /// Mounts all containers, reading their indexes and creating memory-mapped files.
    /// Call this once at startup. Thread-safe but only mounts once.
    /// </summary>
    public void Mount()
    {
        if (_mounted)
            return;

        lock (_mountLock)
        {
            if (_mounted)
                return;

            var allEntries = new List<IAssetEntry>();
            var entriesByPath = new Dictionary<string, IAssetEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(_config.PaksPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (file.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && _profile.PakReader != null)
                    {
                        var entries = _profile.PakReader.ReadEntries(file, _profile, _config.AesKey).ToList();
                        if (entries.Count > 0)
                        {
                            // For pak files, the data path is the pak file itself
                            var mounted = new MountedContainer(file, entries);
                            _mountedContainers[file] = mounted;

                            foreach (var entry in entries)
                            {
                                allEntries.Add(entry);
                                entriesByPath.TryAdd(entry.Path, entry);
                            }
                        }
                    }
                    else if (file.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase) && _profile.IoStoreReader != null)
                    {
                        var entries = _profile.IoStoreReader.ReadEntries(file, _profile, _config.AesKey).ToList();
                        if (entries.Count > 0)
                        {
                            // For IoStore, the data path is the .ucas file
                            var ucasPath = Path.ChangeExtension(file, ".ucas");
                            if (File.Exists(ucasPath))
                            {
                                var mounted = new MountedContainer(ucasPath, entries);
                                _mountedContainers[ucasPath] = mounted;

                                foreach (var entry in entries)
                                {
                                    allEntries.Add(entry);
                                    entriesByPath.TryAdd(entry.Path, entry);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue mounting other containers
                    Serilog.Log.Warning(ex, "Failed to mount container: {Path}", file);
                }
            }

            _allEntries = allEntries;
            _entriesByPath = entriesByPath;
            _mounted = true;
        }
    }

    /// <summary>
    /// Gets all entries from all mounted containers.
    /// </summary>
    public IReadOnlyList<IAssetEntry> GetEntries()
    {
        EnsureMounted();
        return _allEntries!;
    }

    /// <summary>
    /// Gets all entries, optionally filtered by container path.
    /// </summary>
    public IEnumerable<IAssetEntry> GetEntries(Func<string, bool>? containerPathFilter)
    {
        EnsureMounted();

        if (containerPathFilter == null)
            return _allEntries!;

        return _allEntries!.Where(e => containerPathFilter(e.ContainerPath));
    }

    /// <summary>
    /// Gets a specific entry by its path. O(1) lookup.
    /// </summary>
    public IAssetEntry? GetEntry(string path)
    {
        EnsureMounted();
        return _entriesByPath!.TryGetValue(path, out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets the mounted container for a given data path (.ucas or .pak).
    /// </summary>
    public MountedContainer? GetMountedContainer(string dataPath)
    {
        EnsureMounted();
        return _mountedContainers.TryGetValue(dataPath, out var container) ? container : null;
    }

    /// <summary>
    /// Gets the total number of mounted containers.
    /// </summary>
    public int ContainerCount
    {
        get
        {
            EnsureMounted();
            return _mountedContainers.Count;
        }
    }

    /// <summary>
    /// Gets the total number of cached entries.
    /// </summary>
    public int EntryCount
    {
        get
        {
            EnsureMounted();
            return _allEntries?.Count ?? 0;
        }
    }

    private void EnsureMounted()
    {
        if (!_mounted)
            Mount();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var container in _mountedContainers.Values)
            {
                container.Dispose();
            }
            _mountedContainers.Clear();
            _allEntries = null;
            _entriesByPath = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
