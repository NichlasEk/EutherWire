using System.Globalization;
using EutherWire.Document.Analysis;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Export;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Synchronization;
using EutherWire.Document.Templates;
using EutherWire.Export;

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
        case "tasks" when arguments.Length == 2:
            return PrintTasks(ProjectToml.Load(projectDirectory));
        case "normalize" when arguments.Length == 2:
            ProjectDocument normalized = ProjectToml.Load(projectDirectory);
            ProjectToml.Save(projectDirectory, normalized);
            Console.WriteLine($"Normalized {Path.Combine(projectDirectory, ProjectToml.FileName)}");
            return 0;
        case "handles" when arguments.Length == 2:
            ProjectDocument handleDocument = ProjectToml.Load(projectDirectory);
            foreach (EditHandle handle in DocumentHandles.Enumerate(handleDocument))
            {
                Point3 position = DocumentHandleEditor.RequireSpatialPosition(handleDocument, handle.Id);
                Console.WriteLine($"{handle.Id}\t{position.X.ToString("0.###", CultureInfo.InvariantCulture)}\t{position.Y.ToString("0.###", CultureInfo.InvariantCulture)}\t{position.Z.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
            return 0;
        case "properties" when arguments.Length == 2:
            foreach (DocumentProperty property in DocumentProperties.Enumerate(ProjectToml.Load(projectDirectory)))
            {
                string choices = property.Choices is { Count: > 0 } ? $"\t{string.Join('|', property.Choices)}" : string.Empty;
                Console.WriteLine($"{property.Id}\t{property.Kind.ToString().ToLowerInvariant()}\t{property.Value}{choices}");
            }
            return 0;
        case "set-property" when arguments.Length == 4:
            return SetProperty(projectDirectory, arguments[2], arguments[3]);
        case "export-svg" when arguments.Length == 3:
            SvgProjectExporter.Save(arguments[2], ProjectToml.Load(projectDirectory));
            Console.WriteLine($"Exported {Path.GetFullPath(arguments[2])}");
            return 0;
        case "export-png" when arguments.Length == 3:
            PngProjectExporter.Save(arguments[2], ProjectToml.Load(projectDirectory));
            Console.WriteLine($"Exported {Path.GetFullPath(arguments[2])}");
            return 0;
        case "move" when arguments.Length == 5:
            return Move(projectDirectory, arguments[2], arguments[3], arguments[4]);
        case "move3d" when arguments.Length == 6:
            return Move3D(projectDirectory, arguments[2], arguments[3], arguments[4], arguments[5]);
        case "insert-vertex" when arguments.Length == 6:
            return InsertVertex(projectDirectory, arguments[2], arguments[3], arguments[4], arguments[5]);
        case "delete-vertex" when arguments.Length == 4:
            return DeleteVertex(projectDirectory, arguments[2], arguments[3]);
        case "configure" when arguments.Length == 4:
            return Configure(projectDirectory, arguments[2], arguments[3]);
        case "install" when arguments.Length is 4 or 5:
            return Install(projectDirectory, arguments[2], arguments[3], arguments.Length == 5 ? arguments[4] : null);
        case "journal-create" when arguments.Length is 6 or 7:
            return JournalCreate(projectDirectory, arguments[2], arguments[3], arguments[4], arguments[5], arguments.Length == 7 ? arguments[6] : null);
        case "journal-apply" when arguments.Length == 3:
            return JournalApply(projectDirectory, arguments[2]);
        default:
            Usage();
            return 1;
    }
}

static int SetProperty(string projectDirectory, string propertyText, string value)
{
    PropertyHandleId propertyId = PropertyHandleId.Parse(propertyText);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, DocumentProperties.CreateSetCommand(document, propertyId, value));
    ProjectToml.Save(projectDirectory, document);
    DocumentProperty property = DocumentProperties.Enumerate(document).Single(item => item.Id == propertyId);
    Console.WriteLine($"Set {property.Id}={property.Value}");
    return 0;
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

