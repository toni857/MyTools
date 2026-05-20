using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace My;

public sealed record RunningProgramInfo(int ProcessId, string Name, string WindowTitle);

public sealed record ProgramVariableInfo(
    string ProgramName,
    string Name,
    string TypeName,
    object? Value,
    bool CanChange,
    string? DisplayName = null)
{
    public string ValueText => Programs.FormatValue(Value);
}

public sealed record ProgramVariableSnapshot(
    string ProgramName,
    DateTime CreatedAt,
    IReadOnlyDictionary<string, string> Values);

public sealed record ProgramVariableChange(
    string ProgramName,
    string Name,
    string OldValue,
    string NewValue);

public sealed record MemoryInt32Match(
    int ProcessId,
    string ProgramName,
    string Address,
    int Value,
    string? DisplayName = null);

public static class Programs
{
    private const long MaxMemoryScanBytes = 256L * 1024 * 1024;
    private const int PrSetDumpable = 4;
    private const int PrSetPtracer = 0x59616d61;
    private const int ThrowawayVariableMask = unchecked((int)0x5A7E19C3);
    private static readonly nint PrSetPtracerAny = -1;
    private static string AllowedMemoryScanProgramName = ProgramNames.ThrowawayVariableProgram;
    private static volatile int EncodedThrowawayVariable;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DebugProgram> DebugPrograms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PersistentVariableNames = LoadPersistentNames("variable");
    private static readonly Dictionary<string, string> PersistentMemoryNames = LoadPersistentNames("memory");

    public static string CurrentProgramName
    {
        get
        {
            using Process currentProcess = Process.GetCurrentProcess();
            return currentProcess.ProcessName;
        }
    }

    public static void RunMemoryScanTargetProgram()
    {
        AllowLinuxMemoryScan();
        SetThrowawayVariable(42);

        Console.WriteLine($"{AllowedMemoryScanProgramName} läuft. Variable: {GetThrowawayVariable()}");

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);

