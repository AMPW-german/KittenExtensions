
using Brutal.Logging;
using Core.Files.Paths;
using KSA;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Tomlet;
using Tomlet.Attributes;
using XPP.Doc;
using XPP.Patch;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
    private static readonly PatchDomain Domain = new();
    private static PatchExecutor Executor;
  private static PatchLogger? Logger;

    private static readonly Dictionary<string, int> ModIndexByDir = [];
    private static readonly Dictionary<string, string[]> AssetPathsByDir = [];
    private static readonly Dictionary<string, PatchDomain.PatchMod> PatchModByDir = [];
    private static readonly XPName AssetsElName = new("", "", "Assets");

    private static string DirKey(string dirPath) =>
        PatchDomain.NormalizePath(Path.GetFullPath(dirPath));

    public static void OnContentSource(ContentSource source)
    {
        if (Executor == null)
            return;
        try
        {
            RebuildRows(source);
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Error(ex);
        }
    }

    private static void RebuildRows(ContentSource source)
    {
        var rows = new List<XElement>(source.Rows.Count);
        var originalByMod = source.Rows
            .GroupBy(row => (string)row.Attribute("__Mod") ?? "")
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var mod in source.Mods)
        {
            var dirKey = DirKey(mod.DirectoryPath);
            var modIndex = ModIndexByDir.GetValueOrDefault(dirKey, -1);
            if (!PatchModByDir.TryGetValue(dirKey, out var patchMod) || !patchMod.Node.Valid)
            {
                if (originalByMod.TryGetValue(mod.Id, out var original))
                    rows.AddRange(original);
                continue;
            }

            var fileIndexByPath = BuildFileIndex(AssetPathsByDir.GetValueOrDefault(dirKey, []));
            foreach (var file in patchMod.ImportedFiles())
            {
                if (IsAssetFile(file))
                    EmitRows(file, mod.Id, modIndex, fileIndexByPath, rows);
            }
        }

        source.Rows.Clear();
        source.Rows.AddRange(rows);
    }

    private static bool IsAssetFile(XPNodeRef node) =>
        node.Type is XPType.Element && node.Name == AssetsElName;

    private static void EmitRows(
        XPNodeRef fileNode, string modId, int modIndex,
        Dictionary<string, Queue<int>> fileIndexByPath, List<XElement> rows)
    {
        var relAttr = fileNode.Attribute("RelPath");
        var relPath = relAttr.Valid ? relAttr.Value : "";
        var fileIndex = NextFileIndex(fileIndexByPath, relPath);
        var rowIndex = 0;
        for (var row = fileNode.FirstContent; row.Valid; row = row.NextSibling)
        {
            if (row.Type is not XPType.Element)
                continue;
            var element = row.ToXElement();
            ContentStamps.Stamp(element, modId, modIndex, relPath, fileIndex, rowIndex++);
            rows.Add(element);
        }
    }

    private static Dictionary<string, Queue<int>> BuildFileIndex(string[] assetPaths)
    {
        var map = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var i = 0; i < assetPaths.Length; i++)
        {
            var path = assetPaths[i];
            if (!map.TryGetValue(path, out var queue))
                map[path] = queue = new Queue<int>();
            queue.Enqueue(i);
        }
        return map;
    }

    private static int NextFileIndex(Dictionary<string, Queue<int>> map, string relPath) =>
        map.TryGetValue(relPath, out var queue) && queue.Count > 0
            ? queue.Dequeue()
            : -1;

    public static void OnPrepare()
    {
        LoadData();

        PatchDebugPopup debugPopup = null;

        try
        {
            if (DebugEnabled())
            {
                var doc = new XmlDocument();
                doc.Load(new XPDocReader(Domain.Doc.LatestRoot));
                doc.Save(Path.Combine(Constants.DocumentsFolderPath, "root.xml"));
                var patchTask = new PopupTask(debugPopup = new PatchDebugPopup());
                while (patchTask.Show)
                    patchTask.OnFrame();
        CompleteLogging();
            }
            else
                RunPatches();
        }
        catch (Exception ex)
        {
            debugPopup?.Active = false;
      CompleteLogging();
            throw ErrorAndExit(ex.ToString(), Executor?.Next?.Patch ?? XPNodeRef.Invalid);
        }
    }

    private static void RunPatches()
    {
        try
        {
            Executor.StepToEnd();
            if (Executor.HasError)
      {
        CompleteLogging();
                throw ErrorAndExit(Executor.Next.Error.ToString(), Executor.Next.Patch);
      }
      CompleteLogging();
        }
        catch (Exception ex)
        {
      CompleteLogging();
            throw ErrorAndExit(ex.ToString(), Executor?.Next?.Patch ?? XPNodeRef.Invalid);
        }
    }

    private static void LoadData()
    {
        ModIndexByDir.Clear();
        AssetPathsByDir.Clear();
        PatchModByDir.Clear();
        var mods = ModLibrary.Manifest.Mods;
        for (var i = 0; i < mods.Count; i++)
            LoadModData(mods[i], i);
        Executor = Domain.Executor();
    Logger = PatchLogger.Create(Executor);
    }

  private static void CompleteLogging()
  {
    var logger = Logger;
    Logger = null;
    if (logger == null)
      return;

    logger.WriteSummary();
    logger.Dispose();
  }

    private static Exception ErrorAndExit(string error, XPNodeRef elementLoc = default, string strLoc = null)
    {
        Console.Error.WriteLine(error);
        var errTask = new PopupTask(new ErrorPopup(error, elementLoc, strLoc));
        while (errTask.Show)
            errTask.OnFrame();
        Environment.Exit(1);
        throw new InvalidOperationException();
    }

    private static void LoadModData(ModEntry modEntry, int modIndex)
    {
        var tomlPath = Path.Combine("Content", modEntry.Id, "mod.toml");
        if (!File.Exists(tomlPath))
        {
            tomlPath = Path.Combine(ModLibrary.LocalModsFolderPath, modEntry.Id, "mod.toml");
        }
        var dirPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.GetDirectoryName(tomlPath) ?? ""));
        ModToml mod;
        try
        {
            mod = TomletMain.To<ModToml>(File.ReadAllText(tomlPath));
        }
        catch (Exception ex)
        {
            throw ErrorAndExit(ex.ToString(), strLoc: Path.GetFullPath(tomlPath));
        }

        var dirKey = DirKey(dirPath);
        ModIndexByDir.TryAdd(dirKey, modIndex);
        AssetPathsByDir[dirKey] = mod.Assets;

        var patchMod = Domain.AddMod(modEntry.Id);
        PatchModByDir[dirKey] = patchMod;
        patchMod.Node.SetAttribute("Name", mod.Name);
        patchMod.Node.SetAttribute("Path", PatchDomain.NormalizePath(dirPath));

        foreach (var path in mod.Assets)
            LoadModFile(patchMod, dirPath, path);
        foreach (var path in mod.Patches)
            LoadModFile(patchMod, dirPath, path);
    }

    private static void LoadModFile(PatchDomain.PatchMod mod, string dirPath, string path)
    {
        var fullPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.Combine(dirPath, path)));
        try
        {
            var fileElement = mod.ImportFile(fullPath);
            fileElement.SetAttribute("RelPath", path);
        }
        catch (Exception ex)
        {
            ErrorAndExit(ex.ToString(), strLoc: fullPath);
        }
    }

    private class ModToml
    {
        [TomlField("name")]
        public string Name = "";
        [TomlField("assets")]
        public string[] Assets = [];
        [TomlField("patches")]
        public string[] Patches = [];
    }

    private static bool? debug = null;
    public static bool DebugEnabled()
    {
        if (debug.HasValue)
            return debug.Value;
        try
        {
            var manifest = TomletMain.To<ModManifestToml>(File.ReadAllText(ModLibrary.LocalManifestPath));
            debug = manifest.Mods.FirstOrDefault(mod => mod.Id == "KittenExtensionsContinued" && mod.Enabled)?.Debug ?? false;
            return debug.Value;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private class ModManifestToml
    {
        [TomlDoNotInlineObject]
        [TomlField("mods")]
        public List<ModEntryToml> Mods = [];
    }

    private class ModEntryToml
    {
        [TomlField("id")]
        public string Id = "";
        [TomlField("enabled")]
        public bool Enabled = true;
        [TomlField("debug")]
        public bool Debug = false;
    }
}

public static partial class Extensions
{
    public static List<XmlNode> ToNodeList(this XPathNodeIterator iter)
    {
        var list = new List<XmlNode>(iter.Count);
        while (iter.MoveNext())
            list.Add((XmlNode)iter.Current.UnderlyingObject);
        return list;
    }
    public static List<XPathNavigator> ToNavList(this XPathNodeIterator iter)
    {
        var list = new List<XPathNavigator>(iter.Count);
        while (iter.MoveNext())
            list.Add(iter.Current.Clone());
        return list;
    }
}