using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodeWalker.OIVInstaller
{
    public class DocumentationForm : Form
    {
        public DocumentationForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Documentation & Roadmap";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;

            TabControl tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;

            // Tab 1: User Guide
            TabPage tabGuide = new TabPage("User Guide");
            RichTextBox rtbGuide = new RichTextBox();
            rtbGuide.Dock = DockStyle.Fill;
            rtbGuide.ReadOnly = true;
            rtbGuide.BackColor = Color.White;
            rtbGuide.BorderStyle = BorderStyle.None;
            rtbGuide.Padding = new Padding(10);
            rtbGuide.Text = GetUserGuideText();
            tabGuide.Controls.Add(rtbGuide);
            tabControl.TabPages.Add(tabGuide);

            // Tab 2: Todo / Roadmap
            TabPage tabTodo = new TabPage("Developer Todo");
            RichTextBox rtbTodo = new RichTextBox();
            rtbTodo.Dock = DockStyle.Fill;
            rtbTodo.ReadOnly = true;
            rtbTodo.BackColor = Color.White;
            rtbTodo.BorderStyle = BorderStyle.None;
            rtbTodo.Padding = new Padding(10);
            rtbTodo.Text = GetTodoText();
            tabTodo.Controls.Add(rtbTodo);

            tabControl.TabPages.Add(tabTodo);

            // Tab 3: Feature Support
            TabPage tabFeatures = new TabPage("Feature Support");
            RichTextBox rtbFeatures = new RichTextBox();
            rtbFeatures.Dock = DockStyle.Fill;
            rtbFeatures.ReadOnly = true;
            rtbFeatures.BackColor = Color.White;
            rtbFeatures.BorderStyle = BorderStyle.None;
            rtbFeatures.Padding = new Padding(10);
            rtbFeatures.Text = GetFeatureSupportText();
            tabFeatures.Controls.Add(rtbFeatures);
            tabControl.TabPages.Add(tabFeatures);

            this.Controls.Add(tabControl);
            
            // Add close button at bottom
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 50;
            
            Button btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.DialogResult = DialogResult.OK;
            btnClose.Location = new Point(this.Width - 100, 10);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            bottomPanel.Controls.Add(btnClose);
            
            this.Controls.Add(bottomPanel);
        }

        private string GetUserGuideText()
        {
            return 
@"OIV Installer - User Guide
==========================

1. Installation
   - The window opens on an empty landing card. Drag any .oiv or .rpf onto it,
     or click 'Browse...' next to OIV Package to pick one.
   - The card disappears and the package's description, info, and an Install
     button slide in. The window grows to fit the content.
   - Select your GTA V game folder (auto-validated for Legacy / Enhanced).
   - Click 'Install'.
   - FiveM Support: Drag and drop a .RPF file to automatically install it to your FiveM mods folder.
     (Automatically detects path via Registry or %LocalAppData%).

2. Previewing What an OIV Will Do
   - After picking a package, a 'View install steps →' link appears below the
     description.
   - Click it to open a read-only inspector showing every operation the OIV
     declares: archive edits, file adds / deletes, text-file inserts / replaces,
     XML / PSO add / replace / remove, defragmentation.
   - The tree on the left lists each operation; selecting a node shows the
     full details (source paths, XPath expressions, full content) on the right.
   - Useful for sanity-checking a package's scope before installing it.

3. After Install
   - Click 'Done' on the install log to return to the empty landing card,
     ready for the next package.
   - The previous .oiv is dropped automatically so the Install button can't
     fire twice against the same package.

4. Uninstalling & Conflicts
   - If you try to install a package that is already installed, the installer will detect the conflict.
   - You can choose to:
     A) Uninstall & Revert to Backup:
        - Smart Revert: Attempts to undo specific text/XML changes, preserving other mods' edits.
        - Full Backup: Restores exact file backup if smart revert is not applicable.
     B) Uninstall & Reset to Vanilla: Wipes the files and replaces them with original game versions.
     C) Keep Existing: Installs the new mod on top of the old one (Stacking).

5. Skip Backup (Optional)
   - The 'Skip backup' checkbox at the bottom-right of the paths panel will
     install the package without recording any backup data.
   - Trade-off: the install is faster and uses no disk space for backups,
     but the package will NOT show up in Manage Mods and cannot be reverted
     through this tool. Use only when you don't intend to uninstall later.