static int Install(string projectDirectory, string objectText, string statusText, string? actualLengthText)
{
    ObjectId objectId = ObjectId.Parse(objectText);
    if (!Enum.TryParse(statusText, true, out InstallationStatus status))
    {
        throw new ArgumentException($"Unknown installation status '{statusText}'.");
    }
    double? actualLength = actualLengthText is null
        ? null
        : double.Parse(actualLengthText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    InstallationRecord previous = document.RequireInstallationRecord(objectId);
    var desired = new InstallationRecord(
        objectId, status, DateTimeOffset.UtcNow, previous.Note, previous.ActualPosition,
        actualLength ?? previous.ActualLengthMillimetres, previous.TestResult, previous.PhotoReferences, previous.Revision);
    InstallationEvent installationEvent = InstallationEvent.CreateSetRecord(previous, desired, "desktop-cli");
    InstallationEventApplyResult result = InstallationJournal.Apply(document, installationEvent);
    if (result.Status != InstallationEventApplyStatus.Applied) throw new InvalidOperationException(result.Message);
    ProjectToml.Save(projectDirectory, document);
    InstallationJournal.AppendUnique(Path.Combine(projectDirectory, InstallationJournal.FileName), [installationEvent]);
    InstallationRecord updated = document.RequireInstallationRecord(objectId);
    Console.WriteLine($"Installation: {objectId} kind={document.RequireInstallationObjectKind(objectId).ToString().ToLowerInvariant()} status={status} actual_length_mm={(updated.ActualLengthMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown")}");
    return 0;
}

