using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.OIVInstaller
{
    public partial class UninstallForm : Form
    {
        private List<BackupManager> _managers = new List<BackupManager>();
        private Dictionary<BackupLog, BackupManager> _logSource = new Dictionary<BackupLog, BackupManager>();
        private List<BackupLog> _allPackages = new List<BackupLog>();

        // Add-ons tab state — we cache the AddonManager for whichever game folder the
        // user came in with (FiveM doesn't have a dlcpacks concept, so the tab is
        // hidden when only a FiveM folder is available).
        private AddonManager _addonManager;
        private List<AddonManager.AddonInfo> _addonList = new List<AddonManager.AddonInfo>();
        // ItemCheck reentrancy guard — toggling .Checked from code re-fires the event.
        private bool _suppressAddonCheck;

        public UninstallForm(string gameFolder, string fiveMFolder = null)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(gameFolder) && Directory.Exists(gameFolder))
            {
                _managers.Add(new BackupManager(gameFolder));
                _addonManager = new AddonManager(gameFolder);
            }

            if (!string.IsNullOrEmpty(fiveMFolder) && Directory.Exists(fiveMFolder))
            {
                // Avoid adding same folder twice if user selected FiveM folder as game folder
                if (string.IsNullOrEmpty(gameFolder) || !string.Equals(Path.GetFullPath(gameFolder), Path.GetFullPath(fiveMFolder), StringComparison.OrdinalIgnoreCase))
                {
                    _managers.Add(new BackupManager(fiveMFolder));
                }
            }

            this.Text = "Manage Mods";
            LoadPackages();

            // Add-ons require a SP game folder with a mods\ tree; if we don't have
            // one (FiveM-only), drop the tab entirely so the UI isn't misleading.
            if (_addonManager == null)
            {
                tabs.TabPages.Remove(tabAddons);
            }
            else
            {
                // Defer the first LoadAddons until after the window handle exists —
                // LoadAddons uses BeginInvoke to flush queued ListView notifications,
                // which requires a handle. Form.Load fires after handle creation.
                this.Load += (s, e) => LoadAddons();
            }
        }

        // -- Installed Packages tab -----------------------------------------

        private void LoadPackages()
        {
            lstPackages.Items.Clear();
            _allPackages.Clear();
            _logSource.Clear();

            foreach (var manager in _managers)
            {
                var mgrPackages = manager.GetInstalledPackages();
                string folderName = new DirectoryInfo(manager.GameFolder).Name;
                bool isFiveM = folderName.Equals("mods", StringComparison.OrdinalIgnoreCase) && manager.GameFolder.Contains("FiveM", StringComparison.OrdinalIgnoreCase);

                foreach (var pkg in mgrPackages)
                {
                    _allPackages.Add(pkg);
                    _logSource[pkg] = manager;

                    string platformTag = pkg.IsGen9 ? "[Gen9]" : "[Legacy]";
                    string sourceTag = isFiveM ? "[FiveM]" : "[SP]";

                    lstPackages.Items.Add($"{sourceTag} {platformTag} {pkg.PackageName} ({pkg.InstallDate})");
                }
            }
        }

        private void lstPackages_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnUninstall.Enabled = lstPackages.SelectedIndex >= 0;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void btnUninstall_Click(object sender, EventArgs e)
        {
            if (lstPackages.SelectedIndex < 0) return;
            var log = _allPackages[lstPackages.SelectedIndex];
            var manager = _logSource[log];

            // Check for running game process
            while (ProcessHelper.IsGameRunning(out string processName))
            {
                var result = MessageBox.Show(
                    $"The game process '{processName}' is currently running.\n\n" +
                    "Please close the game before uninstalling mods to prevent file locking errors.",
                    "Game is Running",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel) return;
            }

            UninstallMode mode = UninstallMode.Backup;

            bool isFiveM = manager.GameFolder.Contains("FiveM", StringComparison.OrdinalIgnoreCase);

            if (isFiveM)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to uninstall '{log.PackageName}'?\nThis will remove the file from your FiveM mods folder.",
                    "Confirm Uninstall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No) return;

                mode = UninstallMode.Backup;
            }
            else
            {
                // Custom Dialog for Uninstall Choice (SP only)
                using (var prompt = new Form())
                {
                    prompt.Width = 450;
                    prompt.Height = 220;
                    prompt.Text = "Uninstall Options";
                    prompt.StartPosition = FormStartPosition.CenterParent;
                    prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                    prompt.MaximizeBox = false;
                    prompt.MinimizeBox = false;

                    var iconBox = new PictureBox { Image = SystemIcons.Question.ToBitmap(), Size = new Size(32, 32), Location = new Point(20, 20) };
                    prompt.Controls.Add(iconBox);

                    var lbl = new Label
                    {
                        Text = $"How do you want to uninstall '{log.PackageName}'?",
                        Location = new Point(70, 25),
                        Size = new Size(350, 40)
                    };
                    prompt.Controls.Add(lbl);

                    var btnBackup = new Button
                    {
                        Text = "Revert to Previous State\n(Using Backups)",
                        Location = new Point(70, 70),
                        Size = new Size(160, 50),
                        DialogResult = DialogResult.Yes
                    };
                    btnBackup.Click += (s, ev) => { mode = UninstallMode.Backup; prompt.Close(); };
                    prompt.Controls.Add(btnBackup);

                    var btnVanilla = new Button
                    {
                        Text = "Reset to Vanilla\n(Ignore Backups)",
                        Location = new Point(240, 70),
                        Size = new Size(160, 50),
                        DialogResult = DialogResult.OK
                    };
                    btnVanilla.Click += (s, ev) => { mode = UninstallMode.Vanilla; prompt.Close(); };
                    prompt.Controls.Add(btnVanilla);

                    var btnCancel = new Button
                    {
                        Text = "Cancel",
                        Location = new Point(320, 140),
                        Size = new Size(80, 25),
                        DialogResult = DialogResult.Cancel
                    };
                    btnCancel.Click += (s, ev) => { prompt.Close(); };
                    prompt.Controls.Add(btnCancel);
                    prompt.CancelButton = btnCancel;

                    var result = prompt.ShowDialog(this);
                    if (result == DialogResult.Cancel) return;
                }
            }

            lblStatus.Text = "Uninstalling...";
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            btnUninstall.Enabled = false;
            lstPackages.Enabled = false;

            try
            {
                // Ensure keys are loaded (needed for RPF operations on SP).
                if (GTA5Keys.PC_AES_KEY == null)
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            bool isGen9 = File.Exists(Path.Combine(manager.GameFolder, "eboot.bin")) ||
                                         File.Exists(Path.Combine(manager.GameFolder, "GTA5_Enhanced.exe"));
                            GTA5Keys.LoadFromPath(manager.GameFolder, isGen9, null);
                        }
                        catch
                        {
                            // Ignore key loading failure for FiveM / standalone usage.
                        }
                    });
                }

                await Task.Run(() => PerformUninstall(manager, log, mode));

                MessageBox.Show("Uninstallation complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadPackages();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during uninstall: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lblStatus.Text = "";
                progressBar.Visible = false;
                lstPackages.Enabled = true;
                btnUninstall.Enabled = lstPackages.SelectedIndex >= 0;
            }
        }

        private void PerformUninstall(BackupManager manager, BackupLog log, UninstallMode mode)
        {
            var progress = new Progress<string>(msg =>
            {
                if (msg.StartsWith("Reverting:")) return;
                UpdateStatus(msg);
            });
            manager.Uninstall(log, progress, mode);
        }

        private void UpdateStatus(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(UpdateStatus), text);
                return;
            }
            lblStatus.Text = text;
        }

        // -- DLC Add-ons tab ------------------------------------------------

        private void tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Always show a fresh list when the user switches to the Add-ons tab —
            // dlcpacks folder contents may have changed externally between sessions.
            if (tabs.SelectedTab == tabAddons && _addonManager != null)
            {
                LoadAddons();
            }
        }

        private void btnRefreshAddons_Click(object sender, EventArgs e)
        {
            LoadAddons();
        }

        private void LoadAddons()
        {
            if (_addonManager == null) return;

            // ListView posts LVN_ITEMCHANGED notifications via the message queue
            // when items are added / their Checked state is set. Those messages
            // fire AFTER LoadAddons returns to the message pump — so detaching
            // handlers inside LoadAddons and re-attaching in `finally` isn't
            // enough on its own: the late notifications hit live handlers and get
            // treated as "user clicked the checkbox", writing spurious disables
            // to dlclist.xml. The fix is to also defer the *re-attach* to the
            // tail of the message queue with BeginInvoke, so the queued
            // notifications drain first (with handlers still detached) and only
            // then do we resubscribe.
            lstAddons.ItemCheck    -= lstAddons_ItemCheck;
            lstAddons.ItemChecked  -= lstAddons_ItemChecked;
            _suppressAddonCheck = true;
            try
            {
                lstAddons.BeginUpdate();
                lstAddons.Items.Clear();

                if (!_addonManager.ModsFolderExists)
                {
                    lblAddonsHint.Text = "mods\\update\\update.rpf not found — set up your mods folder before managing add-ons.";
                    _addonList = new List<AddonManager.AddonInfo>();
                    lstAddons.EndUpdate();
                    return;
                }

                _addonList = _addonManager.ListAddons();

                if (_addonManager.LastReadError != null)
                {
                    lblAddonsHint.Text = "dlclist.xml read failed — " + _addonManager.LastReadError.Message;
                    lblAddonsHint.ForeColor = System.Drawing.Color.FromArgb(180, 30, 30);
                    lstAddons.EndUpdate();
                    return;
                }
                lblAddonsHint.ForeColor = System.Drawing.Color.Gray;

                foreach (var info in _addonList)
                {
                    string status =
                        info.IsStockDLC ? "Vanilla DLC" :
                        !info.FolderExists ? "Orphan" :
                        info.IsEnabled ? "Enabled" : "Disabled";
                    string folder =
                        info.FolderExists ? info.FolderPath :
                        info.IsStockDLC ? "(Rockstar vanilla — content inside update.rpf)" :
                        "(no folder — orphan dlclist entry)";
                    var item = new ListViewItem(new[] { info.Name, status, folder })
                    {
                        Tag = info,
                    };
                    if (info.IsStockDLC)
                    {
                        item.BackColor = System.Drawing.Color.FromArgb(245, 246, 248);
                        item.ForeColor = System.Drawing.Color.FromArgb(120, 120, 130);
                        item.Font = new System.Drawing.Font(lstAddons.Font, System.Drawing.FontStyle.Italic);
                    }
                    else if (!info.FolderExists)
                    {
                        item.ForeColor = System.Drawing.Color.Gray;
                    }
                    lstAddons.Items.Add(item);
                    // Set Checked AFTER Add — setting it in the property initializer
                    // before the item is in any ListView leads to ItemChecked firing
                    // in ways that can outlive our suppress flag. Once the item is
                    // attached, Checked-set events route correctly and we've detached
                    // the handlers anyway.
                    item.Checked = info.IsEnabled;
                }

                // Counts: keep them mutually exclusive so the line adds up.
                int userTotal    = _addonList.Count(a => !a.IsStockDLC && a.FolderExists);
                int userEnabled  = _addonList.Count(a => !a.IsStockDLC && a.FolderExists && a.IsEnabled);
                int stockCount   = _addonList.Count(a => a.IsStockDLC);
                int orphanCount  = _addonList.Count(a => !a.IsStockDLC && !a.FolderExists);
                lblAddonsHint.Text =
                    $"{userEnabled} / {userTotal} user add-ons enabled" +
                    (stockCount > 0 ? $"  ·  {stockCount} vanilla" : "") +
                    (orphanCount > 0 ? $"  ·  {orphanCount} orphan" : "");
                lstAddons.EndUpdate();

                // After a clean reload, no row's checkbox differs from disk yet.
                // Phantom ItemChecked events during populate (if any) get handled
                // by the deferred re-attach in the finally block.
                btnApplyAddons.Enabled = false;
                btnApplyAddons.Text = "Apply";
            }
            finally
            {
                // Defer the re-attach to the tail of the message queue. Any
                // LVN_ITEMCHANGED notifications posted during Items.Add /
                // Checked-set above are still in the queue at this point; they
                // will be processed first (with handlers detached, so they have
                // no effect), and only then will this BeginInvoke fire and
                // resubscribe for real user interactions.
                this.BeginInvoke((Action)(() =>
                {
                    _suppressAddonCheck = false;
                    lstAddons.ItemCheck    += lstAddons_ItemCheck;
                    lstAddons.ItemChecked  += lstAddons_ItemChecked;
                }));
            }
        }

        // ItemCheck fires BEFORE the .Checked property actually changes, which is
        // the only place we can cancel the toggle for read-only rows.
        private void lstAddons_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressAddonCheck) return;

            var item = lstAddons.Items[e.Index];
            if (!(item.Tag is AddonManager.AddonInfo info)) return;

            if (info.IsStockDLC || !info.FolderExists)
            {
                // Revert any attempt to toggle a read-only row.
                e.NewValue = e.CurrentValue;
            }
        }

        // Click only updates the in-memory checkbox state and the row's status
        // label. Nothing is written to dlclist.xml until the user explicitly
        // hits Apply — this makes phantom ListView ItemChecked events harmless
        // (worst case the user sees a "(pending)" row that doesn't reflect
        // their intent, and clicks Reload to discard it).
        private void lstAddons_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressAddonCheck) return;
            if (e == null || e.Item == null) return;
            if (!(e.Item.Tag is AddonManager.AddonInfo info)) return;
            if (info.IsStockDLC || !info.FolderExists) return;

            UpdateRowStatus(e.Item, info);
            UpdateApplyButtonState();
        }

        // Sets the Status column for a user-toggleable row, marking it with
        // "(pending)" whenever the checkbox differs from what's actually on disk.
        private static void UpdateRowStatus(ListViewItem item, AddonManager.AddonInfo info)
        {
            bool dirty = item.Checked != info.IsEnabled;
            string baseText = item.Checked ? "Enabled" : "Disabled";
            item.SubItems[1].Text = dirty ? $"{baseText} (pending)" : baseText;
        }

        // Apply is enabled iff at least one user-toggleable row's checkbox differs
        // from the on-disk state captured by the last LoadAddons. Defensive null
        // checks: WinForms can reflect deferred LVN_ITEMCHANGED notifications at
        // moments when individual items / their Tag aren't fully wired up yet.
        private void UpdateApplyButtonState()
        {
            if (lstAddons == null || btnApplyAddons == null) return;
            int pending = 0;
            try
            {
                foreach (ListViewItem item in lstAddons.Items)
                {
                    if (item == null) continue;
                    if (!(item.Tag is AddonManager.AddonInfo info)) continue;
                    if (info.IsStockDLC || !info.FolderExists) continue;
                    if (item.Checked != info.IsEnabled) pending++;
                }
            }
            catch (Exception)
            {
                // Items collection mutated under us (e.g. mid-LoadAddons reflection).
                // Bail; the next stable call will recompute correctly.
                return;
            }
            btnApplyAddons.Enabled = pending > 0;
            btnApplyAddons.Text = pending > 0 ? $"Apply ({pending})" : "Apply";
        }

        private async void btnApplyAddons_Click(object sender, EventArgs e)
        {
            // Snapshot pending changes off the UI before the await — we don't want
            // the user toggling more rows mid-apply.
            var changes = new List<(AddonManager.AddonInfo Info, bool Target, ListViewItem Item)>();
            foreach (ListViewItem item in lstAddons.Items)
            {
                if (!(item.Tag is AddonManager.AddonInfo info)) continue;
                if (info.IsStockDLC || !info.FolderExists) continue;
                if (item.Checked != info.IsEnabled)
                    changes.Add((info, item.Checked, item));
            }
            if (changes.Count == 0) return;

            // Check for running game process — same dance as the uninstall path.
            while (ProcessHelper.IsGameRunning(out string processName))
            {
                var result = MessageBox.Show(
                    $"The game process '{processName}' is currently running.\n\n" +
                    "Please close the game before changing dlclist.xml to prevent file locking errors.",
                    "Game is Running",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);
                if (result == DialogResult.Cancel) return;
            }

            lblStatus.Text = $"Applying {changes.Count} change(s)…";
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            lstAddons.Enabled = false;
            btnApplyAddons.Enabled = false;
            btnRefreshAddons.Enabled = false;

            int applied = 0, failed = 0;
            var failures = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var c in changes)
                    {
                        try
                        {
                            _addonManager.SetEnabled(c.Info.Name, c.Target);
                            c.Info.IsEnabled = c.Target;
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failures.Add($"{c.Info.Name}: {ex.Message}");
                        }
                    }
                });
            }
            finally
            {
                progressBar.Visible = false;
                lstAddons.Enabled = true;
                btnRefreshAddons.Enabled = true;
            }

            if (failed > 0)
            {
                MessageBox.Show(
                    $"Applied {applied} change(s). Failed: {failed}.\n\n" + string.Join("\n", failures),
                    "Some changes failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            lblStatus.Text = failed == 0
                ? $"Applied {applied} change(s)."
                : $"Applied {applied} of {applied + failed} change(s).";

            // Reload from disk so the UI reflects ground truth (and any failed
            // toggles snap back to their on-disk state).
            LoadAddons();
        }
    }
}
