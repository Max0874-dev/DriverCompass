using DriverCompass.Core.Parsing;

namespace DriverCompass.Data;

/// <summary>Access to the files compiled into this assembly (offline fallback).</summary>
public static class EmbeddedData
{
    public const string VendorsResource = "DriverCompass.Data.vendors.json";
    public const string SchemaResource = "DriverCompass.Data.vendors.schema.json";
    public const string PciIdsResource = "DriverCompass.Data.pci.ids";

    public static Stream? Open(string resource) => typeof(EmbeddedData).Assembly.GetManifestResourceStream(resource);

    public static string ReadText(string resource)
    {
        using var stream = Open(resource) ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string VendorsJson => ReadText(VendorsResource);

    public static string SchemaJson => ReadText(SchemaResource);

    /// <summary>pci.ids name database (lazy, parsed on first lookup).</summary>
    public static PciIdsDatabase CreatePciIdsDatabase() => new(() => Open(PciIdsResource));
}
