
using Brutal.Logging;
using KSA;
using System;
using System.Collections.Generic;
using System.IO;
using XPP.Doc;
using XPP.Patch;

namespace KittenExtensions.Patch;

internal sealed class PatchLogger : IDisposable
{
    private const string LogFile = "KittenExtensions.log";
    private const string LogDir = @"..\Logs";
    public static string LogPath => Path.Combine(ModLibrary.LocalModsFolderPath, LogDir, LogFile);
    private readonly StreamWriter writer;
    private readonly Dictionary<string, int> appliedByFile = [];
    private readonly Dictionary<string, int> failedByFile = [];
    private readonly Dictionary<PatchAction, MutationSnapshot> mutations = [];

    private PatchLogger(StreamWriter writer)
    {
        this.writer = writer;
        writer.WriteLine();
        writer.WriteLine($"[{DateTimeOffset.UtcNow:O}] KittenExtensions patch session started");
    }

    public static PatchLogger? Create(PatchExecutor executor)
    {
        try
        {
            var path = LogPath;
            Console.WriteLine(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var logger = new PatchLogger(new StreamWriter(path, append: true) { AutoFlush = true });
            executor.ActionStarted += logger.OnActionStarted;
            executor.ActionCompleted += logger.OnActionCompleted;
            executor.ActionFailed += logger.OnActionFailed;
            return logger;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"KittenExtensions: unable to create patch log: {ex.Message}");
            return null;
        }
    }

    public void WriteSummary()
    {
        try
        {
            writer.WriteLine($"[{DateTimeOffset.UtcNow:O}] Patch session summary");
            foreach (var (file, applied) in appliedByFile)
            {
                failedByFile.TryGetValue(file, out var failed);
                writer.WriteLine($"summary source={file} applied={applied} failed={failed}");
                DefaultCategory.Log.Info($"KittenExtensions: {file}: {applied} patch operations applied{(failed > 0 ? $", {failed} failed" : "")}.");
            }
            foreach (var (file, failed) in failedByFile)
            {
                if (appliedByFile.ContainsKey(file))
                    continue;

                writer.WriteLine($"summary source={file} applied=0 failed={failed}");
                DefaultCategory.Log.Warning($"KittenExtensions: {file}: 0 patch operations applied, {failed} failed.");
            }
        }
        catch
        {
        }
    }

    private void OnActionCompleted(PatchAction action)
    {
        try
        {
            if (IsPatchOperation(action.Type))
                AddCount(appliedByFile, GetLocation(action.Patch).File);

            if (mutations.Remove(action, out var mutation))
                WriteMutation("applied", mutation);
        }
        catch
        {
        }
    }

    private void OnActionFailed(PatchAction action)
    {
        try
        {
            if (IsPatchOperation(action.Type))
                AddCount(failedByFile, GetLocation(action.Patch).File);

            if (mutations.Remove(action, out var mutation))
                WriteMutation("failed", mutation, action.Error?.ToString());
        }
        catch
        {
        }
    }

    private void OnActionStarted(PatchAction action)
    {
        if (!IsMutation(action.Type))
            return;

        try
        {
            mutations[action] = new(
              action.Type,
              GetLocation(action.Patch),
              GetLocation(action.Target),
              action.Target.DebugName,
              action.Position,
              GetOldValue(action),
              GetNewValue(action));
        }
        catch
        {
        }
    }

    private void WriteMutation(string outcome, MutationSnapshot mutation, string? error = null)
    {
        writer.Write(
          $"{outcome} type={mutation.Type} mod={Quote(mutation.Source.Mod)} " +
          $"source={Quote(mutation.Source.File)} targetFile={Quote(mutation.Target.File)} " +
          $"targetMod={Quote(mutation.Target.Mod)} targetObject={Quote(mutation.TargetObject)} position={mutation.Position} " +
          $"oldValue={Quote(mutation.OldValue)} newValue={Quote(mutation.NewValue)}");
        if (!string.IsNullOrEmpty(error))
            writer.Write($" error={Quote(error)}");
        writer.WriteLine();
    }

    private static bool IsPatchOperation(ActionType type) => type is
      ActionType.OpCopy or ActionType.OpMerge or ActionType.OpDelete or
      ActionType.OpIf or ActionType.OpIfAny or ActionType.OpIfNone or
      ActionType.OpWith or ActionType.OpSetVar;

    private static bool IsMutation(ActionType type) => type is
      ActionType.Set or ActionType.Insert or ActionType.InsertText or ActionType.Remove;

    private static string GetOldValue(PatchAction action) => action.Type switch
    {
        ActionType.Set => action.Target.Value,
        ActionType.Remove => action.Target.ToString(),
        _ => "<none>",
    };

    private static string GetNewValue(PatchAction action) => action.Type switch
    {
        ActionType.Set or ActionType.InsertText =>
          action.SourceResult.Count > 0 ? action.SourceResult[0].StringValue : "<none>",
        ActionType.Insert => action.Source.ToString(),
        ActionType.Remove => "<removed>",
        _ => "<none>",
    };

    private static SourceLocation GetLocation(XPNodeRef node)
    {
        for (var depth = 0; node.Valid && depth < 100; depth++, node = node.Parent)
        {
            var file = node.Attribute("RelPath");
            if (file.Valid)
            {
                var mod = node.Parent.Attribute("Id");
                return new(file.Value, mod.Valid ? mod.Value : "<unknown>");
            }
        }

        return new("<unknown>", "<unknown>");
    }

    private static void AddCount(Dictionary<string, int> counts, string file) =>
      counts[file] = counts.GetValueOrDefault(file) + 1;

    private static string Quote(string? value)
    {
        value ??= "<none>";
        value = value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
        return $"\"{(value.Length > 4096 ? value[..4096] + "…" : value)}\"";
    }

    private record SourceLocation(string File, string Mod);
    private record MutationSnapshot(
      ActionType Type,
      SourceLocation Source,
      SourceLocation Target,
      string TargetObject,
      PatchPosition Position,
      string OldValue,
      string NewValue);

    public void Dispose() => writer.Dispose();
}
