using URead2.Assets.Abstractions;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Crypto;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.TypeMappings;
using URead2.Profiles.Abstractions;
using URead2.Profiles.Engine;
using URead2.Profiles.Games.DuneAwakening;

namespace URead2;

/// <summary>
/// Factory for creating game profiles.
/// </summary>
public static class ProfilesFactory
{
    /// <summary>
    /// Base Unreal Engine profile. Works for most UE4/UE5 games.
    /// </summary>
    public static IProfile UE_Base() => new UE_BaseProfile();

    /// <summary>
    /// Profile for Dune Awakening.
    /// </summary>
    public static IProfile DuneAwakening() => new DuneAwakeningProfile();

    /// <summary>
    /// Initializes Oodle decompression for the profile.
    /// </summary>
    public static IProfile WithOodleDecompression(this IProfile profile, string oodleDllPath)
    {
        profile.Decompressor.InitializeOodle(oodleDllPath);
        return profile;
    }

    /// <summary>
    /// Initializes zlib-ng decompression for the profile.
    /// </summary>
    public static IProfile WithZlibDecompression(this IProfile profile, string zlibDllPath)
    {
        profile.Decompressor.InitializeZlibNg(zlibDllPath);
        return profile;
    }

    /// <summary>
    /// Creates a profile with usmap type mappings loaded from a file.
    /// </summary>
    public static ConfigurableProfile WithTypeMappings(this IProfile profile, string usmapPath)
    {
        var resolver = UsmapTypeResolver.FromFile(usmapPath, profile.Decompressor);
        return new ConfigurableProfile(profile, resolver);
    }

    /// <summary>
    /// Creates a profile with a custom type resolver.
    /// </summary>
    public static ConfigurableProfile WithTypeResolver(this IProfile profile, ITypeResolver resolver)
    {
        return new ConfigurableProfile(profile, resolver);
    }
}

/// <summary>
/// A profile wrapper that allows runtime configuration of components.
/// </summary>
public class ConfigurableProfile : IProfile
{
    private readonly IProfile _inner;
    private ITypeResolver _typeResolver;

    public ConfigurableProfile(IProfile inner, ITypeResolver? typeResolver = null)
    {
        _inner = inner;
        _typeResolver = typeResolver ?? inner.TypeResolver;
    }

    public IContainerReader? PakReader => _inner.PakReader;
    public IContainerReader? IoStoreReader => _inner.IoStoreReader;
    public IAssetEntryReader? PakEntryReader => _inner.PakEntryReader;
    public IAssetEntryReader? IoStoreEntryReader => _inner.IoStoreEntryReader;
    public Decompressor Decompressor => _inner.Decompressor;
    public IDecryptor Decryptor => _inner.Decryptor;
    public IAssetMetadataReader? UAssetReader => _inner.UAssetReader;
    public IAssetMetadataReader? ZenPackageReader => _inner.ZenPackageReader;
    public IExportDataReader? ExportDataReader => _inner.ExportDataReader;
    public IBulkDataReader? BulkDataReader => _inner.BulkDataReader;
    public IPropertyReader PropertyReader => _inner.PropertyReader;
    public IAssetSchemaReader? AssetSchemaReader => _inner.AssetSchemaReader;

    public ITypeResolver TypeResolver => _typeResolver;

    /// <summary>
    /// Adds an additional type resolver to the chain.
    /// </summary>
    public ConfigurableProfile AddTypeResolver(ITypeResolver resolver)
    {
        _typeResolver = new CompositeTypeResolver(_typeResolver, resolver);
        return this;
    }
}
