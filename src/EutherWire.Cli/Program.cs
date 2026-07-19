using System.Globalization;
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
        default:
            Usage();
            return 1;
    }
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

static void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  eutherwire create-demo <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire validate <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire normalize <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire handles <project.eutherwire>");
    Console.Error.WriteLine("  eutherwire move <project.eutherwire> <handle-id> <x-mm> <y-mm>");
}
