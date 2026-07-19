namespace EutherWire.Document.Model;

public sealed record PlanningSettings(double CableSlackPercent = 10, double ServiceLoopMillimetres = 1000)
{
    public PlanningSettings Validate()
    {
        if (!double.IsFinite(CableSlackPercent) || CableSlackPercent < 0 || CableSlackPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(CableSlackPercent), "Cable slack must be between 0 and 100 percent.");
        }
        if (!double.IsFinite(ServiceLoopMillimetres) || ServiceLoopMillimetres < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ServiceLoopMillimetres), "Service loop must be a non-negative length.");
        }
        return this;
    }
}
