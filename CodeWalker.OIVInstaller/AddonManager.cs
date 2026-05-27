using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// Manages user-installed DLC add-ons living under
    /// <c>&lt;gameFolder&gt;\mods\update\x64\dlcpacks\</c>. Their enabled / disabled
    /// state is determined by whether their folder name appears as a
    /// <c>&lt;Item&gt;dlcpacks:/name/&lt;/Item&gt;</c> entry inside
    /// <c>mods\update\update.rpf\common\data\dlclist.xml</c>.
    ///
    /// Disabling an add-on is non-destructive — only the dlclist entry is removed,
    /// the folder stays in place so re-enabling is a single click. Stock Rockstar
    /// DLCs are reported as read-only so they can be displayed but not toggled.
    /// </summary>
    public class AddonManager
    {
        public class AddonInfo
        {
            public string Name { get; set; } = "";
            public string FolderPath { get; set; } = "";
            public bool IsEnabled { get; set; }
            public bool IsStockDLC { get; set; }
            // Orphan = listed in dlclist but no matching folder on disk.
            public bool FolderExists { get; set; } = true;
        }

        private readonly string _gameFolder;

        public AddonManager(string gameFolder)
        {
            _gameFolder = gameFolder ?? throw new ArgumentNullException(nameof(gameFolder));
        }

        public string DlcpacksFolder => Path.Combine(_gameFolder, "mods", "update", "x64", "dlcpacks");
        public string UpdateRpfPath => Path.Combine(_gameFolder, "mods", "update", "update.rpf");
        public bool ModsFolderExists =>
            Directory.Exists(Path.Combine(_gameFolder, "mods"))
            && File.Exists(UpdateRpfPath);

        /// <summary>
        /// Lists every add-on present either on disk under dlcpacks\ or referenced
        /// in dlclist.xml. User add-ons sort first (alphabetically), then any
        /// stock DLCs, then orphan dlclist entries last.
        /// </summary>
        public List<AddonInfo> ListAddons()
        {
            var result = new List<AddonInfo>();
            var enabledNames = ReadEnabledNames();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(DlcpacksFolder))
            {
                foreach (var dir in Directory.GetDirectories(DlcpacksFolder))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    seen.Add(name);
                    result.Add(new AddonInfo
                    {
                        Name = name,
                        FolderPath = dir,
                        IsEnabled = enabledNames.Contains(name),
                        IsStockDLC = StockDLCs.Contains(name),
                        FolderExists = true,
                    });
                }
            }

            // Orphans — listed in dlclist but no folder on disk.
            foreach (var name in enabledNames)
            {
                if (seen.Contains(name)) continue;
                result.Add(new AddonInfo
                {
                    Name = name,
                    FolderPath = "",
                    IsEnabled = true,
                    IsStockDLC = StockDLCs.Contains(name),
                    FolderExists = false,
                });
            }

            return result
                .OrderBy(a => a.IsStockDLC ? 1 : (a.FolderExists ? 0 : 2))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Adds or removes <c>&lt;Item&gt;dlcpacks:/{addonName}/&lt;/Item&gt;</c> in
        /// dlclist.xml inside update.rpf. Returns true if the file actually
        /// changed; false if the requested state was already in place.
        /// Refuses to toggle Rockstar stock DLCs.
        /// </summary>
        public bool SetEnabled(string addonName, bool enable)
        {
            if (string.IsNullOrWhiteSpace(addonName))
                throw new ArgumentException("Add-on name is empty.", nameof(addonName));
            if (StockDLCs.Contains(addonName))
                throw new InvalidOperationException($"'{addonName}' is a Rockstar stock DLC and cannot be toggled.");
            if (!ModsFolderExists)
                throw new InvalidOperationException("mods\\update\\update.rpf was not found. Set up your mods folder first.");

            // Lazy-load keys if they haven't been loaded yet (e.g. caller opened the
            // form without going through MainForm's install flow first).
            EnsureKeysLoaded();

            // The read + edit + write is wrapped in a retry loop so transient locks
            // (antivirus / Windows Search scanning update.rpf in the moment after our
            // previous write completed) don't surface as a user-visible error. Each
            // attempt re-opens the RPF from scratch since a previous failure could
            // have left internal state stale.
            return WithIoRetry(() =>
            {
                var rpf = new RpfFile(UpdateRpfPath, "mods\\update\\update.rpf");
                rpf.ScanStructure(null, _ => { });

                var fileEntry = FindDlclistEntry(rpf);
                if (fileEntry == null)
                    throw new FileNotFoundException("dlclist.xml not found inside update.rpf.");

                byte[] data = fileEntry.File.ExtractFile(fileEntry);
                string xmlText = TrimBom(Encoding.UTF8.GetString(data));

                var doc = new XmlDocument { PreserveWhitespace = true };
                doc.LoadXml(xmlText);

                var paths = doc.SelectSingleNode("/SMandatoryPacksData/Paths");
                if (paths == null)
                    throw new InvalidDataException("dlclist.xml has no <Paths> node.");

                string targetText = $"dlcpacks:/{addonName}/";
                var existing = FindEntryNode(paths, addonName);

                if (enable)
                {
                    if (existing != null) return false; // already enabled
                    var item = doc.CreateElement("Item");
                    item.InnerText = targetText;
                    AppendWithIndentation(paths, item);
                }
                else
                {
                    if (existing == null) return false; // already disabled
                    RemoveWithSurroundingWhitespace(paths, existing);
                }

                byte[] newData;
                using (var sw = new StringWriterWithEncoding(Encoding.UTF8))
                {
                    doc.Save(sw);
                    newData = Encoding.UTF8.GetBytes(sw.ToString());
                }

                var dir = (RpfDirectoryEntry)fileEntry.Parent;
                RpfFile.CreateFile(dir, fileEntry.Name, newData, overwrite: true);
                return true;
            });
        }

        // Retries a delegate on Win32 ERROR_SHARING_VIOLATION (HResult == 0x80070020)
        // with exponential-ish backoff. update.rpf can briefly lock under antivirus /
        // Windows Search right after we write to it, and the next read/write would
        // otherwise fail with a "file in use" error. ~2s total ceiling so the UI
        // doesn't appear hung. Non-sharing IOExceptions (FileNotFound, etc.) bubble
        // up immediately — retry won't help those.
        private static T WithIoRetry<T>(Func<T> body, int maxAttempts = 6)
        {
            const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
            int delayMs = 80;
            for (int attempt = 1; ; attempt++)
            {
                try { return body(); }
                catch (IOException ex) when (attempt < maxAttempts && ex.HResult == ERROR_SHARING_VIOLATION)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(delayMs);
                    delayMs = Math.Min(delayMs * 2, 600);
                }
            }
        }

        public bool AddonFolderExists(string addonName)
        {
            if (string.IsNullOrWhiteSpace(addonName)) return false;
            return Directory.Exists(Path.Combine(DlcpacksFolder, addonName));
        }

        public static bool IsStockName(string name) => StockDLCs.Contains(name);

        public static bool IsValidAddonName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) { error = "Name is empty."; return false; }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { error = "Name contains invalid characters."; return false; }
            if (name.Contains("/") || name.Contains("\\"))
            { error = "Name cannot contain path separators."; return false; }
            if (StockDLCs.Contains(name))
            { error = $"'{name}' is a Rockstar stock DLC name — pick a different name."; return false; }
            return true;
        }

        /// <summary>
        /// Installs a DLC add-on. <paramref name="sourcePath"/> may be either a folder
        /// containing dlc.rpf (its contents are copied) or a bare dlc.rpf file (copied
        /// into the new <paramref name="addonName"/> folder). After the copy succeeds,
        /// the add-on is enabled in dlclist.xml.
        /// </summary>
        public void InstallAddon(string sourcePath, string addonName, bool overwriteIfExists, IProgress<string> progress = null)
        {
            if (!IsValidAddonName(addonName, out string err))
                throw new InvalidOperationException(err);
            if (!ModsFolderExists)
                throw new InvalidOperationException("mods\\update\\update.rpf was not found. Set up your mods folder first.");

            string dest = Path.Combine(DlcpacksFolder, addonName);
            if (Directory.Exists(dest))
            {
                if (!overwriteIfExists)
                    throw new IOException($"Add-on folder already exists: {dest}");
                progress?.Report("Removing existing folder…");
                Directory.Delete(dest, recursive: true);
            }

            Directory.CreateDirectory(dest);

            if (Directory.Exists(sourcePath))
            {
                progress?.Report("Copying add-on files…");
                CopyDirectory(sourcePath, dest);
            }
            else if (File.Exists(sourcePath))
            {
                progress?.Report($"Copying {Path.GetFileName(sourcePath)}…");
                File.Copy(sourcePath, Path.Combine(dest, Path.GetFileName(sourcePath)), overwrite: true);
            }
            else
            {
                throw new FileNotFoundException("Source path does not exist.", sourcePath);
            }

            progress?.Report("Enabling in dlclist.xml…");
            SetEnabled(addonName, true);
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var sub in Directory.GetDirectories(src))
                CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
        }

        // -- internals -------------------------------------------------------

        /// <summary>
        /// Last exception encountered while reading dlclist.xml, if any. Cleared on
        /// each successful <see cref="ListAddons"/> call. UI uses this to show a
        /// real error instead of silently rendering "everything disabled".
        /// </summary>
        public Exception LastReadError { get; private set; }

        private HashSet<string> ReadEnabledNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LastReadError = null;
            if (!ModsFolderExists) return set;

            try
            {
                // Same retry envelope as SetEnabled — update.rpf can be transiently
                // locked by antivirus / Windows Search right after a previous write,
                // and the read would otherwise silently return an empty set and make
                // every add-on appear disabled.
                WithIoRetry(() =>
                {
                    EnsureKeysLoaded();
                    var rpf = new RpfFile(UpdateRpfPath, "mods\\update\\update.rpf");
                    rpf.ScanStructure(null, _ => { });
                    var fileEntry = FindDlclistEntry(rpf);
                    if (fileEntry == null)
                        throw new FileNotFoundException("dlclist.xml not found inside mods\\update\\update.rpf.");

                    byte[] data = fileEntry.File.ExtractFile(fileEntry);
                    if (data == null || data.Length == 0)
                        throw new InvalidDataException("dlclist.xml extracted to empty bytes — RPF may be corrupted.");

                    string xmlText = TrimBom(Encoding.UTF8.GetString(data));
                    var doc = new XmlDocument();
                    doc.LoadXml(xmlText);
                    var items = doc.SelectNodes("/SMandatoryPacksData/Paths/Item");
                    if (items == null) return true;

                    foreach (XmlNode item in items)
                    {
                        string name = ExtractName(item.InnerText);
                        if (!string.IsNullOrEmpty(name)) set.Add(name);
                    }
                    return true;
                });
            }
            catch (Exception ex)
            {
                LastReadError = ex;
            }
            return set;
        }

        private static RpfFileEntry FindDlclistEntry(RpfFile rpf)
        {
            // common\data\dlclist.xml
            var common = rpf.Root?.Directories?.FirstOrDefault(d =>
                d.Name.Equals("common", StringComparison.OrdinalIgnoreCase));
            var data = common?.Directories?.FirstOrDefault(d =>
                d.Name.Equals("data", StringComparison.OrdinalIgnoreCase));
            return data?.Files?.FirstOrDefault(f =>
                f.Name.Equals("dlclist.xml", StringComparison.OrdinalIgnoreCase)) as RpfFileEntry;
        }

        private static XmlNode FindEntryNode(XmlNode paths, string addonName)
        {
            foreach (XmlNode child in paths.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (!child.Name.Equals("Item", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ExtractName(child.InnerText), addonName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        // Extracts the bare folder name from an <Item> body, handling every format
        // that shows up in dlclist.xml in the wild:
        //   dlcpacks:/<name>/                  (modern, canonical)
        //   dlcpacks:\<name>\                  (some installs use backslashes)
        //   platform:/dlcPacks/<name>/         (older Rockstar entries)
        private static string ExtractName(string itemText)
        {
            if (string.IsNullOrWhiteSpace(itemText)) return "";
            string s = itemText.Trim();

            // Normalize backslashes to forward slashes so the rest of this method
            // can treat both `:\` and `:/` as the same prefix terminator. Without
            // this, entries like `dlcpacks:\22b\` would fall through and end up
            // as the literal raw text, never matching an on-disk folder name.
            s = s.Replace('\\', '/');

            // Strip everything up to and including the first ":/" so we drop the
            // protocol-style prefix (platform: or dlcpacks:).
            int colonSlash = s.IndexOf(":/", StringComparison.Ordinal);
            if (colonSlash >= 0) s = s.Substring(colonSlash + 2);

            // After that, the older format leaves a "dlcPacks/" container segment
            // in front of the name — strip it case-insensitively.
            const string container = "dlcpacks/";
            if (s.Length >= container.Length &&
                s.Substring(0, container.Length).Equals(container, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(container.Length);
            }

            return s.Trim('/').Trim();
        }

        // Insert a new <Item> as the last child of <Paths>, copying the indentation
        // pattern of the previous sibling so the file stays readable to humans.
        private static void AppendWithIndentation(XmlNode paths, XmlElement item)
        {
            XmlNode lastWhitespace = null;
            for (int i = paths.ChildNodes.Count - 1; i >= 0; i--)
            {
                var ch = paths.ChildNodes[i];
                if (ch.NodeType == XmlNodeType.Element)
                {
                    // The whitespace before this last element is the indentation we want to mirror.
                    if (i - 1 >= 0 && paths.ChildNodes[i - 1].NodeType == XmlNodeType.Whitespace)
                        lastWhitespace = paths.ChildNodes[i - 1];
                    break;
                }
            }

            if (lastWhitespace != null)
            {
                var ws = paths.OwnerDocument.CreateWhitespace(lastWhitespace.Value);
                paths.AppendChild(ws);
            }
            paths.AppendChild(item);
        }

        // Remove the element AND the whitespace immediately preceding it so the
        // file doesn't accumulate blank lines on repeated enable/disable cycles.
        private static void RemoveWithSurroundingWhitespace(XmlNode paths, XmlNode element)
        {
            var prev = element.PreviousSibling;
            paths.RemoveChild(element);
            if (prev != null && prev.NodeType == XmlNodeType.Whitespace)
                paths.RemoveChild(prev);
        }

        private static string TrimBom(string s)
        {
            if (!string.IsNullOrEmpty(s) && s.Length > 0 && s[0] == '﻿')
                return s.Substring(1);
            return s;
        }

        private void EnsureKeysLoaded()
        {
            if (GTA5Keys.PC_AES_KEY != null) return;
            bool isGen9 = File.Exists(Path.Combine(_gameFolder, "GTA5_Enhanced.exe"))
                          || File.Exists(Path.Combine(_gameFolder, "eboot.bin"));
            try { GTA5Keys.LoadFromPath(_gameFolder, isGen9, null); }
            catch { /* surfaced later when the RPF read itself fails */ }
        }

        // -- stock-DLC roster ------------------------------------------------

        // Canonical Rockstar pack roster from a recent dlclist.xml. Used to mark
        // these rows as read-only in the Add-ons tab so the user can't disable a
        // stock pack and break the game. Stored lower-cased; the HashSet is
        // OrdinalIgnoreCase so capitalization variations all match.
        private static readonly HashSet<string> StockDLCs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Early single-player / multiplayer DLCs (platform:/dlcPacks/ format)
            "mpbeach", "mpbusiness", "mpchristmas", "mpvalentines", "mpbusiness2",
            "mphipster", "mpindependence", "mppilot", "spupgrade", "mplts",

            // Online expansions
            "mpheist", "mpchristmas2", "mpluxe", "mpluxe2", "mpreplay",
            "mplowrider", "mphalloween", "mpapartment", "mpxmas_604490",
            "mplowrider2", "mpjanuary2016", "mpvalentines2", "mpexecutive",
            "mpstunt", "mpimportexport", "mpbiker", "mpspecialraces",
            "mpgunrunning", "mpairraces", "mpsmuggler", "mpchristmas2017",
            "mpassault", "mpbattle", "mpchristmas2018", "mpvinewood",
            "mpheist3", "mpsum", "mpheist4", "mptuner", "mpsecurity",
            "mpg9ec", "mpsum2", "mpsum2_g9ec", "mpchristmas3", "mpchristmas3_g9ec",
            "mp2023_01", "mp2023_01_g9ec", "mp2023_02", "mp2023_02_g9ec",
            "mp2024_01", "mp2024_01_g9ec", "mp2024_02", "mp2024_02_g9ec",
            "mp2025_01", "mp2025_01_g9ec", "mp2025_02", "mp2025_02_g9ec",

            // Patch packs
            "mppatchesng",
            "patchday1ng", "patchday2ng", "patchday2bng", "patchday3ng",
            "patchday4ng", "patchday5ng", "patchday6ng", "patchday7ng",
            "patchday8ng", "patchday9ng", "patchday10ng", "patchday11ng",
            "patchday12ng", "patchday13ng", "patchday14ng", "patchday15ng",
            "patchday16ng", "patchday17ng", "patchday18ng", "patchday19ng",
            "patchday20ng", "patchday21ng", "patchday22ng", "patchday23ng",
            "patchday24ng", "patchday25ng", "patchday26ng", "patchday27ng",
            "patchday28ng",
            "patchdayg9ecng", "patchday27g9ecng", "patchday28g9ecng",
            "patchbvh00",
            "patch2023_01", "patch2023_01_g9ec", "patch2023_02",
            "patch2024_01", "patch2024_01_g9ec", "patch2024_02",
            "patch2025_01", "patch2025_02", "patch2025_02_g9ec",
        };
    }
}
