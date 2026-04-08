using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Linq;

namespace MitHelper.Data;

public class MitSheetLoader
{
    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public MitSheetLoader(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi  = pi;
        _log = log;
    }

    // Load every sheet found across all search directories
    public List<MitSheet> LoadAllSheets()
    {
        var candidates = new List<(DateTime Modified, MitSheet Sheet, string Path)>();

        foreach (var dir in GetSearchDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json  = File.ReadAllText(file);
                    var sheet = JsonSerializer.Deserialize<MitSheet>(json, JsonOptions);
                    if (sheet != null)
                        candidates.Add((File.GetLastWriteTime(file), sheet, file));
                }
                catch (Exception ex)
                {
                    _log.Warning($"[MitHelper] Skipped {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        candidates.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return candidates.Select(c => c.Sheet).ToList();
    }

    //Check the directories
    public List<MitSheet> LoadSheetsForDuty(uint dutyId)
    {
        var candidates = new List<(DateTime Modified, MitSheet Sheet, string Path)>();

        foreach (var dir in GetSearchDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json  = File.ReadAllText(file);
                    var sheet = JsonSerializer.Deserialize<MitSheet>(json, JsonOptions);
                    if (sheet != null && sheet.Duty == dutyId)
                        candidates.Add((File.GetLastWriteTime(file), sheet, file));
                }
                catch (Exception ex)
                {
                    _log.Warning($"[MitHelper] Skipped {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        candidates.Sort((a, b) => b.Modified.CompareTo(a.Modified));

        var results = new List<MitSheet>();
        foreach (var (_, sheet, path) in candidates)
        {
            _log.Debug($"[MitHelper] Loaded sheet '{sheet.Name}' from {path}");
            results.Add(sheet);
        }

        return results;
    }

    /// All directories to search
    private IEnumerable<string> GetSearchDirectories()
    {
        var myDir = _pi.AssemblyLocation.Directory?.FullName;
        if (myDir == null) yield break;
        yield return myDir;
    }

    /// Expose all JSON file paths across search dirs — used by the editor
    public IEnumerable<string> GetAllJsonFiles()
    {
        foreach (var dir in GetSearchDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
                yield return file;
        }
    }
}
