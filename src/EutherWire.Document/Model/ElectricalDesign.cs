namespace EutherWire.Document.Model;

public enum ConductorMaterial { Copper, Aluminium }

public sealed record ElectricalRuleProfile(
    string Id,
    string InstallationStandard,
    string InstallationStandardEdition,
    string CableSizingGuide,
    string CableSizingGuideEdition)
{
    public static ElectricalRuleProfile Sweden2026 { get; } = new(
        "se-ss43640-00-4-2023-sek421-5.1-2026",
        "SS 436 40 00",
        "4:2023",
        "SEK Handbok 421",
        "5.1:2026");
}

public sealed record CircuitDesign
{
    public CircuitDesign(
        double nominalVoltageVolts,
        int phaseCount,
        ConductorMaterial conductorMaterial = ConductorMaterial.Copper,
        int? loadedConductorCount = null,
        double? designCurrentAmperes = null,
        double? protectiveDeviceAmperes = null,
        string? protectiveDeviceCharacteristic = null,
        double? referenceCurrentCarryingCapacityAmperes = null,
        double ambientCorrectionFactor = 1,
        double groupingCorrectionFactor = 1,
        double thermalInsulationCorrectionFactor = 1,
        string? referenceSource = null)
    {
        if (!double.IsFinite(nominalVoltageVolts) || nominalVoltageVolts <= 0) throw new ArgumentOutOfRangeException(nameof(nominalVoltageVolts));
        if (phaseCount is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(phaseCount));
        if (loadedConductorCount is <= 0) throw new ArgumentOutOfRangeException(nameof(loadedConductorCount));
        RequireOptionalPositive(designCurrentAmperes, nameof(designCurrentAmperes));
        RequireOptionalPositive(protectiveDeviceAmperes, nameof(protectiveDeviceAmperes));
        RequireOptionalPositive(referenceCurrentCarryingCapacityAmperes, nameof(referenceCurrentCarryingCapacityAmperes));
        RequirePositive(ambientCorrectionFactor, nameof(ambientCorrectionFactor));
        RequirePositive(groupingCorrectionFactor, nameof(groupingCorrectionFactor));
        RequirePositive(thermalInsulationCorrectionFactor, nameof(thermalInsulationCorrectionFactor));
        NominalVoltageVolts = nominalVoltageVolts;
        PhaseCount = phaseCount;
        ConductorMaterial = conductorMaterial;
        LoadedConductorCount = loadedConductorCount;
        DesignCurrentAmperes = designCurrentAmperes;
        ProtectiveDeviceAmperes = protectiveDeviceAmperes;
        ProtectiveDeviceCharacteristic = protectiveDeviceCharacteristic;
        ReferenceCurrentCarryingCapacityAmperes = referenceCurrentCarryingCapacityAmperes;
        AmbientCorrectionFactor = ambientCorrectionFactor;
        GroupingCorrectionFactor = groupingCorrectionFactor;
        ThermalInsulationCorrectionFactor = thermalInsulationCorrectionFactor;
        ReferenceSource = referenceSource;
    }

    public double NominalVoltageVolts { get; }
    public int PhaseCount { get; }
    public ConductorMaterial ConductorMaterial { get; }
    public int? LoadedConductorCount { get; }
    public double? DesignCurrentAmperes { get; }
    public double? ProtectiveDeviceAmperes { get; }
    public string? ProtectiveDeviceCharacteristic { get; }
    public double? ReferenceCurrentCarryingCapacityAmperes { get; }
    public double AmbientCorrectionFactor { get; }
    public double GroupingCorrectionFactor { get; }
    public double ThermalInsulationCorrectionFactor { get; }
    public string? ReferenceSource { get; }

    public double? CorrectedCurrentCarryingCapacityAmperes => ReferenceCurrentCarryingCapacityAmperes is double reference
        ? reference * AmbientCorrectionFactor * GroupingCorrectionFactor * ThermalInsulationCorrectionFactor
        : null;

    private static void RequireOptionalPositive(double? value, string name)
    {
        if (value is double number) RequirePositive(number, name);
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