6. Managing Mods
   - The 'Manage Mods' button opens a window with two tabs:

     a) Installed Packages
        - Lists every OIV / FiveM .rpf installed through this tool.
        - Select a row and click 'Uninstall' to revert.
        - Skip-Backup installs do NOT appear here (they have no backup data).

     b) DLC Add-ons
        - Lists every folder under mods\update\x64\dlcpacks\ together with
          every entry already declared in dlclist.xml.
        - Each user add-on has a checkbox; tick to enable, untick to disable.
          Disabling is non-destructive — only the dlclist entry is removed,
          the folder stays in place so re-enabling is one click.
        - Toggles are STAGED — clicking a checkbox does NOT write to
          dlclist.xml immediately. The row's status changes to
          'Enabled (pending)' / 'Disabled (pending)' and the Apply button
          at the bottom-right shows the pending count, e.g. 'Apply (3)'.
        - Click 'Apply' to commit every pending change in one batch.
          Per-row failures are reported and the list reloads from disk so
          the UI always reflects ground truth.
        - 'Reload' re-reads dlclist and resets all checkboxes — useful for
          discarding pending changes without applying them.
        - Vanilla Rockstar DLCs (mpheist, mp2025_*, patchday*, etc.) appear
          on a pale grey row with italic text and a 'Vanilla DLC' status —
          read-only, so a stray click can't disable a stock pack.
        - Orphans (entries in dlclist with no matching folder) are flagged
          'Orphan' and also read-only. Usually a leftover from manually
          deleting an add-on folder.

7. Installing DLC Add-ons
   - Drag a folder containing dlc.rpf onto the window:
       The folder name becomes the add-on name automatically.
   - Or drag a bare dlc.rpf file:
       A small dialog asks you to type a destination folder name. The name
       is validated (no path separators, not a Rockstar stock name).
   - Click Install:
       The folder/file is copied to mods\update\x64\dlcpacks\<name>\ and
       a matching <Item>dlcpacks:/<name>/</Item> is added to dlclist.xml
       inside mods\update\update.rpf.
   - Conflict: if dlcpacks\<name>\ already exists you are asked to confirm
     before overwriting.
   - Add-ons installed this way do NOT show in 'Installed Packages' — they
     are managed entirely through the DLC Add-ons tab.

8. Backups
   - Backups are stored in your game folder under 'OIV_Uninstall_Data'.
   - Do not verify/repair via Steam/Epic/Launcher while mods are installed if you plan to uninstall them via this tool, as it might desync the backup state.

9. Supported Features (OpenIV 2.2 Format)
   - Full metadata support (name, version, author, description, colors).
   - XML editing with XPath (add, replace, remove operations).
   - Text file editing (insert, replace, delete operations).
   - PSO/META editing inside RPF archives (YMT, YMF, YMAP, YTYP).
   - Nested RPF creation (createIfNotExist at any depth).
   - Supports GTA V Legacy & Enhanced (Gen9) Versions.
   - <gameversion> validation (Warns if package requires Enhanced/Legacy mismatch).

10. Command Line Interface (CLI)
   - The installer supports full automation via command line.
   - Usage: CodeWalker.OIVInstaller.exe [options]
   - Options:
     --install <path>       Install an OIV package
     --uninstall <name>     Uninstall a package by name
     --uninstall-oiv <path> Uninstall by reading package name from OIV file
     --list                 List installed packages
     --set-game <path>      Set default game folder
     --get-game             Get default game folder
     --browse               Open folder picker to set game folder
     --force                Ignore GameVersion warnings
     --skip_backup          Do not back up original files (saves space)
     --help                 Show all options

   - Automation:
     If no game folder is set, CLI commands will open a folder picker dialog.
     Example batch scripts for automation are available in the repository:
     https://github.com/crxhvrd/CodeWalkerProjects/tree/master
