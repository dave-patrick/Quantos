using System.Text.Json;

namespace Quantos.Core.Storage;

public enum StepKind { Write, Move, Delete }

public sealed class JournalStep
{
    public StepKind Kind { get; set; }
    public string Path { get; set; } = "";           // vault-relative
    public string? To { get; set; }                  // move destination, vault-relative
    public string? BeforeHash { get; set; }          // null: file did not exist
    public string? AfterHash { get; set; }
    public string? Before { get; set; }              // previous content, for rollback
    public string? After { get; set; }               // new content, for completion
    public bool Done { get; set; }

    public static JournalStep Write(string path, string? before, string after) => new()
    {
        Kind = StepKind.Write, Path = path, Before = before, After = after,
        BeforeHash = before is null ? null : Hashing.Sha256(before), AfterHash = Hashing.Sha256(after)
    };

    public static JournalStep Move(string from, string to) => new() { Kind = StepKind.Move, Path = from, To = to };

    public static JournalStep Delete(string path, string before) => new()
    {
        Kind = StepKind.Delete, Path = path, Before = before, BeforeHash = Hashing.Sha256(before)
    };
}

public sealed class JournalOperation
{
    public string OpId { get; set; } = Ulid.New();
    public string Description { get; set; } = "";
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public List<JournalStep> Steps { get; set; } = [];
}

public sealed record RecoveryResult(string OpId, string Description, string Outcome, IReadOnlyList<string> Problems);

/// <summary>
/// Recoverable multi-file operations (BUILD-PLAN section 6). The plan is written before any file
/// changes; each step is marked done as it completes. Recovery checks the current version of
/// every affected file before completing or undoing an interrupted operation.
/// </summary>
public sealed class Journal(Vault vault)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Called with the absolute path before a file is overwritten or moved, so history can be saved.</summary>
    public Action<string>? BeforeChange { get; set; }

    /// <summary>Test hook: throw after this many steps to simulate a crash.</summary>
    public int? CrashAfterSteps { get; set; }

    public void Execute(JournalOperation op)
    {
        Directory.CreateDirectory(vault.Journal);
        var file = Path.Combine(vault.Journal, op.OpId + ".json");
        Persist(file, op);
        var completed = 0;
        foreach (var step in op.Steps)
        {
            if (CrashAfterSteps is { } limit && completed >= limit) throw new SimulatedCrashException();
            Apply(step);
            step.Done = true;
            Persist(file, op);
            completed++;
        }
        File.Delete(file);
    }

    private void Apply(JournalStep step)
    {
        var path = vault.Absolute(step.Path);
        switch (step.Kind)
        {
            case StepKind.Write:
                if (File.Exists(path)) BeforeChange?.Invoke(path);
                AtomicFile.WriteAllText(path, step.After!);
                break;
            case StepKind.Move:
                AtomicFile.Move(path, vault.Absolute(step.To!));
                break;
            case StepKind.Delete:
                if (File.Exists(path)) { BeforeChange?.Invoke(path); File.Delete(path); }
                break;
        }
    }

    private static void Persist(string file, JournalOperation op) => AtomicFile.WriteAllText(file, JsonSerializer.Serialize(op, Json));

    public bool HasPending => Directory.Exists(vault.Journal) && Directory.EnumerateFiles(vault.Journal, "*.json").Any();

    public IReadOnlyList<RecoveryResult> Recover()
    {
        var results = new List<RecoveryResult>();
        if (!Directory.Exists(vault.Journal)) return results;
        foreach (var file in Directory.EnumerateFiles(vault.Journal, "*.json").Order().ToList())
        {
            JournalOperation? op;
            try { op = JsonSerializer.Deserialize<JournalOperation>(AtomicFile.ReadAllText(file), Json); }
            catch (JsonException) { op = null; }
            if (op is null)
            {
                File.Move(file, file + ".unreadable", overwrite: true);
                results.Add(new RecoveryResult(Path.GetFileNameWithoutExtension(file), "unreadable journal", "left untouched", ["Journal file could not be read."]));
                continue;
            }
            results.Add(RecoverOne(file, op));
        }
        return results;
    }

    private RecoveryResult RecoverOne(string file, JournalOperation op)
    {
        // Classify each pending step against the files as they are now.
        var forwardable = true;
        foreach (var step in op.Steps.Where(s => !s.Done))
        {
            switch (State(step))
            {
                case StepState.AlreadyApplied: step.Done = true; break;
                case StepState.Ready: break;
                default: forwardable = false; break;
            }
        }

        var problems = new List<string>();
        if (forwardable)
        {
            foreach (var step in op.Steps.Where(s => !s.Done))
            {
                Apply(step);
                step.Done = true;
                Persist(file, op);
            }
            File.Delete(file);
            return new RecoveryResult(op.OpId, op.Description, "completed", problems);
        }

        // Roll back completed steps, newest first, only where the file is still exactly as we left it.
        foreach (var step in op.Steps.Where(s => s.Done).Reverse())
        {
            var path = vault.Absolute(step.Path);
            switch (step.Kind)
            {
                case StepKind.Write:
                    if (Hashing.FileHash(path) != step.AfterHash) { problems.Add($"{step.Path} changed since the interrupted write; left as is."); break; }
                    BeforeChange?.Invoke(path);
                    if (step.Before is null) File.Delete(path); else AtomicFile.WriteAllText(path, step.Before);
                    break;
                case StepKind.Move:
                    var to = vault.Absolute(step.To!);
                    if (File.Exists(to) && !File.Exists(path)) AtomicFile.Move(to, path);
                    else problems.Add($"Couldn't move {step.To} back to {step.Path}; left as is.");
                    break;
                case StepKind.Delete:
                    if (!File.Exists(path) && step.Before is not null) AtomicFile.WriteAllText(path, step.Before);
                    break;
            }
        }
        var rollbackFailed = problems.Count > 0;
        foreach (var step in op.Steps.Where(s => !s.Done && State(s) == StepState.Conflict))
            problems.Add($"{step.Path} was changed by something else before the interrupted step reached it; that change was kept.");

        if (!rollbackFailed) File.Delete(file);
        else File.Move(file, file + ".unresolved", overwrite: true);
        return new RecoveryResult(op.OpId, op.Description, rollbackFailed ? "partly rolled back" : "rolled back", problems);
    }

    private enum StepState { Ready, AlreadyApplied, Conflict }

    private StepState State(JournalStep step)
    {
        var path = vault.Absolute(step.Path);
        switch (step.Kind)
        {
            case StepKind.Write:
                var hash = Hashing.FileHash(path);
                if (hash == step.AfterHash) return StepState.AlreadyApplied;
                return hash == step.BeforeHash ? StepState.Ready : StepState.Conflict;
            case StepKind.Move:
                var to = vault.Absolute(step.To!);
                if (File.Exists(to) && !File.Exists(path)) return StepState.AlreadyApplied;
                return File.Exists(path) && !File.Exists(to) ? StepState.Ready : StepState.Conflict;
            case StepKind.Delete:
                if (!File.Exists(path)) return StepState.AlreadyApplied;
                return Hashing.FileHash(path) == step.BeforeHash ? StepState.Ready : StepState.Conflict;
        }
        return StepState.Conflict;
    }
}

public sealed class SimulatedCrashException() : Exception("Simulated crash (test hook).");
