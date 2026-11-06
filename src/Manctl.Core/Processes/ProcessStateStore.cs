using System.Text.Json;

namespace Manctl.Core.Processes;

/// <summary>Durable record of everything manctl has launched, so a stale lock file left behind by a crashed or
/// force-killed process gets cleaned up on the next start instead of breaking the next launch (including a manual
/// one by the user, since this file is human-readable JSON).</summary>
public sealed class ProcessStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "manctl", "managed-processes.json");

    private readonly object _gate = new();

    public ProcessRecord? Get(string profileName)
    {
        lock (_gate)
        {
            return LoadAll().GetValueOrDefault(profileName);
        }
    }

    public List<ProcessRecord> GetAll()
    {
        lock (_gate)
        {
            return [.. LoadAll().Values];
        }
    }

    public void Upsert(ProcessRecord record)
    {
        lock (_gate)
        {
            var all = LoadAll();
            all[record.ProfileName] = record;
            SaveAll(all);
        }
    }

    public void Remove(string profileName)
    {
        lock (_gate)
        {
            var all = LoadAll();
            if (all.Remove(profileName))
                SaveAll(all);
        }
    }

    private static Dictionary<string, ProcessRecord> LoadAll()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, ProcessRecord>>(json) ?? [];
        }
        catch
        {
            // Corrupt/partial state file (e.g. process was killed mid-write) — start clean rather than crash.
            return [];
        }
    }

    private static void SaveAll(Dictionary<string, ProcessRecord> all)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        // Write to a temp file then move, so a crash mid-write never leaves a half-written, unparsable state file.
        var tmpPath = FilePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmpPath, FilePath, overwrite: true);
    }
}