";
        }

        private string GetTodoText()
        {
            return
@"Developer Todo List / Roadmap
=============================

COMPLETED FEATURES:
[x] PSO file editing inside RPF archives (YMT, YMF, YMAP, YTYP, PSO)
[x] Nested RPF creation (createIfNotExist at any depth)
[x] XML append positions (First, Last, Before, After)
[x] Path normalization fixes (mods\mods duplication bug)
[x] Parent chain header refresh for deeply nested RPFs
[x] Smart Text/XML Revert on uninstall (reverses specific additions/edits instead of full file revert)
[x] FiveM RPF Support (Install/Uninstall/CLI)
[x] Recursive Batch Script Generation
[x] Skip-Backup option (faster, non-revertable install path)
[x] Empty-state landing card with drag-and-drop affordance
[x] Animated empty <-> loaded transition (header / button slide / form resize)
[x] Resizable window with reactive title-clip + button anchoring
[x] Install Steps preview window (assembly.xml operations tree)
[x] Auto-return to empty state after a successful install
[x] Theme support from OIV header background (panel color + accent on links/Install)
[x] Idle R→G→B header pulse while no package is loaded
[x] DLC Add-on management (Manage Mods → DLC Add-ons tab):
    - enable/disable user add-ons by toggling their dlclist.xml entry
    - staged toggles + explicit Apply / Reload buttons (no surprise writes
      when ListView fires deferred LVN_ITEMCHANGED notifications)
    - vanilla Rockstar DLCs rendered as read-only ""Vanilla DLC"" rows
    - orphan dlclist entries flagged separately
[x] DLC Add-on installation by drag-and-drop:
    - drop a folder containing dlc.rpf  → installs as <foldername>
    - drop a bare dlc.rpf file          → prompts for an add-on name
    - confirms overwrite if dlcpacks\<name>\ already exists
[x] Retry-with-backoff on update.rpf I/O lock (antivirus / search indexer)

KNOWN LIMITATIONS:
- OpenIV may report validation errors on RPFs with 2+ levels of nesting,
  but these files open correctly in CodeWalker.

REMAINING TODO:

1. Archive Management
   [ ] Implement actual 'Defragmentation' logic (currently a placeholder).

2. UI Improvements
   [ ] Display extended metadata (License, Social Media links) in the main window.
   [ ] Smooth color tween for header theme on loaded <-> empty transition
       (currently snaps to default blue).

3. Core Features
   [ ] Add full transaction support for safer installations (rollback on crash).
   [ ] Validating 'Condition' attributes for file content more rigorously.

4. Enhanced Installation Features
   [ ] Implement 'Enhanced' mod installation flow (Vortex-style).
       - Allow installing not only a single mod package but also choosing optional components.
   [ ] Automatic dependencies installation (OpenIV.asi / OpenRPF.asi / RageOpenV.asi, ASI Loader).

";
        }

        private string GetFeatureSupportText()
        {
            return
@"OpenIV 2.2 Feature Support & Uninstall Logic
============================================

The following table details how each OIV 2.2 feature is handled during installation and uninstallation (Manage Mods).

1. FILE OPERATIONS
------------------
- <add> (New File)
  Install: Copies file to game/mods folder.
  Uninstall: Deletes the file.

- <add> (Replace File)
  Install: Backs up original file, then overwrites.
  Uninstall: Restores the original file from backup.

- <delete>
  Install: Backs up target file, then deletes it.
  Uninstall: Restores the deleted file from backup.

2. ARCHIVE OPERATIONS
---------------------
- <archive> (createIfNotExist=""True"")
  Install: Creates new RPF archive.
  Uninstall: Deletes the created archive.

- <archive> (Edit Existing)
  Install: Opens archive to perform inner operations.
  Uninstall: Reverses inner operations (see below).

3. TEXT EDITING (Smart Revert)
------------------------------
- <add> (Append Line)
- <insert> (Before/After)
- <replace>
- <delete>

  Install: Tracks specific line changes.
  Uninstall: SMART REVERT - Attempts to reverse ONLY the specific lines changed by this mod.
             (e.g., removes inserted lines, restores replaced lines).
             If Smart Revert fails, restores the full file backup.

4. XML / PSO EDITING (Smart Revert)
-----------------------------------
- <add> (First/Last/Before/After)
- <replace>
- <remove>

  Install: Tracks specific XPath operations.
  Uninstall: SMART REVERT - Attempts to reverse ONLY the specific node changes.
             (e.g. removes added nodes, restores removed nodes).
             If Smart Revert fails, restores the full file backup.

5. METADATA & COLORS
--------------------
- Handled purely by the installer UI. No game file impact.

6. COMPATIBILITY CHECKS
-----------------------
- <gameversion>
  Validates if the target game folder (Legacy/Enhanced) matches the package requirement.
  Shows a warning if mismatched, but permits installation.

7. ADD-ON CONTENT (DLCs)
------------------------
- Addon RPFs (e.g. dlc.rpf)
  Install: Copied via <content> or created via <archive>. Use backup system to track as 'Added'.
  Uninstall: The added .rpf file is DELETED. Parent folders (e.g. dlcpacks/modname) are removed if empty.

- dlclist.xml Updates
  Install: Typically done via XML <add> command.
  Uninstall: Reversed via Smart XML Revert (the added line is removed), keeping other mods intact.

8. FIVEM SUPPORT
----------------
- .RPF Installation
  Install: Direct copy to registry-detected path or %localappdata%\FiveM\FiveM.app\mods. (Creates mods folder if needed).
  Uninstall: Deletes the .rpf file. (Shows 'Remove Mod' confirmation).
- Metadata
  Reads assembly.xml from inside the RPF to display mod info.
";
        }
    }
}
