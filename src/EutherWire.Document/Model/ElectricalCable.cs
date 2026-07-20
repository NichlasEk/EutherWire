namespace EutherWire.Document.Model;

public enum CableProductKind { Custom, Cat6, Cat6A, Ekrk, Rk, Fk, Fibre, Coax }
public enum CircuitPreset { Custom, Data, SinglePhase, ThreePhase, ThreePhaseNeutral, Lighting }
public enum ConductorFunction { Line1, Line2, Line3, Neutral, ProtectiveEarth, SwitchedLive, Control, DcPositive, DcNegative, DataPair, Spare }

public sealed record ConductorSpec
{
    public ConductorSpec(string id, ConductorFunction function, string colour, double areaSquareMillimetres, double? outsideDiameterMillimetres = null, string? terminalLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(colour);
        if (!double.IsFinite(areaSquareMillimetres) || areaSquareMillimetres <= 0) throw new ArgumentOutOfRangeException(nameof(areaSquareMillimetres));
        if (outsideDiameterMillimetres is double diameter && (!double.IsFinite(diameter) || diameter <= 0)) throw new ArgumentOutOfRangeException(nameof(outsideDiameterMillimetres));
        Id = id; Function = function; Colour = colour; AreaSquareMillimetres = areaSquareMillimetres;
        OutsideDiameterMillimetres = outsideDiameterMillimetres; TerminalLabel = terminalLabel;
    }
    public string Id { get; }
    public ConductorFunction Function { get; }
    public string Colour { get; }
    public double AreaSquareMillimetres { get; }
    public double? OutsideDiameterMillimetres { get; }
    public string? TerminalLabel { get; }
}

public sealed record ElectricalCableSpec
{
    public ElectricalCableSpec(CableProductKind product, CircuitPreset preset, IEnumerable<ConductorSpec> conductors, string shielding = "none", bool poeCapable = false, double? outsideDiameterMillimetres = null, CircuitDesign? design = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shielding);
        List<ConductorSpec> list = conductors.ToList();
        if (list.Count == 0) throw new ArgumentException("A cable needs at least one conductor or data pair.", nameof(conductors));
        if (list.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != list.Count) throw new ArgumentException("Conductor IDs must be unique.", nameof(conductors));
        if (outsideDiameterMillimetres is double diameter && (!double.IsFinite(diameter) || diameter <= 0)) throw new ArgumentOutOfRangeException(nameof(outsideDiameterMillimetres));
        Product = product; Preset = preset; Conductors = list; Shielding = shielding; PoeCapable = poeCapable;
        OutsideDiameterMillimetres = outsideDiameterMillimetres; Design = design;
    }
    public CableProductKind Product { get; }
    public CircuitPreset Preset { get; }
    public IReadOnlyList<ConductorSpec> Conductors { get; }
    public string Shielding { get; }
    public bool PoeCapable { get; }
    public double? OutsideDiameterMillimetres { get; }
    public CircuitDesign? Design { get; }
}

public static class ElectricalCableProfiles
{
    public static ElectricalCableSpec Infer(CableKind kind) => kind switch
    {
        CableKind.Cat6 => Data(CableProductKind.Cat6, false),
        CableKind.Cat6A => Data(CableProductKind.Cat6A, false),
        CableKind.Mains3G25 => SinglePhase(CableProductKind.Ekrk, 2.5),
        CableKind.Mains5G6 => ThreePhaseNeutral(CableProductKind.Ekrk, 6),
        CableKind.FibreDuplex => new(CableProductKind.Fibre, CircuitPreset.Data, [new("duplex", ConductorFunction.DataPair, "fibre", 0.2)]),
        CableKind.Coax => new(CableProductKind.Coax, CircuitPreset.Data, [new("core", ConductorFunction.DataPair, "copper", 0.5)]),
        CableKind.LowVoltageDc => new(CableProductKind.Custom, CircuitPreset.Custom, [new("positive", ConductorFunction.DcPositive, "red", 0.75), new("negative", ConductorFunction.DcNegative, "black", 0.75)]),
        _ => new(CableProductKind.Custom, CircuitPreset.Custom, [new("unknown", ConductorFunction.Spare, "unknown", 1)]),
    };

    public static ElectricalCableSpec SinglePhase(CableProductKind product, double area) => new(product, CircuitPreset.SinglePhase,
        [new("l1", ConductorFunction.Line1, "brown", area), new("n", ConductorFunction.Neutral, "blue", area), new("pe", ConductorFunction.ProtectiveEarth, "green_yellow", area)]);

    public static ElectricalCableSpec ThreePhaseNeutral(CableProductKind product, double area) => new(product, CircuitPreset.ThreePhaseNeutral,
        [new("l1", ConductorFunction.Line1, "brown", area), new("l2", ConductorFunction.Line2, "black", area), new("l3", ConductorFunction.Line3, "grey", area), new("n", ConductorFunction.Neutral, "blue", area), new("pe", ConductorFunction.ProtectiveEarth, "green_yellow", area)]);

    public static ElectricalCableSpec Lighting(CableProductKind product, double area, int switchedLives = 1) => new(product, CircuitPreset.Lighting,
        SinglePhase(product, area).Conductors.Concat(Enumerable.Range(1, switchedLives).Select(index => new ConductorSpec($"t{index}", ConductorFunction.SwitchedLive, index == 1 ? "black" : "orange", area, terminalLabel: $"T{index}"))));

    private static ElectricalCableSpec Data(CableProductKind product, bool poe) => new(product, CircuitPreset.Data,
        [new("pair1", ConductorFunction.DataPair, "white_blue", 0.2), new("pair2", ConductorFunction.DataPair, "white_orange", 0.2), new("pair3", ConductorFunction.DataPair, "white_green", 0.2), new("pair4", ConductorFunction.DataPair, "white_brown", 0.2)], "u_utp", poe);
}
