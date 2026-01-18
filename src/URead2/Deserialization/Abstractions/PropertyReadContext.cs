using URead2.Assets.Models;
using URead2.Deserialization.Properties;
using URead2.TypeResolution;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Context for property reading operations.
/// Provides name table, type resolution, object reference resolution, and error tracking.
/// </summary>
public class PropertyReadContext
{
    /// <summary>
    /// Name table from the asset.
    /// </summary>
    public required string[] NameTable { get; init; }

    #region Error Tracking

    /// <summary>
    /// Fatal error code if a fatal error occurred, null otherwise.
    /// </summary>
    public ReadErrorCode? FatalError { get; private set; }

    /// <summary>
    /// Stream position where fatal error occurred.
    /// </summary>
    public long FatalPosition { get; private set; }

    /// <summary>
    /// Detail message for fatal error.
    /// </summary>
    public string? FatalDetail { get; private set; }

    /// <summary>
    /// List of non-fatal diagnostics (warnings/recoveries).
    /// Lazily allocated on first warning.
    /// </summary>
    public List<ReadDiagnostic>? Diagnostics { get; private set; }

    /// <summary>
    /// True if a fatal error has occurred.
    /// </summary>
    public bool HasFatalError => FatalError.HasValue;

    /// <summary>
    /// Records a fatal error. Only the first fatal error is recorded.
    /// </summary>
    public void Fatal(ReadErrorCode code, long position, string? detail = null)
    {
        if (!HasFatalError)
        {
            FatalError = code;
            FatalPosition = position;
            FatalDetail = detail;
        }
    }

    /// <summary>
    /// Records a non-fatal diagnostic (warning or recovery).
    /// </summary>
    public void Warn(DiagnosticCode code, long position, string? detail = null)
    {
        Diagnostics ??= [];
        Diagnostics.Add(new ReadDiagnostic(code, position, detail));
    }

    #endregion

    /// <summary>
    /// Type registry for type and schema lookup.
    /// </summary>
    public required TypeRegistry TypeRegistry { get; init; }

    /// <summary>
    /// Property reader for deserializing nested structures.
    /// </summary>
    public required IPropertyReader PropertyReader { get; init; }

    /// <summary>
    /// Import table for resolving object references to external objects.
    /// Imports are pre-resolved during metadata loading.
    /// </summary>
    public AssetImport[]? Imports { get; init; }

    /// <summary>
    /// Export table for resolving object references to local objects.
    /// </summary>
    public AssetExport[]? Exports { get; init; }

    /// <summary>
    /// The current package path (for building full object paths).
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// Whether properties use unversioned serialization format.
    /// </summary>
    public bool IsUnversioned { get; init; }

    /// <summary>
    /// Resolves a package index to an ObjectReference.
    /// </summary>
    public ObjectReference ResolveReference(int packageIndex)
    {
        if (packageIndex == 0)
            return new ObjectReference { Index = 0 };

        if (packageIndex < 0)
        {
            // Import reference - imports are pre-resolved during metadata loading
            int importIndex = -packageIndex - 1;
            if (Imports != null && importIndex >= 0 && importIndex < Imports.Length)
            {
                var import = Imports[importIndex];

                return new ObjectReference
                {
                    Type = import.ClassName,
                    Name = import.Name,
                    Path = import.PackageName,
                    Index = packageIndex,
                    IsFullyResolved = import.IsResolved
                };
            }
        }
        else
        {
            // Export reference (local to this package)
            int exportIndex = packageIndex - 1;
            if (Exports != null && exportIndex >= 0 && exportIndex < Exports.Length)
            {
                var export = Exports[exportIndex];
                return new ObjectReference
                {
                    Type = export.ClassName,
                    Name = export.Name,
                    Path = PackagePath,
                    Index = packageIndex,
                    ResolvedExport = export,
                    IsFullyResolved = true
                };
            }
        }

        // Couldn't resolve, return with just the index
        return new ObjectReference { Index = packageIndex };
    }

    /// <summary>
    /// Gets an import by index (0-based).
    /// </summary>
    public AssetImport? GetImport(int importIndex)
    {
        if (Imports == null || importIndex < 0 || importIndex >= Imports.Length)
            return null;
        return Imports[importIndex];
    }

    /// <summary>
    /// Gets an export by index (0-based).
    /// </summary>
    public AssetExport? GetExport(int exportIndex)
    {
        if (Exports == null || exportIndex < 0 || exportIndex >= Exports.Length)
            return null;
        return Exports[exportIndex];
    }
}