            if (key.KeyChar is 'e' or 'E')
            {
                SetThrowawayVariable(GetThrowawayVariable() + 1);
                Console.WriteLine($"Variable: {GetThrowawayVariable()}");
            }
            else if (key.KeyChar is 'r' or 'R')
            {
                SetThrowawayVariable(43);
                Console.WriteLine($"Variable: {GetThrowawayVariable()}");
            }
        }
    }

    private static int GetThrowawayVariable()
    {
        return EncodedThrowawayVariable ^ ThrowawayVariableMask;
    }

    private static void SetThrowawayVariable(int value)
    {
        EncodedThrowawayVariable = value ^ ThrowawayVariableMask;
    }

    private static void AllowLinuxMemoryScan()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        _ = prctl(PrSetDumpable, 1, 0, 0, 0);
        _ = prctl(PrSetPtracer, PrSetPtracerAny, 0, 0, 0);
    }

    public static IReadOnlyList<RunningProgramInfo> ListRunningPrograms(bool onlyProgramsWithWindow = false)
    {
        List<RunningProgramInfo> programs = [];

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string title = process.MainWindowTitle;

                if (onlyProgramsWithWindow && string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                programs.Add(new RunningProgramInfo(process.Id, process.ProcessName, title));
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return programs
            .OrderBy(program => program.Name)
            .ThenBy(program => program.ProcessId)
            .ToList();
    }

    public static void ShowRunningPrograms(bool onlyProgramsWithWindow = false)
    {
        IReadOnlyList<RunningProgramInfo> programs = ListRunningPrograms(onlyProgramsWithWindow);

        if (programs.Count == 0)
        {
            Console.WriteLine("Keine laufenden Programme gefunden.");
            return;
        }

        foreach (RunningProgramInfo program in programs)
        {
            string title = string.IsNullOrWhiteSpace(program.WindowTitle)
                ? ""
                : $" - {program.WindowTitle}";

            Console.WriteLine($"{program.ProcessId}: {program.Name}{title}");
        }
    }

    public static void RegisterVariable<T>(
        string programName,
        string variableName,
        Func<T> getValue,
        Action<T>? setValue = null)
    {
        if (getValue is null)
        {
            throw new ArgumentNullException(nameof(getValue));
        }

        Action<object?>? setter = null;

        if (setValue is not null)
        {
            Action<T> realSetValue = setValue;
            setter = value => realSetValue((T)value!);
        }

        RegisterVariable(programName, variableName, typeof(T), () => getValue(), setter);
    }

    public static void RegisterVariable<T>(
        string variableName,
        Func<T> getValue,
        Action<T>? setValue = null)
    {
        RegisterVariable(CurrentProgramName, variableName, getValue, setValue);
    }

    public static void RegisterObject(
        string programName,
        object target,
        string namePrefix = "",
        bool includePrivate = false)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        RegisterMembers(programName, target.GetType(), target, namePrefix, includePrivate, false);
    }

    public static void RegisterObject(
        object target,
        string namePrefix = "",
        bool includePrivate = false)
    {
        RegisterObject(CurrentProgramName, target, namePrefix, includePrivate);
    }

    public static void RegisterStaticVariables(
        string programName,
        Type type,
        string namePrefix = "",
        bool includePrivate = false)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        RegisterMembers(programName, type, null, namePrefix, includePrivate, true);
    }

    public static void RegisterStaticVariables(
        Type type,
        string namePrefix = "",
        bool includePrivate = false)
    {
        RegisterStaticVariables(CurrentProgramName, type, namePrefix, includePrivate);
    }

    public static IReadOnlyList<string> ListRegisteredPrograms()
    {
        lock (Sync)
        {
            return DebugPrograms.Values
                .Select(program => program.Name)
                .OrderBy(name => name)
                .ToList();
        }
    }

    public static void ShowRegisteredPrograms()
    {
        IReadOnlyList<string> programs = ListRegisteredPrograms();

        if (programs.Count == 0)
        {
            Console.WriteLine("Keine Programme mit registrierten Variablen gefunden.");
            return;
        }

        foreach (string program in programs)
        {
            Console.WriteLine(program);
        }
    }

    public static IReadOnlyList<ProgramVariableInfo> ListVariables(string programName)
    {
        DebugProgram program = GetProgram(programName);

        lock (Sync)
        {
            return program.Variables.Values
                .Select(variable =>
                {
                    program.VariableNames.TryGetValue(variable.Name, out string? displayName);
                    displayName ??= GetPersistentName("variable", program.Name, variable.Name);
                    return variable.CreateInfo(program.Name, displayName);
                })
                .OrderBy(variable => variable.DisplayName ?? variable.Name)
                .ThenBy(variable => variable.Name)
                .ToList();
        }
    }

    public static void ShowVariables(string programName)
    {
        IReadOnlyList<ProgramVariableInfo> variables = ListVariables(programName);

        if (variables.Count == 0)
        {
            Console.WriteLine("Dieses Programm hat keine registrierten Variablen.");
            return;
        }

        PrintVariables(variables);
    }

    public static void WaitForProgramNameAndShowVariables()
    {
        string programName = AskNotEmpty("Programmname: ");
        ShowVariables(programName);
    }

    public static void ChangeVariable(string programName, string variableName, string newValue)
    {
        DebugVariable variable = GetVariable(programName, variableName);

        if (!variable.CanChange)
        {
            throw new InvalidOperationException("Diese Variable kann nur gelesen werden.");
        }

        if (!TryConvert(newValue, variable.Type, out object? convertedValue, out string? errorMessage))
        {
            throw new ArgumentException(errorMessage, nameof(newValue));
        }

        variable.SetValue(convertedValue);
    }

    public static void WaitForVariableChange()
    {
        string programName = AskNotEmpty("Programmname: ");
        string variableName = AskNotEmpty("Variablenname: ");
        string newValue = AskNotEmpty("Neuer Wert: ");

        ChangeVariable(programName, variableName, newValue);
        Console.WriteLine("Variable wurde geändert.");
    }

    public static void NameVariable(string programName, string variableName, string displayName)
    {
        DebugProgram program = GetProgram(programName);
        string realVariableName = ValidateName(variableName, nameof(variableName));
        string realDisplayName = ValidateName(displayName, nameof(displayName));

        lock (Sync)
        {
            if (!program.Variables.ContainsKey(realVariableName))
            {
                throw new ArgumentException("Diese Variable ist nicht registriert.", nameof(variableName));
            }

            program.VariableNames[realVariableName] = realDisplayName;
        }

        SetPersistentName("variable", program.Name, realVariableName, realDisplayName);
    }

    public static void RemoveVariableName(string programName, string variableName)
    {
        DebugProgram program = GetProgram(programName);
        string realVariableName = ValidateName(variableName, nameof(variableName));

        lock (Sync)
        {
            program.VariableNames.Remove(realVariableName);
        }

        RemovePersistentName("variable", program.Name, realVariableName);
    }

    public static IReadOnlyDictionary<string, string> ListVariableNames(string programName)
    {
        DebugProgram program = GetProgram(programName);

        lock (Sync)
        {
            return new Dictionary<string, string>(program.VariableNames, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void WaitForVariableName()
    {
        string programName = AskNotEmpty("Programmname: ");
        string variableName = AskNotEmpty("Variablenname: ");
        string displayName = AskNotEmpty("Eigener Name: ");

        NameVariable(programName, variableName, displayName);
        Console.WriteLine("Name wurde gespeichert.");
    }

    public static void NameMemoryAddress(string programName, string address, string displayName)
    {
        if (!IsAllowedMemoryScanProgramName(programName))
        {
            throw new InvalidOperationException(
                $"Memory-Namen sind nur für {AllowedMemoryScanProgramName} erlaubt.");
        }

        string realAddress = NormalizeMemoryAddress(address);
        string realDisplayName = ValidateName(displayName, nameof(displayName));

        SetPersistentName("memory", AllowedMemoryScanProgramName, realAddress, realDisplayName);
    }

    public static void RemoveMemoryAddressName(string programName, string address)
    {
        if (!IsAllowedMemoryScanProgramName(programName))
        {
            throw new InvalidOperationException(
                $"Memory-Namen sind nur für {AllowedMemoryScanProgramName} erlaubt.");
        }

        RemovePersistentName("memory", AllowedMemoryScanProgramName, NormalizeMemoryAddress(address));
    }

    public static IReadOnlyDictionary<string, string> ListMemoryAddressNames(string programName)
    {
        if (!IsAllowedMemoryScanProgramName(programName))
        {
            throw new InvalidOperationException(
                $"Memory-Namen sind nur für {AllowedMemoryScanProgramName} erlaubt.");
        }

        lock (Sync)
        {
            return PersistentMemoryNames
                .Where(pair => pair.Key.StartsWith($"{AllowedMemoryScanProgramName}:", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    pair => pair.Key[(AllowedMemoryScanProgramName.Length + 1)..],
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void WaitForMemoryAddressName()
    {
        string programName = AskNotEmpty("Programmname: ");

        if (!IsAllowedMemoryScanProgramName(programName))
        {
            Console.WriteLine($"Fehler: Memory-Namen sind nur für {AllowedMemoryScanProgramName} erlaubt. Eingabe war: '{programName}'");
            return;
        }

        string address = AskNotEmpty("Adresse: ");
        string displayName = AskNotEmpty("Eigener Name: ");

        try
        {
            NameMemoryAddress(programName, address, displayName);
            Console.WriteLine("Memory-Name wurde gespeichert.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Fehler: {exception.Message}");
        }
    }

    public static void WaitForMemoryAddressNames()
    {
        string programName = AskNotEmpty("Programmname: ");

        try
        {
            IReadOnlyDictionary<string, string> names = ListMemoryAddressNames(programName);

            if (names.Count == 0)
            {
                Console.WriteLine("Keine Memory-Namen gespeichert.");
                return;
            }

            foreach (KeyValuePair<string, string> name in names.OrderBy(name => name.Value))
            {
                Console.WriteLine($"{name.Key} [{name.Value}]");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Fehler: {exception.Message}");
        }
    }

    public static IReadOnlyList<ProgramVariableInfo> FindVariables(
        string programName,
        Func<ProgramVariableInfo, bool> filter)
    {
        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        return ListVariables(programName)
            .Where(filter)
            .ToList();
    }

    public static IReadOnlyList<ProgramVariableInfo> FindVariablesByName(string programName, string namePart)
    {
        return FindVariables(programName, variable => ContainsText(variable.Name, namePart));
    }

    public static IReadOnlyList<ProgramVariableInfo> FindVariablesByType(string programName, string typePart)
    {
        return FindVariables(programName, variable => ContainsText(variable.TypeName, typePart));
    }

    public static IReadOnlyList<ProgramVariableInfo> FindVariablesByValue(string programName, string value)
    {
        return FindVariables(programName, variable => TextEquals(variable.ValueText, value));
    }

    public static IReadOnlyList<ProgramVariableInfo> FindChangeableVariables(string programName)
    {
        return FindVariables(programName, variable => variable.CanChange);
    }

    public static ProgramVariableSnapshot CaptureSnapshot(string programName)
    {
        IReadOnlyList<ProgramVariableInfo> variables = ListVariables(programName);
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (ProgramVariableInfo variable in variables)
        {
            values[variable.Name] = variable.ValueText;
        }

        string realProgramName = variables.Count == 0
            ? GetProgram(programName).Name
            : variables[0].ProgramName;

        return new ProgramVariableSnapshot(realProgramName, DateTime.Now, values);
    }

    public static IReadOnlyList<ProgramVariableChange> FindVariablesByChange(
        ProgramVariableSnapshot before,
        ProgramVariableSnapshot after,
        string oldValue,
        string newValue)
    {
        if (before is null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (after is null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        if (!TextEquals(before.ProgramName, after.ProgramName))
        {
            throw new ArgumentException("Beide Snapshots müssen vom gleichen Programm kommen.", nameof(after));
        }

        List<ProgramVariableChange> changes = [];

        foreach (KeyValuePair<string, string> oldVariable in before.Values)
        {
            if (!TextEquals(oldVariable.Value, oldValue))
            {
                continue;
            }

            if (!after.Values.TryGetValue(oldVariable.Key, out string? currentValue))
            {
                continue;
            }

            if (TextEquals(currentValue, newValue))
            {
                changes.Add(new ProgramVariableChange(before.ProgramName, oldVariable.Key, oldVariable.Value, currentValue));
            }
        }

        return changes
            .OrderBy(change => change.Name)
            .ToList();
    }

    public static void WaitForVariableSearch()
    {
        string programName = AskNotEmpty("Programmname: ");

        Console.WriteLine("Filter:");
        Console.WriteLine("1 = Name enthält Text");
        Console.WriteLine("2 = Typ enthält Text");
        Console.WriteLine("3 = Aktueller Wert ist genau");
        Console.WriteLine("4 = Wert hat sich von einem Wert zu einem anderen Wert geändert");
        Console.WriteLine("5 = Variable kann geändert werden");

        string filter = AskNotEmpty("Auswahl: ");

        switch (filter)
        {
            case "1":
                PrintVariables(FindVariablesByName(programName, AskNotEmpty("Text im Namen: ")));
                break;

            case "2":
                PrintVariables(FindVariablesByType(programName, AskNotEmpty("Text im Typ: ")));
                break;

            case "3":
                PrintVariables(FindVariablesByValue(programName, AskNotEmpty("Wert: ")));
                break;

            case "4":
                SearchChangedVariables(programName);
                break;

            case "5":
                PrintVariables(FindChangeableVariables(programName));
                break;

            default:
                Console.WriteLine("Unbekannter Filter.");
                break;
        }
    }

    public static IReadOnlyList<MemoryInt32Match> ScanMemoryForInt32(
        string programName,
        int value,
        int maxMatches = 100)
    {
        if (!IsAllowedMemoryScanProgramName(programName))
        {
            throw new InvalidOperationException(
                $"Memoryscan ist nur für {AllowedMemoryScanProgramName} erlaubt.");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Memoryscan ist in dieser Bibliothek nur unter Linux eingebaut.");
        }

        if (maxMatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMatches), "Die Trefferanzahl muss größer als 0 sein.");
        }

        using Process process = FindAllowedMemoryScanProcess()
            ?? throw new InvalidOperationException("Das Wegwerfprogramm läuft gerade nicht.");

        return ScanLinuxProcessMemoryForInt32(process.Id, process.ProcessName, value, maxMatches);
    }

    public static IReadOnlyList<MemoryInt32Match> ScanMemoryValueSequence(
        string programName,
        IReadOnlyList<int> values,
        int maxMatches = 100)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Wert muss angegeben werden.", nameof(values));
        }

        Dictionary<string, MemoryInt32Match> candidates = ScanMemoryForInt32(programName, values[0], maxMatches)
            .ToDictionary(match => match.Address, StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < values.Count && candidates.Count > 0; index++)
        {
            Console.WriteLine($"Ändere den Wert jetzt auf {values[index]} und drücke Enter.");
            Console.ReadLine();

            HashSet<string> currentAddresses = ScanMemoryForInt32(programName, values[index], maxMatches * 4)
                .Select(match => match.Address)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string address in candidates.Keys.ToList())
            {
                if (!currentAddresses.Contains(address))
                {
                    candidates.Remove(address);
                }
            }
        }

        return candidates.Values
            .Select(match =>
            {
                string? displayName = GetPersistentName("memory", match.ProgramName, match.Address);
                return match with { Value = values[^1], DisplayName = displayName };
            })
            .OrderBy(match => match.DisplayName ?? match.Address)
            .ThenBy(match => match.Address)
            .ToList();
    }

    public static IReadOnlyList<MemoryInt32Match> ScanMemoryByInt32Delta(
        string programName,
        int changeCount,
        int deltaPerChange,
        int maxMatches = 1000)
    {
        if (!IsAllowedMemoryScanProgramName(programName))
        {
            throw new InvalidOperationException(
                $"Memoryscan ist nur für {AllowedMemoryScanProgramName} erlaubt.");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Memoryscan ist in dieser Bibliothek nur unter Linux eingebaut.");
        }

        if (changeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changeCount), "Die Anzahl der Änderungen muss größer als 0 sein.");
        }

        if (maxMatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMatches), "Die Trefferanzahl muss größer als 0 sein.");
        }

        using Process process = FindAllowedMemoryScanProcess()
            ?? throw new InvalidOperationException("Das Wegwerfprogramm läuft gerade nicht.");

        IReadOnlyList<MemorySnapshotBlock> snapshot = CaptureLinuxMemorySnapshot(process.Id);
        List<MemoryInt32Match> candidates = [];

        for (int index = 0; index < changeCount; index++)
        {
            Console.WriteLine($"Ändere den Wert jetzt um {deltaPerChange} und drücke Enter.");
            Console.ReadLine();

            candidates = index == 0
                ? FindLinuxInt32DeltaCandidates(process.Id, process.ProcessName, snapshot, deltaPerChange, maxMatches)
                : FilterLinuxInt32DeltaCandidates(process.Id, candidates, deltaPerChange);

            Console.WriteLine($"Kandidaten nach Änderung {index + 1}: {candidates.Count}");

            if (candidates.Count == 0)
            {
                break;
            }
        }

        return candidates
            .Select(match =>
            {
                string? displayName = GetPersistentName("memory", match.ProgramName, match.Address);
                return match with { DisplayName = displayName };
            })
            .OrderBy(match => match.DisplayName ?? match.Address)
            .ThenBy(match => match.Address)
            .ToList();
    }

    public static void WaitForMemoryScan()
    {
        string programName = AskNotEmpty("Programmname: ");

        if (!IsAllowedMemoryScanProgramName(programName))
        {
            Console.WriteLine($"Fehler: Memoryscan ist nur für {AllowedMemoryScanProgramName} erlaubt. Eingabe war: '{programName}'");
            return;
        }

        Console.WriteLine("Memory-Filter:");
        Console.WriteLine("1 = Genauer Int32-Wert");
        Console.WriteLine("2 = Feste Wertfolge");
        Console.WriteLine("3 = Aufsteigende Werte");
        Console.WriteLine("4 = Absteigende Werte");
        Console.WriteLine("5 = Gespeicherte Memory-Namen anzeigen");
        Console.WriteLine("6 = Rohwert ändert sich um Betrag");

        string filter = AskNotEmpty("Auswahl: ");

        try
        {
            switch (filter)
            {
                case "1":
                    PrintMemoryMatches(ScanMemoryForInt32(programName, AskInt32("Int32-Wert suchen: ")));
                    break;

                case "2":
                    PrintMemoryMatches(ScanMemoryValueSequence(programName, AskMemoryValueSequence(null)));
                    break;

                case "3":
                    PrintMemoryMatches(ScanMemoryValueSequence(programName, AskMemoryValueSequence(MemorySequenceDirection.Ascending)));
                    break;

                case "4":
                    PrintMemoryMatches(ScanMemoryValueSequence(programName, AskMemoryValueSequence(MemorySequenceDirection.Descending)));
                    break;

                case "5":
                    PrintMemoryAddressNames(programName);
                    break;

                case "6":
                    PrintMemoryMatches(ScanMemoryByInt32Delta(
                        programName,
                        AskPositiveInt("Wie viele Änderungen willst du prüfen? "),
                        AskInt32("Änderung pro Schritt: ")));
                    break;

                default:
                    Console.WriteLine("Unbekannter Memory-Filter.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Fehler: {exception.Message}");
        }
    }

    public static void WaitForAllowedMemoryScanProgramNameChange()
    {
        Console.WriteLine($"Aktuell erlaubt: {AllowedMemoryScanProgramName}");
        string programName = AskNotEmpty("Neuer erlaubter Programmname: ");
        SetAllowedMemoryScanProgramName(programName);
        Console.WriteLine($"Erlaubter Programmname wurde geändert: {AllowedMemoryScanProgramName}");
    }

    public static void SetConsolePhotoWindowSize()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Write("\x1b[4;1200;1900t");
                Console.Write("\x1b[8;60;220t");
            }

            if (OperatingSystem.IsWindows())
            {
                int width = global::System.Math.Min(220, Console.LargestWindowWidth);
                int height = global::System.Math.Min(60, Console.LargestWindowHeight);

                if (width > 0 && height > 0)
                {
                    Console.SetBufferSize(global::System.Math.Max(width, Console.BufferWidth), global::System.Math.Max(2000, height));
                    Console.SetWindowSize(width, height);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static void StartDebugConsole()
    {
        Console.WriteLine("Debug-Konsole gestartet. Schreibe help für Befehle.");

        while (true)
        {
            Console.Write("> ");
            string? command = Console.ReadLine();

            if (command is null)
            {
                return;
            }

            switch (command.Trim().ToLowerInvariant())
            {
                case "exit":
                case "quit":
                case "ende":
                    return;

                case "help":
                case "hilfe":
                    ShowHelp();
                    break;

                case "programs":
                case "prozesse":
                    ShowRunningPrograms();
                    break;

                case "targets":
                case "registriert":
                    ShowRegisteredPrograms();
                    break;

                case "list":
                case "lsit":
                case "variablen":
                    WaitForProgramNameAndShowVariables();
                    break;

                case "set":
                case "ändern":
                case "aendern":
                    WaitForVariableChange();
                    break;

                case "search":
                case "suche":
                    WaitForVariableSearch();
                    break;

                case "memscan":
                case "memoryscan":
                    WaitForMemoryScan();
                    break;

                case "target":
                case "memtarget":
                case "program":
                    WaitForAllowedMemoryScanProgramNameChange();
                    break;

                case "memname":
                case "memoryname":
                    WaitForMemoryAddressName();
                    break;

                case "memnames":
                case "memorynames":
                    WaitForMemoryAddressNames();
                    break;

                case "name":
                case "benennen":
                    WaitForVariableName();
                    break;

                case "":
                    break;

                default:
                    Console.WriteLine("Unbekannter Befehl. Schreibe help für Befehle.");
                    break;
            }
        }
    }

    internal static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static void RegisterMembers(
        string programName,
        Type type,
        object? target,
        string namePrefix,
        bool includePrivate,
        bool staticMembers)
    {
        BindingFlags flags = staticMembers
            ? BindingFlags.Static
            : BindingFlags.Instance;

        flags |= BindingFlags.Public;

        if (includePrivate)
        {
            flags |= BindingFlags.NonPublic;
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.IsStatic != staticMembers)
            {
                continue;
            }

            string variableName = CreateVariableName(namePrefix, field.Name);
            Action<object?>? setter = field.IsInitOnly
                ? null
                : value => field.SetValue(target, value);

            RegisterVariable(
                programName,
                variableName,
                field.FieldType,
                () => field.GetValue(target),
                setter);
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            MethodInfo? getter = property.GetGetMethod(includePrivate);

            if (getter is null || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (getter.IsStatic != staticMembers)
            {
                continue;
            }

            MethodInfo? setterMethod = property.GetSetMethod(includePrivate);
            Action<object?>? setter = setterMethod is null
                ? null
                : value => property.SetValue(target, value);

            RegisterVariable(
                programName,
                CreateVariableName(namePrefix, property.Name),
                property.PropertyType,
                () => property.GetValue(target),
                setter);
        }
    }

    private static void RegisterVariable(
        string programName,
        string variableName,
        Type variableType,
        Func<object?> getValue,
        Action<object?>? setValue)
    {
        string realProgramName = ValidateName(programName, nameof(programName));
        string realVariableName = ValidateName(variableName, nameof(variableName));

        lock (Sync)
        {
            if (!DebugPrograms.TryGetValue(realProgramName, out DebugProgram? program))
            {
                program = new DebugProgram(realProgramName);
                DebugPrograms.Add(realProgramName, program);
            }

            program.Variables[realVariableName] = new DebugVariable(realVariableName, variableType, getValue, setValue);
        }
    }

    private static DebugProgram GetProgram(string programName)
    {
        string realProgramName = ValidateName(programName, nameof(programName));

        lock (Sync)
        {
            if (DebugPrograms.TryGetValue(realProgramName, out DebugProgram? program))
            {
                return program;
            }
        }

        throw new ArgumentException("Dieses Programm hat keine registrierten Variablen.", nameof(programName));
    }

    private static DebugVariable GetVariable(string programName, string variableName)
    {
        DebugProgram program = GetProgram(programName);
        string realVariableName = ValidateName(variableName, nameof(variableName));

        lock (Sync)
        {
            if (program.Variables.TryGetValue(realVariableName, out DebugVariable? variable))
            {
                return variable;
            }
        }

        throw new ArgumentException("Diese Variable ist nicht registriert.", nameof(variableName));
    }

    private static void SearchChangedVariables(string programName)
    {
        string oldValue = AskNotEmpty("Alter Wert: ");
        ProgramVariableSnapshot before = CaptureSnapshot(programName);

        Console.WriteLine("Erster Snapshot wurde gespeichert. Ändere jetzt den Wert und drücke Enter.");
        Console.ReadLine();

        string newValue = AskNotEmpty("Neuer Wert: ");
        ProgramVariableSnapshot after = CaptureSnapshot(programName);
        IReadOnlyList<ProgramVariableChange> changes = FindVariablesByChange(before, after, oldValue, newValue);

        if (changes.Count == 0)
        {
            Console.WriteLine("Keine passenden Variablen gefunden.");
            return;
        }

        foreach (ProgramVariableChange change in changes)
        {
            Console.WriteLine($"{change.Name}: {change.OldValue} -> {change.NewValue}");
        }
    }

    private static void PrintVariables(IReadOnlyList<ProgramVariableInfo> variables)
    {
        if (variables.Count == 0)
        {
            Console.WriteLine("Keine passenden Variablen gefunden.");
            return;
        }

        foreach (ProgramVariableInfo variable in variables)
        {
            string changeText = variable.CanChange ? "änderbar" : "nur lesen";
            string displayName = string.IsNullOrWhiteSpace(variable.DisplayName)
                ? ""
                : $" [{variable.DisplayName}]";

            Console.WriteLine($"{variable.Name}{displayName} ({variable.TypeName}, {changeText}) = {variable.ValueText}");
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("programs    Laufende Prozesse anzeigen");
        Console.WriteLine("targets     Programme mit registrierten Variablen anzeigen");
        Console.WriteLine("list        Variablen eines registrierten Programms anzeigen");
        Console.WriteLine("set         Variable ändern");
        Console.WriteLine("search      Variablen filtern");
        Console.WriteLine("memscan     Speicher im Wegwerfprogramm suchen");
        Console.WriteLine("target      Erlaubten Memoryscan-Programmnamen ändern");
        Console.WriteLine("memname     Speicheradresse im Debugger benennen");
        Console.WriteLine("memnames    Gespeicherte Memory-Namen anzeigen");
        Console.WriteLine("name        Variable für dieses Programm benennen");
        Console.WriteLine("exit        Debug-Konsole beenden");
    }

    private static int AskInt32(string question)
    {
        while (true)
        {
            string valueText = AskNotEmpty(question);

            if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value)
                || int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            Console.WriteLine("Bitte eine ganze Zahl eingeben.");
        }
    }

    private static IReadOnlyList<int> AskMemoryValueSequence(MemorySequenceDirection? direction)
    {
        List<int> values = [AskInt32("Startwert: ")];
        int changeCount = AskPositiveInt("Wie viele Änderungen willst du eingeben? ");

        for (int index = 0; index < changeCount; index++)
        {
            while (true)
            {
                int nextValue = AskInt32($"Wert nach Änderung {index + 1}: ");

                if (direction == MemorySequenceDirection.Ascending && nextValue <= values[^1])
                {
                    Console.WriteLine("Der nächste Wert muss größer als der vorherige Wert sein.");
                    continue;
                }

                if (direction == MemorySequenceDirection.Descending && nextValue >= values[^1])
                {
                    Console.WriteLine("Der nächste Wert muss kleiner als der vorherige Wert sein.");
                    continue;
                }

                values.Add(nextValue);
                break;
            }
        }

        return values;
    }

    private static int AskPositiveInt(string question)
    {
        while (true)
        {
            int value = AskInt32(question);

            if (value > 0)
            {
                return value;
            }

            Console.WriteLine("Bitte eine Zahl größer als 0 eingeben.");
        }
    }

    private static void PrintMemoryMatches(IReadOnlyList<MemoryInt32Match> matches)
    {
        if (matches.Count == 0)
        {
            Console.WriteLine("Keine Speicherstellen gefunden.");
            return;
        }

        foreach (MemoryInt32Match match in matches)
        {
            string displayName = string.IsNullOrWhiteSpace(match.DisplayName)
                ? ""
                : $" [{match.DisplayName}]";

            Console.WriteLine($"{match.ProgramName}({match.ProcessId}) {match.Address}{displayName} = {match.Value}");
        }

        Console.WriteLine($"Treffer: {matches.Count}");
    }

    private static void PrintMemoryAddressNames(string programName)
    {
        IReadOnlyDictionary<string, string> names = ListMemoryAddressNames(programName);

        if (names.Count == 0)
        {
            Console.WriteLine("Keine Memory-Namen gespeichert.");
            return;
        }

        foreach (KeyValuePair<string, string> name in names.OrderBy(name => name.Value))
        {
            Console.WriteLine($"{name.Key} [{name.Value}]");
        }
    }

    private static IReadOnlyList<MemoryInt32Match> ScanLinuxProcessMemoryForInt32(
        int processId,
        string programName,
        int value,
        int maxMatches)
    {
        string mapsPath = $"/proc/{processId}/maps";
        string memoryPath = $"/proc/{processId}/mem";
        byte[] searchedBytes = BitConverter.GetBytes(value);
        byte[] buffer = new byte[64 * 1024];
        List<MemoryInt32Match> matches = [];
        long scannedBytes = 0;

        using FileStream memory = new(memoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        foreach (MemoryRegion region in ReadLinuxMemoryRegions(mapsPath))
        {
            ulong position = region.Start;
            ulong remaining = region.End - region.Start;

            while (remaining > 0 && matches.Count < maxMatches && scannedBytes < MaxMemoryScanBytes)
            {
                int bytesToRead = (int)global::System.Math.Min(
                    (ulong)buffer.Length,
                    global::System.Math.Min(remaining, (ulong)(MaxMemoryScanBytes - scannedBytes)));

                if (position > long.MaxValue)
                {
                    break;
                }

                int bytesRead;

                try
                {
                    memory.Seek((long)position, SeekOrigin.Begin);
                    bytesRead = memory.Read(buffer, 0, bytesToRead);
                }
                catch
                {
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                for (int index = 0; index <= bytesRead - sizeof(int) && matches.Count < maxMatches; index++)
                {
                    if (buffer[index] == searchedBytes[0]
                        && buffer[index + 1] == searchedBytes[1]
                        && buffer[index + 2] == searchedBytes[2]
                        && buffer[index + 3] == searchedBytes[3])
                    {
                        ulong address = position + (ulong)index;
                        string addressText = $"0x{address:X}";
                        string? displayName = GetPersistentName("memory", programName, addressText);
                        matches.Add(new MemoryInt32Match(processId, programName, addressText, value, displayName));
                    }
                }

                position += (ulong)bytesRead;
                remaining -= (ulong)bytesRead;
                scannedBytes += bytesRead;
            }

            if (matches.Count >= maxMatches || scannedBytes >= MaxMemoryScanBytes)
            {
                break;
            }
        }

        return matches;
    }

    private static IReadOnlyList<MemoryRegion> ReadLinuxMemoryRegions(string mapsPath)
    {
        List<MemoryRegion> regions = [];

        foreach (string line in File.ReadLines(mapsPath))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || !parts[1].StartsWith("rw", StringComparison.Ordinal))
            {
                continue;
            }

            string[] addresses = parts[0].Split('-');

            if (addresses.Length != 2
                || !ulong.TryParse(addresses[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong start)
                || !ulong.TryParse(addresses[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong end)
                || end <= start)
            {
                continue;
            }

            regions.Add(new MemoryRegion(start, end));
        }

        return regions;
    }

    private static IReadOnlyList<MemorySnapshotBlock> CaptureLinuxMemorySnapshot(int processId)
    {
        string mapsPath = $"/proc/{processId}/maps";
        string memoryPath = $"/proc/{processId}/mem";
        byte[] buffer = new byte[64 * 1024];
        List<MemorySnapshotBlock> blocks = [];
        long scannedBytes = 0;

        using FileStream memory = new(memoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        foreach (MemoryRegion region in ReadLinuxMemoryRegions(mapsPath))
        {
            ulong position = region.Start;
            ulong remaining = region.End - region.Start;

            while (remaining > 0 && scannedBytes < MaxMemoryScanBytes)
            {
                int bytesToRead = (int)global::System.Math.Min(
                    (ulong)buffer.Length,
                    global::System.Math.Min(remaining, (ulong)(MaxMemoryScanBytes - scannedBytes)));

                if (position > long.MaxValue)
                {
                    break;
                }

                int bytesRead;

                try
                {
                    memory.Seek((long)position, SeekOrigin.Begin);
                    bytesRead = memory.Read(buffer, 0, bytesToRead);
                }
                catch
                {
                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                byte[] bytes = new byte[bytesRead];
                Array.Copy(buffer, bytes, bytesRead);
                blocks.Add(new MemorySnapshotBlock(position, bytes));

                position += (ulong)bytesRead;
                remaining -= (ulong)bytesRead;
                scannedBytes += bytesRead;
            }

            if (scannedBytes >= MaxMemoryScanBytes)
            {
                break;
            }
        }

        return blocks;
    }

    private static List<MemoryInt32Match> FindLinuxInt32DeltaCandidates(
        int processId,
        string programName,
        IReadOnlyList<MemorySnapshotBlock> snapshot,
        int delta,
        int maxMatches)
    {
        string memoryPath = $"/proc/{processId}/mem";
        byte[] currentBytes = new byte[64 * 1024];
        List<MemoryInt32Match> matches = [];

        using FileStream memory = new(memoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        foreach (MemorySnapshotBlock block in snapshot)
        {
            if (matches.Count >= maxMatches || block.Start > long.MaxValue)
            {
                break;
            }

            if (currentBytes.Length < block.Bytes.Length)
            {
                currentBytes = new byte[block.Bytes.Length];
            }

            int bytesRead;

            try
            {
                memory.Seek((long)block.Start, SeekOrigin.Begin);
                bytesRead = memory.Read(currentBytes, 0, block.Bytes.Length);
            }
            catch
            {
                continue;
            }

            int comparableBytes = global::System.Math.Min(bytesRead, block.Bytes.Length);

            for (int index = 0; index <= comparableBytes - sizeof(int) && matches.Count < maxMatches; index++)
            {
                int oldValue = BitConverter.ToInt32(block.Bytes, index);
                int newValue = BitConverter.ToInt32(currentBytes, index);

                if (unchecked(newValue - oldValue) != delta)
                {
                    continue;
                }

                ulong address = block.Start + (ulong)index;
                matches.Add(new MemoryInt32Match(processId, programName, $"0x{address:X}", newValue));
            }
        }

        return matches;
    }

    private static List<MemoryInt32Match> FilterLinuxInt32DeltaCandidates(
        int processId,
        IReadOnlyList<MemoryInt32Match> candidates,
        int delta)
    {
        string memoryPath = $"/proc/{processId}/mem";
        byte[] buffer = new byte[sizeof(int)];
        List<MemoryInt32Match> matches = [];

        using FileStream memory = new(memoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        foreach (MemoryInt32Match candidate in candidates)
        {
            if (!TryParseMemoryAddress(candidate.Address, out ulong address) || address > long.MaxValue)
            {
                continue;
            }

            int bytesRead;

            try
            {
                memory.Seek((long)address, SeekOrigin.Begin);
                bytesRead = memory.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                continue;
            }

            if (bytesRead != buffer.Length)
            {
                continue;
            }

            int newValue = BitConverter.ToInt32(buffer, 0);

            if (unchecked(newValue - candidate.Value) == delta)
            {
                matches.Add(candidate with { Value = newValue });
            }
        }

        return matches;
    }

    private static string AskNotEmpty(string question)
    {
        while (true)
        {
            Console.Write(question);
            string? input = Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(input))
            {
                return input.Trim();
            }

            Console.WriteLine("Bitte etwas eingeben.");
        }
    }

    private static string CreateVariableName(string namePrefix, string name)
    {
        string trimmedPrefix = namePrefix.Trim();

        if (trimmedPrefix.Length == 0)
        {
            return name;
        }

        return $"{trimmedPrefix}.{name}";
    }

    private static string ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Der Name darf nicht leer sein.", parameterName);
        }

        return value.Trim();
    }

    private static bool IsAllowedMemoryScanProgramName(string programName)
    {
        string normalizedName = NormalizeProgramName(programName);
        string allowedName = NormalizeProgramName(AllowedMemoryScanProgramName);

        return string.Equals(normalizedName, allowedName, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAllowedMemoryScanProgramName(string programName)
    {
        AllowedMemoryScanProgramName = NormalizeProgramName(programName);
    }

    private static Process? FindAllowedMemoryScanProcess()
    {
        foreach (Process process in Process.GetProcesses().OrderBy(process => process.Id))
        {
            try
            {
                if (IsAllowedMemoryScanProcess(process))
                {
                    return process;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }

            process.Dispose();
        }

        return null;
    }

    private static bool IsAllowedMemoryScanProcess(Process process)
    {
        if (IsAllowedMemoryScanProgramName(process.ProcessName))
        {
            return true;
        }

        string? executableName = GetExecutableName(process);

        return executableName is not null && IsAllowedMemoryScanProgramName(executableName);
    }

    private static string? GetExecutableName(Process process)
    {
        try
        {
            string? fileName = process.MainModule?.FileName;

            return string.IsNullOrWhiteSpace(fileName)
                ? null
                : Path.GetFileNameWithoutExtension(fileName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string NormalizeProgramName(string programName)
    {
        string value = new(
            ValidateName(programName, nameof(programName))
                .Trim('"', '\'', ' ')
                .Where(character => !char.IsControl(character) && char.GetUnicodeCategory(character) != UnicodeCategory.Format)
                .ToArray());

        value = Path.GetFileName(value);

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        return value.Trim();
    }

    private static string NormalizeMemoryAddress(string address)
    {
        string value = ValidateName(address, nameof(address));

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsedAddress))
        {
            throw new ArgumentException("Die Adresse muss ein Hex-Wert sein, z. B. 0x1234ABCD.", nameof(address));
        }

        return $"0x{parsedAddress:X}";
    }

    private static bool TryParseMemoryAddress(string address, out ulong parsedAddress)
    {
        string value = address.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedAddress);
    }

    private static string PersistentNamesPath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mytools-debugger");

            return Path.Combine(folder, "names.txt");
        }
    }

    private static string CreatePersistentKey(string programName, string key)
    {
        return $"{programName}:{key}";
    }

    private static string? GetPersistentName(string kind, string programName, string key)
    {
        Dictionary<string, string> names = GetPersistentDictionary(kind);
        string persistentKey = CreatePersistentKey(programName, key);

        lock (Sync)
        {
            return names.TryGetValue(persistentKey, out string? displayName)
                ? displayName
                : null;
        }
    }

    private static void SetPersistentName(string kind, string programName, string key, string displayName)
    {
        Dictionary<string, string> names = GetPersistentDictionary(kind);
        string persistentKey = CreatePersistentKey(programName, key);

        lock (Sync)
        {
            names[persistentKey] = displayName;
            SavePersistentNames();
        }
    }

    private static void RemovePersistentName(string kind, string programName, string key)
    {
        Dictionary<string, string> names = GetPersistentDictionary(kind);
        string persistentKey = CreatePersistentKey(programName, key);

        lock (Sync)
        {
            names.Remove(persistentKey);
            SavePersistentNames();
        }
    }

    private static Dictionary<string, string> GetPersistentDictionary(string kind)
    {
        return kind switch
        {
            "variable" => PersistentVariableNames,
            "memory" => PersistentMemoryNames,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Unbekannte Namensart.")
        };
    }

    private static Dictionary<string, string> LoadPersistentNames(string kind)
    {
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        string path = PersistentNamesPath;

        if (!File.Exists(path))
        {
            return names;
        }

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split('\t');

            if (parts.Length != 4 || !TextEquals(parts[0], kind))
            {
                continue;
            }

            try
            {
                string programName = DecodePersistentPart(parts[1]);
                string key = DecodePersistentPart(parts[2]);
                string displayName = DecodePersistentPart(parts[3]);

                names[CreatePersistentKey(programName, key)] = displayName;
            }
            catch (FormatException)
            {
            }
        }

        return names;
    }

    private static void SavePersistentNames()
    {
        string path = PersistentNamesPath;
        string? folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        List<string> lines = [];
        AddPersistentNameLines(lines, "variable", PersistentVariableNames);
        AddPersistentNameLines(lines, "memory", PersistentMemoryNames);

        File.WriteAllLines(path, lines);
    }

    private static void AddPersistentNameLines(
        List<string> lines,
        string kind,
        Dictionary<string, string> names)
    {
        foreach (KeyValuePair<string, string> name in names.OrderBy(name => name.Key))
        {
            int separator = name.Key.IndexOf(':');

            if (separator < 0)
            {
                continue;
            }

            string programName = name.Key[..separator];
            string key = name.Key[(separator + 1)..];

            lines.Add(string.Join(
                '\t',
                kind,
                EncodePersistentPart(programName),
                EncodePersistentPart(key),
                EncodePersistentPart(name.Value)));
        }
    }

    private static string EncodePersistentPart(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string DecodePersistentPart(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static bool ContainsText(string value, string text)
    {
        return value.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TextEquals(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvert(string input, Type targetType, out object? value, out string? errorMessage)
    {
        Type realType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (TextEquals(input, "null"))
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
            {
                value = null;
                errorMessage = null;
                return true;
            }

            value = null;
            errorMessage = "null passt nicht zu diesem Datentyp.";
            return false;
        }

        try
        {
            if (realType == typeof(string))
            {
                value = input;
                errorMessage = null;
                return true;
            }

            if (realType == typeof(char))
            {
                if (input.Length == 1)
                {
                    value = input[0];
                    errorMessage = null;
                    return true;
                }

                value = null;
                errorMessage = "Ein char muss genau ein Zeichen lang sein.";
                return false;
            }

            if (realType == typeof(bool))
            {
                if (TryConvertBool(input, out bool boolValue))
                {
                    value = boolValue;
                    errorMessage = null;
                    return true;
                }

                value = null;
                errorMessage = "Der Wert muss true/false, ja/nein oder 1/0 sein.";
                return false;
            }

            if (realType.IsEnum)
            {
                if (Enum.TryParse(realType, input, true, out object? enumValue))
                {
                    value = enumValue;
                    errorMessage = null;
                    return true;
                }

                value = null;
                errorMessage = "Der Wert passt zu keinem Enum-Wert.";
                return false;
            }

            if (realType == typeof(Guid))
            {
                if (Guid.TryParse(input, out Guid guid))
                {
                    value = guid;
                    errorMessage = null;
                    return true;
                }

                value = null;
                errorMessage = "Der Wert ist keine gültige Guid.";
                return false;
            }

            TypeConverter converter = TypeDescriptor.GetConverter(realType);

            if (converter.CanConvertFrom(typeof(string)))
            {
                value = converter.ConvertFrom(null, CultureInfo.CurrentCulture, input);
                errorMessage = null;
                return true;
            }

            value = Convert.ChangeType(input, realType, CultureInfo.CurrentCulture);
            errorMessage = null;
            return true;
        }
        catch
        {
            try
            {
                value = Convert.ChangeType(input, realType, CultureInfo.InvariantCulture);
                errorMessage = null;
                return true;
            }
            catch
            {
                value = null;
                errorMessage = "Der Wert passt nicht zum Datentyp der Variable.";
                return false;
            }
        }
    }

    private static bool TryConvertBool(string input, out bool value)
    {
        switch (input.Trim().ToLowerInvariant())
        {
            case "true":
            case "wahr":
            case "ja":
            case "j":
            case "1":
                value = true;
                return true;

            case "false":
            case "falsch":
            case "nein":
            case "n":
            case "0":
                value = false;
                return true;

            default:
                value = false;
                return false;
        }
    }

    private sealed class DebugProgram(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, DebugVariable> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> VariableNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct MemoryRegion(ulong Start, ulong End);

    private sealed record MemorySnapshotBlock(ulong Start, byte[] Bytes);

    private enum MemorySequenceDirection
    {
        Ascending,
        Descending
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, nint arg2, nint arg3, nint arg4, nint arg5);

    private sealed class DebugVariable(
        string name,
        Type type,
        Func<object?> getValue,
        Action<object?>? setValue)
    {
        public string Name { get; } = name;

        public Type Type { get; } = type;

        public bool CanChange => setValue is not null;

        public ProgramVariableInfo CreateInfo(string programName, string? displayName)
        {
            object? value;

            try
            {
                value = getValue();
            }
            catch (Exception exception)
            {
                value = $"<Fehler: {exception.Message}>";
            }

            return new ProgramVariableInfo(programName, Name, GetTypeName(Type), value, CanChange, displayName);
        }

        public void SetValue(object? value)
        {
            if (setValue is null)
            {
                throw new InvalidOperationException("Diese Variable kann nur gelesen werden.");
            }

            setValue(value);
        }
    }

    private static string GetTypeName(Type type)
    {
        Type? nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType is not null)
        {
            return $"{GetTypeName(nullableType)}?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        int genericMarker = type.Name.IndexOf('`');
        string name = genericMarker < 0
            ? type.Name
            : type.Name[..genericMarker];
        string arguments = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));

        return $"{name}<{arguments}>";
    }
}
