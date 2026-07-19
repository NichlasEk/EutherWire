using System.Globalization;
using EutherWire.Document.Analysis;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Templates;

try
{
    return Run(args);
}
catch (Exception exception) when (exception is ArgumentException or IOException or ProjectFormatException or FormatException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

static int Run(string[] arguments)
{
    if (arguments.Length < 2)
    {
        Usage();
        return 1;
    }

    string command = arguments[0];
    string projectDirectory = arguments[1];
    switch (command)
    {
        case "create-demo" when arguments.Length == 2:
            ProjectToml.Save(projectDirectory, ProjectTemplates.CreateGarageDraft());
            Console.WriteLine($"Created {Path.Combine(projectDirectory, ProjectToml.FileName)}");
            return 0;
        case "validate" when arguments.Length == 2:
            PrintSummary(ProjectToml.Load(projectDirectory));
            return 0;
        case "report" when arguments.Length == 2:
            return PrintReport(ProjectToml.Load(projectDirectory));
        case "normalize" when arguments.Length == 2:
            ProjectDocument normalized = ProjectToml.Load(projectDirectory);
            ProjectToml.Save(projectDirectory, normalized);
            Console.WriteLine($"Normalized {Path.Combine(projectDirectory, ProjectToml.FileName)}");
            return 0;
        case "handles" when arguments.Length == 2:
            foreach (EditHandle handle in DocumentHandles.Enumerate(ProjectToml.Load(projectDirectory)))
            {
                Console.WriteLine($"{handle.Id}\t{handle.Position.X.ToString("0.###", CultureInfo.InvariantCulture)}\t{handle.Position.Y.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
            return 0;
        case "move" when arguments.Length == 5:
            return Move(projectDirectory, arguments[2], arguments[3], arguments[4]);
        case "insert-vertex" when arguments.Length == 6:
            return InsertVertex(projectDirectory, arguments[2], arguments[3], arguments[4], arguments[5]);
        case "delete-vertex" when arguments.Length == 4:
            return DeleteVertex(projectDirectory, arguments[2], arguments[3]);
        case "configure" when arguments.Length == 4:
            return Configure(projectDirectory, arguments[2], arguments[3]);
        case "install" when arguments.Length is 4 or 5:
            return Install(projectDirectory, arguments[2], arguments[3], arguments.Length == 5 ? arguments[4] : null);
        default:
            Usage();
            return 1;
    }
}

static int Configure(string projectDirectory, string slackText, string serviceLoopText)
{
    double slackPercent = double.Parse(slackText, NumberStyles.Float, CultureInfo.InvariantCulture);
    double serviceLoopMillimetres = double.Parse(serviceLoopText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new SetPlanningSettingsCommand(new PlanningSettings(slackPercent, serviceLoopMillimetres)));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Planning: slack={slackPercent:0.###}% service_loop={serviceLoopMillimetres:0.###} mm");
    return 0;
}

static int Install(string projectDirectory, string cableText, string statusText, string? actualLengthText)
{
    ObjectId cableId = ObjectId.Parse(cableText);
    if (!Enum.TryParse(statusText, true, out InstallationStatus status))
    {
        throw new ArgumentException($"Unknown installation status '{statusText}'.");
    }
    double? actualLength = actualLengthText is null
        ? null
        : double.Parse(actualLengthText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new SetCableInstallationCommand(cableId, status, actualLength));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Installation: {cableId} status={status} actual_length_mm={(actualLength?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown")}");
    return 0;
}

static int InsertVertex(string projectDirectory, string routeText, string indexText, string xText, string yText)
{
    ObjectId routeId = ObjectId.Parse(routeText);
    int index = int.Parse(indexText, CultureInfo.InvariantCulture);
    double x = double.Parse(xText, NumberStyles.Float, CultureInfo.InvariantCulture);
    double y = double.Parse(yText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new InsertRouteVertexCommand(routeId, index, new Point2(x, y)));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Inserted {routeId}:vertex:{index} at {x.ToString("0.###", CultureInfo.InvariantCulture)}, {y.ToString("0.###", CultureInfo.InvariantCulture)} mm");
    return 0;
}

static int DeleteVertex(string projectDirectory, string routeText, string indexText)
{
    ObjectId routeId = ObjectId.Parse(routeText);
    int index = int.Parse(indexText, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new DeleteRouteVertexCommand(routeId, index));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Deleted {routeId}:vertex:{index}");
    return 0;
}

static int Move(string projectDirectory, string handleText, string xText, string yText)
{
    EditHandleId handleId = EditHandleId.Parse(handleText);
    if (!DocumentHandleEditor.CanSetPosition(handleId))
    {
        throw new InvalidOperationException($"Handle '{handleId}' is an anchor and cannot be moved.");
    }
    double x = double.Parse(xText, NumberStyles.Float, CultureInfo.InvariantCulture);
    double y = double.Parse(yText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new MoveEditHandleCommand(handleId, new Point2(x, y)));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Moved {handleId} to {x.ToString("0.###", CultureInfo.InvariantCulture)}, {y.ToString("0.###", CultureInfo.InvariantCulture)} mm");
    return 0;
}

static void PrintSummary(ProjectDocument document)
{
    Console.WriteLine($"OK schema={document.SchemaVersion} name={document.Name}");
    Console.WriteLine($"devices={document.Devices.Count} conduits={document.Conduits.Count} cables={document.Cables.Count} annotations={document.Annotations.Count} handles={DocumentHandles.Enumerate(document).Count}");
}

static int PrintReport(ProjectDocument document)
{
    ProjectAnalysis analysis = ProjectAnalyzer.Analyze(document);
    PrintSummary(document);
    Console.WriteLine($"cable_length_m={analysis.TotalCableLengthMillimetres / 1000:0.###} recommended_m={analysis.RecommendedCableLengthMillimetres / 1000:0.###} conduit_length_m={analysis.TotalConduitLengthMillimetres / 1000:0.###}");
    Console.WriteLine($"planning_slack_percent={document.Planning.CableSlackPercent:0.###} service_loop_mm={document.Planning.ServiceLoopMillimetres:0.###} actual_cable_m={(analysis.ActualCableLengthMillimetres is double actual ? (actual / 1000).ToString("0.###", CultureInfo.InvariantCulture) : "unknown")}");
    Console.WriteLine("Materials:");
    foreach (MaterialItem item in analysis.Materials)
    {
        Console.WriteLine($"  {item.Category}\t{item.Description}\t{item.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {item.Unit}");
    }
    Console.WriteLine("Conduit fill (planning estimate):");
    foreach (ConduitFill fill in analysis.ConduitFills)
    {
        Console.WriteLine($"  {fill.ConduitId}\t{fill.FillRatio.ToString("P1", CultureInfo.InvariantCulture)}\tknown={fill.KnownCableCount} unknown={fill.UnknownCableCount}");
    }
    Console.WriteLine($"Diagnostics: errors={analysis.ErrorCount} warnings={analysis.WarningCount}");
    foreach (ProjectDiagnostic diagnostic in analysis.Diagnostics)
    {
        string objectText = diagnostic.ObjectId is ObjectId id ? $" [{id}]" : string.Empty;
        Console.WriteLine($"  {diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}{objectText}: {diagnostic.Message}");
    }
    return analysis.ErrorCount > 0 ? 3 : 0;
}

static void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  eutherwire create-demo <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire validate <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire report <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire normalize <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire handles <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire move <project.eutherwire> <handle-id> <x-mm> <y-mm>");
    Console.Error.WriteLine("  eutherwire insert-vertex <project.eutherwire> <route-id> <index> <x-mm> <y-mm>");
    Console.Error.WriteLine("  eutherwire delete-vertex <project.eutherwire> <route-id> <index>");
    Console.Error.WriteLine("  eutherwire configure <project.eutherwire> <slack-percent> <service-loop-mm>");
    Console.Error.WriteLine("  eutherwire install <project.eutherwire> <cable-id> <planned|installed|tested|changed|blocked> [actual-length-mm]");
}
