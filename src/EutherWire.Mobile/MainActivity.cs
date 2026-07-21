using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using EutherWire.Document.Analysis;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Synchronization;
using Color = Android.Graphics.Color;
using Typeface = Android.Graphics.Typeface;
using TypefaceStyle = Android.Graphics.TypefaceStyle;

namespace EutherWire.Mobile;

[Activity(
    Label = "EutherWire",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Unspecified)]
public sealed class MainActivity : Activity
{
    private const int ImportSnapshotRequest = 1001;
    private const int ExportEventsRequest = 1002;
    private const string PreferencesName = "eutherwire_mobile";
    private const string ActiveProjectKey = "active_project";
    private const string DeviceIdKey = "device_id";
    private const string PendingJournalName = "pending-installation-events.jsonl";

    private readonly string[] _filterNames = ["ALL", "TO DO", "DONE", "BLOCKED"];
    private ProjectDocument? _document;
    private string? _projectDirectory;
    private LinearLayout? _taskList;
    private TextView? _projectTitle;
    private TextView? _summary;
    private TextView? _message;
    private Spinner? _filter;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        LoadActiveProject();
    }

    private void BuildUi()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetBackgroundColor(Color.ParseColor("#0b1821"));
        root.SetPadding(Dp(16), Dp(14), Dp(16), Dp(10));

        var heading = Text("EUTHERWIRE · INSTALL", 21, "#55d7ff");
        heading.SetTypeface(Typeface.Monospace, TypefaceStyle.Bold);
        root.AddView(heading);
        _projectTitle = Text("NO PROJECT LOADED", 15, "#f2c94c");
        root.AddView(_projectTitle);
        _summary = Text("Import a project snapshot to begin.", 13, "#9eb0bb");
        root.AddView(_summary);

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetPadding(0, Dp(10), 0, Dp(8));
        Button import = ActionButton("IMPORT SNAPSHOT");
        import.Click += (_, _) => BeginImport();
        actions.AddView(import, Weighted());
        Button export = ActionButton("EXPORT EVENTS");
        export.Click += (_, _) => BeginExport();
        actions.AddView(export, Weighted());
        root.AddView(actions);

        _filter = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, _filterNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _filter.Adapter = adapter;
        _filter.ItemSelected += (_, _) => RenderTasks();
        root.AddView(_filter, MatchWrap());

        _message = Text(string.Empty, 12, "#f2c94c");
        root.AddView(_message);

        var scroll = new ScrollView(this)
        {
            FillViewport = true,
        };
        _taskList = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        scroll.AddView(_taskList, MatchWrap());
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(root);
    }

    private void LoadActiveProject()
    {
        string? directory = Preferences.GetString(ActiveProjectKey, null);
        if (string.IsNullOrWhiteSpace(directory) || !File.Exists(Path.Combine(directory, ProjectToml.FileName)))
        {
            _document = null;
            _projectDirectory = null;
            RenderTasks();
            return;
        }
        try
        {
            _projectDirectory = directory;
            _document = ProjectToml.Load(directory);
            RecoverPendingEvents();
            RenderTasks();
        }
        catch (Exception exception)
        {
            _document = null;
            _projectDirectory = null;
            ShowMessage($"Could not load project: {exception.Message}", error: true);
            RenderTasks();
        }
    }

    private void RenderTasks()
    {
        if (_taskList is null || _projectTitle is null || _summary is null) return;
        _taskList.RemoveAllViews();
        if (_document is null)
        {
            _projectTitle.Text = "NO PROJECT LOADED";
            _summary.Text = "Import a .eutherwire-snapshot to begin.";
            return;
        }

        ProjectAnalysis analysis = ProjectAnalyzer.Analyze(_document);
        int pendingCount = PendingEvents().Count;
        _projectTitle.Text = _document.Name.ToUpperInvariant();
        _summary.Text = $"{analysis.CompletedInstallationCount}/{analysis.InstallationTasks.Count} DONE · {pendingCount} PENDING EVENT{(pendingCount == 1 ? string.Empty : "S")}";
        List<InstallationTask> tasks = Filter(analysis.InstallationTasks, _filter?.SelectedItemPosition ?? 0).ToList();
        foreach (InstallationTask task in tasks)
        {
            Button row = new(this)
            {
                Gravity = GravityFlags.Left | GravityFlags.CenterVertical,
                Text = TaskText(task),
                TextSize = 14,
            };
            row.SetAllCaps(false);
            row.SetTextColor(StatusColour(task.Status));
            row.SetBackgroundColor(Color.ParseColor("#132632"));
            row.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
            row.Click += (_, _) => ShowTask(task.ObjectId);
            var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            parameters.SetMargins(0, Dp(5), 0, Dp(5));
            _taskList.AddView(row, parameters);
        }
        if (tasks.Count == 0)
            _taskList.AddView(Text("No tasks in this filter.", 14, "#9eb0bb"));
    }

    private void ShowTask(ObjectId objectId)
    {
        if (_document is null) return;
        InstallationTask task = ProjectAnalyzer.Analyze(_document).InstallationTasks.Single(item => item.ObjectId == objectId);
        InstallationRecord record = _document.RequireInstallationRecord(objectId);
        var form = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        form.SetPadding(Dp(20), Dp(8), Dp(20), 0);
        form.AddView(Text($"{task.Kind.ToString().ToUpperInvariant()} · {objectId}", 12, "#55d7ff"));
        form.AddView(Text(task.Label, 20, "#ffffff"));
        form.AddView(Text(Details(task), 13, "#9eb0bb"));

        var statusSpinner = new Spinner(this);
        string[] statuses = Enum.GetNames<InstallationStatus>().Select(Name).ToArray();
        var statusAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, statuses);
        statusAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        statusSpinner.Adapter = statusAdapter;
        statusSpinner.SetSelection(Array.IndexOf(Enum.GetValues<InstallationStatus>(), record.Status));
        form.AddView(Label("STATUS"));
        form.AddView(statusSpinner);

        var note = new EditText(this)
        {
            Text = record.Note ?? string.Empty,
            Hint = "What was installed or changed?",
            Gravity = GravityFlags.Top,
        };
        note.SetMinLines(2);
        note.SetTextColor(Color.White);
        note.SetHintTextColor(Color.ParseColor("#718590"));
        form.AddView(Label("FIELD NOTE"));
        form.AddView(note);

        EditText? actualLength = null;
        if (task.Kind is InstallationObjectKind.Cable or InstallationObjectKind.Conduit)
        {
            actualLength = new EditText(this)
            {
                Text = record.ActualLengthMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                Hint = "millimetres",
                InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal,
            };
            actualLength.SetTextColor(Color.White);
            actualLength.SetHintTextColor(Color.ParseColor("#718590"));
            form.AddView(Label("ACTUAL LENGTH (MM)"));
            form.AddView(actualLength);
        }

        var scroll = new ScrollView(this);
        scroll.AddView(form);
        var dialogBuilder = new AlertDialog.Builder(this);
        dialogBuilder.SetView(scroll);
        dialogBuilder.SetNegativeButton("CANCEL", (_, _) => { });
        dialogBuilder.SetPositiveButton("SAVE OFFLINE", (_, _) => SaveTask(objectId, statusSpinner, note, actualLength));
        AlertDialog dialog = dialogBuilder.Create() ?? throw new InvalidOperationException("Could not create task dialog.");
        dialog.Show();
        dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);
    }

    private void SaveTask(ObjectId objectId, Spinner statusSpinner, EditText note, EditText? actualLength)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            InstallationRecord current = _document.RequireInstallationRecord(objectId);
            InstallationStatus status = Enum.GetValues<InstallationStatus>()[statusSpinner.SelectedItemPosition];
            double? length = string.IsNullOrWhiteSpace(actualLength?.Text)
                ? null
                : ParseLength(actualLength.Text);
            var desired = new InstallationRecord(objectId, status, note: note.Text,
                actualPosition: current.ActualPosition,
                actualLengthMillimetres: actualLength is null ? current.ActualLengthMillimetres : length,
                testResult: current.TestResult,
                photoReferences: current.PhotoReferences,
                revision: current.Revision);
            InstallationEvent installationEvent = InstallationEvent.CreateSetRecord(current, desired, AuthorDeviceId);
            string pendingPath = Path.Combine(_projectDirectory, PendingJournalName);
            InstallationJournal.AppendUnique(pendingPath, [installationEvent]);
            InstallationEventApplyResult result = InstallationJournal.Apply(_document, installationEvent);
            if (result.Status != InstallationEventApplyStatus.Applied) throw new InvalidOperationException(result.Message);
            ProjectToml.Save(_projectDirectory, _document);
            InstallationJournal.AppendUnique(Path.Combine(_projectDirectory, InstallationJournal.FileName), [installationEvent]);
            ShowMessage($"Saved offline · revision {_document.RequireInstallationRecord(objectId).Revision}");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not save task: {exception.Message}", error: true);
        }
    }

    private void RecoverPendingEvents()
    {
        if (_document is null || _projectDirectory is null) return;
        IReadOnlyList<InstallationEvent> pending = PendingEvents();
        bool changed = false;
        foreach (InstallationEvent installationEvent in pending)
        {
            InstallationEventApplyResult result = InstallationJournal.Apply(_document, installationEvent);
            changed |= result.Status == InstallationEventApplyStatus.Applied;
        }
        if (changed) ProjectToml.Save(_projectDirectory, _document);
        if (pending.Count > 0)
            InstallationJournal.AppendUnique(Path.Combine(_projectDirectory, InstallationJournal.FileName), pending);
    }

    private void BeginImport()
    {
        void OpenPicker()
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            StartActivityForResult(intent, ImportSnapshotRequest);
        }

        if (PendingEvents().Count == 0)
        {
            OpenPicker();
            return;
        }
        var importBuilder = new AlertDialog.Builder(this);
        importBuilder.SetTitle("Pending field work");
        importBuilder.SetMessage("This project has events that have not been exported. Import another snapshot anyway?");
        importBuilder.SetNegativeButton("CANCEL", (_, _) => { });
        importBuilder.SetPositiveButton("IMPORT", (_, _) => OpenPicker());
        importBuilder.Show();
    }

    private void BeginExport()
    {
        if (_projectDirectory is null || PendingEvents().Count == 0)
        {
            ShowMessage("There are no pending events to export.");
            return;
        }
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/x-ndjson");
        intent.PutExtra(Intent.ExtraTitle, $"eutherwire-{DateTime.UtcNow:yyyyMMdd-HHmm}-events.jsonl");
        StartActivityForResult(intent, ExportEventsRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is null) return;
        try
        {
            if (requestCode == ImportSnapshotRequest) ImportSnapshot(data.Data);
            if (requestCode == ExportEventsRequest) ExportPendingEvents(data.Data);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, error: true);
        }
    }

    private void ImportSnapshot(Android.Net.Uri uri)
    {
        string temporarySnapshot = Path.Combine(CacheDir!.AbsolutePath, $"import-{Guid.NewGuid():N}.eutherwire-snapshot");
        try
        {
            using (Stream input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Could not open selected snapshot."))
            using (Stream output = File.Create(temporarySnapshot)) input.CopyTo(output);
            string projectDirectory = Path.Combine(FilesDir!.AbsolutePath, "projects", $"project-{Guid.NewGuid():N}.eutherwire");
            ProjectSnapshotInfo info = ProjectSnapshot.Import(temporarySnapshot, projectDirectory);
            Preferences.Edit()!.PutString(ActiveProjectKey, projectDirectory)!.Apply();
            _projectDirectory = projectDirectory;
            _document = ProjectToml.Load(projectDirectory);
            ShowMessage($"Imported {info.FileCount} files · schema {info.ProjectSchemaVersion}");
            RenderTasks();
        }
        finally
        {
            if (File.Exists(temporarySnapshot)) File.Delete(temporarySnapshot);
        }
    }

    private void ExportPendingEvents(Android.Net.Uri uri)
    {
        if (_projectDirectory is null) return;
        string pendingPath = Path.Combine(_projectDirectory, PendingJournalName);
        using Stream input = File.OpenRead(pendingPath);
        using Stream output = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("Could not create event file.");
        input.CopyTo(output);
        ShowMessage($"Exported {PendingEvents().Count} pending event(s). Import a refreshed snapshot to clear them.");
    }

    private IReadOnlyList<InstallationEvent> PendingEvents() =>
        _projectDirectory is null ? [] : InstallationJournal.Read(Path.Combine(_projectDirectory, PendingJournalName));

    private IEnumerable<InstallationTask> Filter(IEnumerable<InstallationTask> tasks, int selected) => selected switch
    {
        1 => tasks.Where(task => task.Status is not (InstallationStatus.Installed or InstallationStatus.Tested)),
        2 => tasks.Where(task => task.Status is InstallationStatus.Installed or InstallationStatus.Tested),
        3 => tasks.Where(task => task.Status == InstallationStatus.Blocked),
        _ => tasks,
    };

    private static string TaskText(InstallationTask task)
    {
        string marker = task.Status switch
        {
            InstallationStatus.Installed => "✓",
            InstallationStatus.Tested => "✓✓",
            InstallationStatus.Blocked => "!",
            InstallationStatus.Changed => "Δ",
            _ => "○",
        };
        return $"{marker}  {task.Label}\n{task.Kind.ToString().ToUpperInvariant()} · {Name(task.Status.ToString())}";
    }

    private static string Details(InstallationTask task)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.From)) lines.Add($"FROM  {task.From}");
        if (!string.IsNullOrWhiteSpace(task.To)) lines.Add($"TO    {task.To}");
        if (task.PlannedLengthMillimetres is double planned) lines.Add($"PLAN  {planned / 1000:0.##} m");
        if (task.ActualLengthMillimetres is double actual) lines.Add($"ACTUAL {actual / 1000:0.##} m");
        if (!string.IsNullOrWhiteSpace(task.Note)) lines.Add($"NOTE  {task.Note}");
        return string.Join("\n", lines);
    }

    private void ShowMessage(string message, bool error = false)
    {
        if (_message is not null)
        {
            _message.Text = message;
            _message.SetTextColor(Color.ParseColor(error ? "#ff6b6b" : "#f2c94c"));
        }
        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }

    private TextView Text(string value, float size, string colour)
    {
        var text = new TextView(this)
        {
            Text = value,
            TextSize = size,
        };
        text.SetTextColor(Color.ParseColor(colour));
        return text;
    }

    private TextView Label(string value)
    {
        TextView label = Text(value, 11, "#55d7ff");
        label.SetPadding(0, Dp(12), 0, 0);
        return label;
    }

    private Button ActionButton(string label)
    {
        var button = new Button(this)
        {
            Text = label,
            TextSize = 12,
        };
        button.SetTextColor(Color.ParseColor("#dff7ff"));
        return button;
    }

    private LinearLayout.LayoutParams Weighted()
    {
        var parameters = new LinearLayout.LayoutParams(0, Dp(52), 1);
        parameters.SetMargins(Dp(3), 0, Dp(3), 0);
        return parameters;
    }

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private ISharedPreferences Preferences => GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    private string AuthorDeviceId
    {
        get
        {
            string? existing = Preferences.GetString(DeviceIdKey, null);
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
            string created = $"android-{Guid.NewGuid():N}";
            Preferences.Edit()!.PutString(DeviceIdKey, created)!.Apply();
            return created;
        }
    }

    private static string Name(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {char.ToUpperInvariant(character)}" : char.ToUpperInvariant(character).ToString()));

    private static double ParseLength(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double local)) return local;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant)) return invariant;
        string alternate = value.Contains(',') ? value.Replace(',', '.') : value.Replace('.', ',');
        if (double.TryParse(alternate, NumberStyles.Float, CultureInfo.CurrentCulture, out double converted)) return converted;
        throw new FormatException("Length must be a number, for example 12500 or 12500.5.");
    }

    private static Color StatusColour(InstallationStatus status) => Color.ParseColor(status switch
    {
        InstallationStatus.Installed or InstallationStatus.Tested => "#66e6a5",
        InstallationStatus.Blocked => "#ff6b6b",
        InstallationStatus.Changed => "#f2c94c",
        _ => "#dff7ff",
    });
}