static int JournalCreate(string projectDirectory, string outputPath, string objectText, string authorDeviceId, string statusText, string? actualLengthText)
{
    ObjectId objectId = ObjectId.Parse(objectText);
    if (!Enum.TryParse(statusText, true, out InstallationStatus status))
        throw new ArgumentException($"Unknown installation status '{statusText}'.");
    double? actualLength = actualLengthText is null
        ? null
        : double.Parse(actualLengthText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    InstallationRecord current = document.RequireInstallationRecord(objectId);
    var desired = new InstallationRecord(objectId, status, current.UpdatedAt, current.Note, current.ActualPosition,
        actualLength ?? current.ActualLengthMillimetres, current.TestResult, current.PhotoReferences, current.Revision);
    InstallationEvent installationEvent = InstallationEvent.CreateSetRecord(current, desired, authorDeviceId);
    InstallationJournal.AppendUnique(outputPath, [installationEvent]);
    Console.WriteLine($"Created event {installationEvent.EventId:D} object={objectId} base_revision={installationEvent.BaseRevision} output={Path.GetFullPath(outputPath)}");
    return 0;
}

static int JournalApply(string projectDirectory, string incomingPath)
{
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    IReadOnlyList<InstallationEvent> incoming = InstallationJournal.Read(incomingPath);
    string localJournalPath = Path.Combine(projectDirectory, InstallationJournal.FileName);
    Dictionary<Guid, InstallationEvent> localEvents = InstallationJournal.Read(localJournalPath).ToDictionary(item => item.EventId);
    var collisionIds = new HashSet<Guid>();
    var results = incoming.Select(installationEvent =>
    {
        if (localEvents.TryGetValue(installationEvent.EventId, out InstallationEvent? existing) &&
            !string.Equals(InstallationJournal.Serialize(existing), InstallationJournal.Serialize(installationEvent), StringComparison.Ordinal))
        {
            collisionIds.Add(installationEvent.EventId);
            return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Conflict,
                "Event ID already exists locally with different content.");
        }
        return InstallationJournal.Apply(document, installationEvent);
    }).ToList();
    if (results.Any(result => result.Status == InstallationEventApplyStatus.Applied)) ProjectToml.Save(projectDirectory, document);
    IEnumerable<InstallationEvent> retainable = results
        .Where(result => !collisionIds.Contains(result.Event.EventId))
        .Select(result => result.Event);
    int appended = InstallationJournal.AppendUnique(localJournalPath, retainable);
    foreach (InstallationEventApplyResult result in results)
        Console.WriteLine($"{result.Status.ToString().ToLowerInvariant()}\t{result.Event.EventId:D}\t{result.Event.ObjectId}\t{result.Message}");
    int applied = results.Count(result => result.Status == InstallationEventApplyStatus.Applied);
    int duplicates = results.Count(result => result.Status == InstallationEventApplyStatus.Duplicate);
    int conflicts = results.Count(result => result.Status == InstallationEventApplyStatus.Conflict);
    Console.WriteLine($"Journal: applied={applied} duplicates={duplicates} conflicts={conflicts} appended={appended}");
    return conflicts > 0 ? 4 : 0;
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

static int Move3D(string projectDirectory, string handleText, string xText, string yText, string zText)
{
    EditHandleId handleId = EditHandleId.Parse(handleText);
    if (!DocumentHandleEditor.CanSetPosition(handleId))
    {
        throw new InvalidOperationException($"Handle '{handleId}' is an anchor and cannot be moved.");
    }
    double x = double.Parse(xText, NumberStyles.Float, CultureInfo.InvariantCulture);
    double y = double.Parse(yText, NumberStyles.Float, CultureInfo.InvariantCulture);
    double z = double.Parse(zText, NumberStyles.Float, CultureInfo.InvariantCulture);
    ProjectDocument document = ProjectToml.Load(projectDirectory);
    var history = new CommandHistory();
    history.Execute(document, new MoveSpatialHandleCommand(handleId, new Point3(x, y, z)));
    ProjectToml.Save(projectDirectory, document);
    Console.WriteLine($"Moved {handleId} to {x.ToString("0.###", CultureInfo.InvariantCulture)}, {y.ToString("0.###", CultureInfo.InvariantCulture)}, Z {z.ToString("0.###", CultureInfo.InvariantCulture)} mm");
    return 0;
}

static void PrintSummary(ProjectDocument document)
{
    Console.WriteLine($"OK schema={document.SchemaVersion} name={document.Name}");
    Console.WriteLine($"devices={document.Devices.Count} openings={document.Openings.Count} dimensions={document.WallDimensions.Count} conduits={document.Conduits.Count} cables={document.Cables.Count} annotations={document.Annotations.Count} handles={DocumentHandles.Enumerate(document).Count}");
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
    Console.WriteLine("Conduit fill (mechanical planning check):");
    foreach (ConduitFill fill in analysis.ConduitFills)
    {
        ConduitLoad load = analysis.ConduitLoads.Single(item => item.ConduitId == fill.ConduitId);
        Console.WriteLine($"  {fill.ConduitId}\t{fill.FillRatio.ToString("P1", CultureInfo.InvariantCulture)}\tcables={load.CableCount} circuits={load.PowerCircuitCount} conductors={load.ConductorCount} loaded={load.KnownLoadedConductorCount} unknown_loaded={load.UnknownLoadedCircuitCount} unknown_diameter={fill.UnknownCableCount + fill.UnknownConductorCount}");
    }
    Console.WriteLine($"Electrical sizing ({document.ElectricalRules.Id}):");
    foreach (ElectricalDesignCheck check in analysis.ElectricalDesignChecks)
    {
        Console.WriteLine($"  {check.CableId}\t{check.Status.ToString().ToUpperInvariant()}\t{check.Message}");
    }
    Console.WriteLine($"Diagnostics: errors={analysis.ErrorCount} warnings={analysis.WarningCount}");
    foreach (ProjectDiagnostic diagnostic in analysis.Diagnostics)
    {
        string objectText = diagnostic.ObjectId is ObjectId id ? $" [{id}]" : string.Empty;
        Console.WriteLine($"  {diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}{objectText}: {diagnostic.Message}");
    }
    return analysis.ErrorCount > 0 ? 3 : 0;
}

static int PrintTasks(ProjectDocument document)
{
    ProjectAnalysis analysis = ProjectAnalyzer.Analyze(document);
    Console.WriteLine($"Installation tasks: completed={analysis.CompletedInstallationCount} total={analysis.InstallationTasks.Count}");
    foreach (InstallationTask task in analysis.InstallationTasks)
    {
        string actual = task.ActualLengthMillimetres is double length
            ? length.ToString("0.###", CultureInfo.InvariantCulture)
            : "unknown";
        string planned = task.PlannedLengthMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? "n/a";
        Console.WriteLine($"{task.ObjectId}\t{task.Kind.ToString().ToLowerInvariant()}\t{task.Status.ToString().ToLowerInvariant()}\t{task.Label}\t{task.From}\t{task.To}\tplanned_mm={planned}\tactual_mm={actual}");
    }
    return analysis.ErrorCount > 0 ? 3 : 0;
}

static void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  eutherwire create-demo <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire validate <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire report <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire tasks <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire normalize <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire handles <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire properties <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire set-property <project.eutherwire> <property-handle> <value>");
    Console.Error.WriteLine("  eutherwire export-svg <project.eutherwire> <output.svg>");
    Console.Error.WriteLine("  eutherwire export-png <project.eutherwire> <output.png>");
    Console.Error.WriteLine("  eutherwire move <project.eutherwire> <handle-id> <x-mm> <y-mm>");
    Console.Error.WriteLine("  eutherwire move3d <project.eutherwire> <handle-id> <x-mm> <y-mm> <z-mm>");
    Console.Error.WriteLine("  eutherwire insert-vertex <project.eutherwire> <route-id> <index> <x-mm> <y-mm>");
    Console.Error.WriteLine("  eutherwire delete-vertex <project.eutherwire> <route-id> <index>");
    Console.Error.WriteLine("  eutherwire configure <project.eutherwire> <slack-percent> <service-loop-mm>");
    Console.Error.WriteLine("  eutherwire install <project.eutherwire> <object-id> <planned|installed|tested|changed|blocked> [actual-length-mm]");
    Console.Error.WriteLine("  eutherwire journal-create <project.eutherwire> <output.jsonl> <object-id> <author-device-id> <status> [actual-length-mm]");
    Console.Error.WriteLine("  eutherwire journal-apply <project.eutherwire> <incoming.jsonl>");
}
