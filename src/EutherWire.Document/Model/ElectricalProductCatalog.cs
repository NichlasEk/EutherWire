using System.Text.Json.Serialization;
using Tomlyn;

namespace EutherWire.Document.Model;

public sealed record ConduitProduct(
    string Id,
    string Manufacturer,
    string Name,
    string ENumber,
    double NominalDiameterMillimetres,
    double InnerDiameterMillimetres,
    string SourceUrl);

public sealed class ElectricalProductCatalog
{
    private ElectricalProductCatalog(IReadOnlyList<ConduitProduct> conduits) => Conduits = conduits;

    public IReadOnlyList<ConduitProduct> Conduits { get; }

    public ConduitProduct? FindConduit(string id) =>
        Conduits.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public ConduitProduct RequireConduit(string id) =>
        FindConduit(id)
        ?? throw new KeyNotFoundException($"Conduit product '{id}' does not exist in the electrical product catalog.");

    public static ElectricalProductCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        CatalogFile file = TomlSerializer.Deserialize<CatalogFile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The electrical product catalog is empty.");
        if (file.SchemaVersion != 1) throw new InvalidDataException($"Unsupported product catalog schema {file.SchemaVersion}; expected 1.");
        var products = file.Conduits.Select(source =>
        {
            string id = Required(source.Id, "conduit.id");
            double nominal = Positive(source.NominalDiameterMillimetres, $"conduits[{id}].nominal_diameter_mm");
            double inner = Positive(source.InnerDiameterMillimetres, $"conduits[{id}].inner_diameter_mm");
            if (inner >= nominal) throw new InvalidDataException($"Conduit product '{id}' must have an inner diameter smaller than its nominal diameter.");
            string sourceUrl = Required(source.SourceUrl, $"conduits[{id}].source_url");
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Conduit product '{id}' needs an absolute HTTPS source URL.");
            return new ConduitProduct(id, Required(source.Manufacturer, $"conduits[{id}].manufacturer"),
                Required(source.Name, $"conduits[{id}].name"), Required(source.ENumber, $"conduits[{id}].e_number"),
                nominal, inner, sourceUrl);
        }).ToList();
        if (products.Count == 0) throw new InvalidDataException("The electrical product catalog contains no conduits.");
        if (products.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != products.Count)
            throw new InvalidDataException("Conduit product IDs must be unique.");
        return new ElectricalProductCatalog(products);
    }

    public static ElectricalProductCatalog LoadBundled()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "catalog", "electrical-products.toml"),
            Path.Combine(Directory.GetCurrentDirectory(), "catalog", "electrical-products.toml"),
        ];
        string path = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not find catalog/electrical-products.toml.");
        return Load(path);
    }

    private static string Required(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new InvalidDataException($"Missing or empty {field}.");

    private static double Positive(double value, string field) =>
        double.IsFinite(value) && value > 0 ? value : throw new InvalidDataException($"{field} must be a positive finite number.");

    private sealed class CatalogFile
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("conduits")] public List<ConduitFile> Conduits { get; set; } = [];
    }

    private sealed class ConduitFile
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("manufacturer")] public string? Manufacturer { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("e_number")] public string? ENumber { get; set; }
        [JsonPropertyName("nominal_diameter_mm")] public double NominalDiameterMillimetres { get; set; }
        [JsonPropertyName("inner_diameter_mm")] public double InnerDiameterMillimetres { get; set; }
        [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
    }
}
