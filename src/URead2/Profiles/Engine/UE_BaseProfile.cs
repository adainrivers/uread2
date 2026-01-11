using URead2.Assets;
using URead2.Assets.Abstractions;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Containers.IoStore;
using URead2.Containers.Pak;
using URead2.Crypto;
using URead2.Deserialization;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Properties;
using URead2.Profiles.Abstractions;

namespace URead2.Profiles.Engine;

public class UE_BaseProfile : IProfile
{
    protected internal UE_BaseProfile() { }
    public virtual IContainerReader? PakReader { get; } = new PakReader();
    public virtual IContainerReader? IoStoreReader { get; } = new IoStoreReader();
    public virtual Decompressor Decompressor { get; } = new();
    public virtual IDecryptor Decryptor { get; } = new AesDecryptor();

    public virtual IAssetEntryReader? PakEntryReader =>
        new PakEntryReader(Decompressor, Decryptor);

    public virtual IAssetEntryReader? IoStoreEntryReader =>
        new IoStoreEntryReader(Decompressor, Decryptor);

    public virtual IAssetMetadataReader? UAssetReader { get; } = new UAssetMetadataReader();
    public virtual IAssetMetadataReader? ZenPackageReader { get; } = new ZenPackageMetadataReader();
    public virtual IExportDataReader? ExportDataReader { get; } = new ExportDataReader();
    public virtual IBulkDataReader? BulkDataReader { get; } = new BulkDataReader();

    public virtual IPropertyReader PropertyReader { get; } = new PropertyReader();
    public virtual IAssetSchemaReader? AssetSchemaReader { get; } = new AssetSchemaReader();
}
