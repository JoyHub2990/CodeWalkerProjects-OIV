using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace CodeWalker.OIVInstaller
{
    public partial class MainForm : Form
    {
        private OivPackage _package;
        private string _gameFolder = ""; // Current install target
        private string _spGameFolder = ""; // Actual GTA V folder
        private string _gameFolderLegacy = "";
        private string _gameFolderEnhanced = "";
        private MarqueePainter _marquee;
        private Animator _formFadeAnimator;
        private Animator _formResizeAnimator;
        private HeaderColorPulser _headerPulser;

        public MainForm()
        {
            InitializeComponent();
            // Hide the original lblPackageName — we paint the title text ourselves on
            // pnlTitleClipping for sub-pixel scrolling smoothness. The Label still owns
            // font/color which the painter mirrors.
            this.Opacity = 0; // fade in on Shown
            AttachButtonAnimations();
            SetupMarqueePainter();
            SetupEmptyState();
            SetupTitleClipResizing();
            AttachSkipBackupConfirm();
        }

        // The title clip is fixed-width in the Designer (235px) but with a resizable
        // form, the header buttons are anchored Top|Right so they slide further from
        // the title as the form widens — and during the empty→loaded slide tween the
        // gap between title-left and Docs-left changes too. Reactively size the clip
        // to fill that gap so the marquee always uses the full available space.
        private void SetupTitleClipResizing()
        {
            EventHandler refresh = (s, e) => UpdateTitleClipWidth();
            btnDocs.LocationChanged += refresh;
            pnlTitleClipping.LocationChanged += refresh;
            this.SizeChanged += refresh;
        }

        private void UpdateTitleClipWidth()
        {
            if (pnlTitleClipping == null || btnDocs == null) return;
            int target = btnDocs.Left - pnlTitleClipping.Left - 12;
            if (target < 80) target = 80;
            if (pnlTitleClipping.Width != target)
            {
                pnlTitleClipping.Width = target;
                pnlTitleClipping.Invalidate();
            }
        }

        // Empty-state landing view: a dashed-border card with package glyph + hints.
        // Shown until a package is loaded; replaced by description/info/additional via
        // the crossfade transition in DisplayPackageInfo.
        private void SetupEmptyState()
        {
            lblEmptyIcon.Text = ""; // OpenFile glyph (Segoe MDL2 Assets)
            panelEmptyState.Paint += OnEmptyStatePaint;
            panelEmptyState.SizeChanged += (s, e) =>
            {
                CenterEmptyStateContent();
                // Panel doesn't fully invalidate on resize by default, so the previously
                // drawn dashed border ghosts in the newly exposed area. Force repaint.
                panelEmptyState.Invalidate();
            };
            // Initial centering is performed in MainForm_Load — the window handle isn't
            // created yet here, so we can't BeginInvoke onto the UI thread.
        }

        private void OnEmptyStatePaint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = panelEmptyState.ClientRectangle;
            rect.Inflate(-1, -1);
            using (var path = RoundedRectPath(rect, 8))
            using (var pen = new System.Drawing.Pen(Color.FromArgb(205, 212, 220), 1.4f))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Custom;
                pen.DashPattern = new float[] { 5f, 4f };
                g.DrawPath(pen, path);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // The "Mod Package:" / "GTA V Folder:" labels are AutoSize, so their widths
        // aren't known until first layout. Align both textboxes (and the status labels
        // below them) just after the *wider* of the two labels so the gap is tight
        // and consistent regardless of label text. Textboxes are anchored Top|Left|
        // Right so changing .Left expands their Width to keep the Browse button gap.
        private void AlignPathFieldsToLabels()
        {
            int rightOfWiderLabel = Math.Max(lblOivLabel.Right, lblGameFolderLabel.Right);
            int textboxLeft = rightOfWiderLabel + 6;
            txtOivPath.Left = textboxLeft;
            txtGameFolder.Left = textboxLeft;
            lblGameStatus.Left = textboxLeft;
            lblAsiStatus.Left = textboxLeft;
        }

        private void CenterEmptyStateContent()
        {
            if (panelEmptyState == null || panelEmptyState.IsDisposed) return;
            int pw = panelEmptyState.ClientSize.Width;
            int ph = panelEmptyState.ClientSize.Height;
            int gapIconTitle = 6;
            int gapTitleSub = 4;
            int totalH = lblEmptyIcon.Height + gapIconTitle + lblEmptyTitle.Height + gapTitleSub + lblEmptySubtitle.Height;
            int y = Math.Max(8, (ph - totalH) / 2);

            lblEmptyIcon.Left = (pw - lblEmptyIcon.Width) / 2;
            lblEmptyIcon.Top = y;
            lblEmptyTitle.Left = (pw - lblEmptyTitle.Width) / 2;
            lblEmptyTitle.Top = lblEmptyIcon.Bottom + gapIconTitle;
            lblEmptySubtitle.Left = (pw - lblEmptySubtitle.Width) / 2;
            lblEmptySubtitle.Top = lblEmptyTitle.Bottom + gapTitleSub;
        }

        private void AttachSkipBackupConfirm()
        {
            // First-time confirm: if the user ticks Skip Backup, make sure they understand
            // that this install won't appear in Manage Mods and can't be reverted from the UI.
            // No persistence — they get the prompt every time they re-enable it within a session.
            bool confirmed = false;
            chkSkipBackup.CheckedChanged += (s, e) =>
            {
                if (!chkSkipBackup.Checked) { confirmed = false; return; }
                if (confirmed) return;
                var result = MessageBox.Show(
                    "Skipping backup creation means:\n\n" +
                    "• No backup of the game files this install touches\n" +
                    "• This install won't show up in Manage Mods\n" +
                    "• You won't be able to uninstall it through this app\n\n" +
                    "Continue with backup disabled?",
                    "Skip Backup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes) { confirmed = true; }
                else { chkSkipBackup.Checked = false; }
            };
        }

        private void SetupMarqueePainter()
        {
            // Replace the legacy 1px-step Timer marquee with a custom-painted, time-based
            // sub-pixel renderer. lblPackageName becomes invisible but its font/color are
            // still the source of truth so theme code keeps working unchanged.
            lblPackageName.Visible = false;
            _marquee = new MarqueePainter(pnlTitleClipping, lblPackageName.Font)
            {
                ForeColor = lblPackageName.ForeColor,
                Text = lblPackageName.Text,
            };
            _marquee.Start();
            // Stop the legacy timer (still wired in Designer); keep the field around so
            // the Designer-generated InitializeComponent doesn't need editing.
            tmrMarquee.Stop();
        }

        // Mirror the (still-hidden) lblPackageName state onto the marquee painter so
        // that DisplayPackageInfo's existing assignments to lblPackageName.Text / ForeColor
        // continue to drive the visible title without changes elsewhere.
        private void SyncMarqueeFromLabel()
        {
            if (_marquee == null) return;
            _marquee.Text = lblPackageName.Text ?? "";
            _marquee.ForeColor = lblPackageName.ForeColor;
            _marquee.Font = lblPackageName.Font;
            _marquee.ResetPosition();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Empty-state labels are AutoSize, so we wait until the form's first layout
            // pass (Load fires after the handle is created and initial layout has run)
            // before measuring + centering them.
            CenterEmptyStateContent();
            AlignPathFieldsToLabels();

            // Idle-state header animation — slow R→G→B→R rainbow loop while no
            // package is loaded. Stops the moment a package is picked.
            _headerPulser = new HeaderColorPulser(
                panelHeader,
                Color.FromArgb(220, 60, 60),    // red
                Color.FromArgb(60, 180, 90),    // green
                Color.FromArgb(0, 120, 215));   // blue (Windows accent)
            _headerPulser.CycleMs = 12000;
            _headerPulser.Start();

            // Check command line args for OIV file
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1]) && (args[1].EndsWith(".oiv", StringComparison.OrdinalIgnoreCase) || args[1].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
            {
                txtOivPath.Text = args[1];
                LoadOivPackage(args[1]);
            }

            LoadConfig();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Smooth opacity fade-in. Cheap polish — masks the brief paint-storm of the
            // first WM_PAINT pass and gives the window a "settled" entrance.
            _formFadeAnimator?.Dispose();
            _formFadeAnimator = new Animator();
            _formFadeAnimator.Tween(220, t =>
            {
                if (this.IsDisposed) return;
                this.Opacity = t;
            }, () =>
            {
                if (!this.IsDisposed) this.Opacity = 1.0;
            }, Easing.EaseOutCubic);
        }

        /// <summary>
        /// Wires smooth color-blend hover/press animations onto the form's buttons.
        /// Header buttons are white-on-accent; browse buttons sit on the white content
        /// area so their hover shades are slightly different.
        /// </summary>
        private void AttachButtonAnimations()
        {
            // White header buttons (Install / Manage Mods / Docs) — hover slightly off-white,
            // press a touch deeper. Subtle so it reads as polish, not as state change.
            var headerIdle = Color.White;
            var headerHover = Color.FromArgb(240, 244, 250);
            var headerPress = Color.FromArgb(225, 232, 242);

            ButtonHoverAnimator.Attach(btnInstall, headerIdle, headerHover, headerPress);
            ButtonHoverAnimator.Attach(btnUninstall, headerIdle, headerHover, headerPress);
            ButtonHoverAnimator.Attach(btnDocs, headerIdle, headerHover, headerPress);

            // Browse buttons use the system control face — give them a faint blue tint on hover.
            var browseIdle = SystemColors.Control;
            var browseHover = Color.FromArgb(225, 235, 248);
            var browsePress = Color.FromArgb(205, 220, 240);

            // These default to UseVisualStyleBackColor=true; flip off so our BackColor wins.
            btnBrowseOiv.UseVisualStyleBackColor = false;
            btnBrowseGame.UseVisualStyleBackColor = false;
            btnBrowseOiv.FlatStyle = FlatStyle.Flat;
            btnBrowseGame.FlatStyle = FlatStyle.Flat;
            btnBrowseOiv.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            btnBrowseGame.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);

            ButtonHoverAnimator.Attach(btnBrowseOiv, browseIdle, browseHover, browsePress);
            ButtonHoverAnimator.Attach(btnBrowseGame, browseIdle, browseHover, browsePress);
            ButtonHoverAnimator.Attach(btnDone, headerIdle, headerHover, headerPress);
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && 
                   (files[0].EndsWith(".oiv", StringComparison.OrdinalIgnoreCase) || 
                    files[0].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string file = files[0];
                    if (file.EndsWith(".oiv", StringComparison.OrdinalIgnoreCase) || 
                        file.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                    {
                        txtOivPath.Text = file;
                        LoadOivPackage(file);
                    }
                }
            }
        }
        
        private void btnBrowseOiv_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select OIV or RPF Package";
                dlg.Filter = "OIV/RPF Packages (*.oiv;*.rpf)|*.oiv;*.rpf|All Files (*.*)|*.*";
                
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtOivPath.Text = dlg.FileName;
                    LoadOivPackage(dlg.FileName);
                }
            }
        }
        
        private void btnBrowseGame_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select GTA V Game Folder";
                if (_package != null && _package.IsFiveM)
                {
                    dlg.Description = "Select FiveM Application Data Folder (contains FiveM.app or FiveM.exe)";
                }
                dlg.ShowNewFolderButton = false;
                
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string selected = dlg.SelectedPath;
                    if (_package != null && _package.IsFiveM)
                    {
                        string modsFolder = selected;
                        if (Directory.Exists(Path.Combine(selected, "FiveM.app")))
                        {
                            modsFolder = Path.Combine(selected, "FiveM.app", "mods");
                        }
                        else if (File.Exists(Path.Combine(selected, "FiveM.exe")) && !Path.GetFileName(selected).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Directory.Exists(Path.Combine(selected, "FiveM.app")))
                            {
                                modsFolder = Path.Combine(selected, "FiveM.app", "mods");
                            }
                            else
                            {
                                modsFolder = Path.Combine(selected, "mods");
                            }
                        }
                        else if (!Path.GetFileName(selected).Equals("mods", StringComparison.OrdinalIgnoreCase))
                        {
                            modsFolder = Path.Combine(selected, "mods");
                        }
                        selected = modsFolder;
                    }

                    txtGameFolder.Text = selected;
                    _gameFolder = selected;
                    _spGameFolder = selected; // User explicitly selected this

                    // Update specific version slot if we know context
                    if (_package != null && _package.Metadata.GameVersion == GameVersion.Enhanced)
                    {
                        _gameFolderEnhanced = _gameFolder;
                    }
                    else if (_package != null && _package.Metadata.GameVersion == GameVersion.Legacy)
                    {
                        _gameFolderLegacy = _gameFolder;
                    }
                    else
                    {
                        // Fallback: If no package context, assume Legacy/Base is the primary one people set
                        // Or maybe update both if they are empty?
                        if (string.IsNullOrEmpty(_gameFolderLegacy)) _gameFolderLegacy = _gameFolder;
                    }

                    ValidateGameFolder();
                    UpdateInstallButton();
                    SaveConfig(); // Save on manual selection
                }
            }
        }
        
        // GetConfigPath removed, logic moved to OivAppConfig
        
        private void LoadConfig()
        {
             var config = OivAppConfig.Load();
             if (config != null)
             {
                 if (!string.IsNullOrEmpty(config.LastGameFolder) && Directory.Exists(config.LastGameFolder))
                 {
                     _gameFolder = config.LastGameFolder;
                     _spGameFolder = config.LastGameFolder;
                     txtGameFolder.Text = _gameFolder;
                     ValidateGameFolder();
                 }
                 
                 _gameFolderLegacy = config.GameFolderLegacy;
                 _gameFolderEnhanced = config.GameFolderEnhanced;
             }
        }
        
        private void SaveConfig()
        {
            string saveFolder = !string.IsNullOrEmpty(_spGameFolder) ? _spGameFolder : _gameFolder;
            if (string.IsNullOrEmpty(saveFolder)) return;

            var config = OivAppConfig.Load(); // Reload to keep other changes? Or just overwrite?
            // Simple overwrite for now as we are single instance typically
            config.LastGameFolder = saveFolder;
            config.GameFolderLegacy = _gameFolderLegacy;
            config.GameFolderEnhanced = _gameFolderEnhanced;
            
            OivAppConfig.Save(config);
        }
        
        // Internal OivConfig class removed

        private void btnUninstall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_gameFolder) || !Directory.Exists(_gameFolder))
            {
                MessageBox.Show("Please select a valid GTA V folder first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Pass BOTH the SP folder and the FiveM folder
            string spParam = !string.IsNullOrEmpty(_spGameFolder) && Directory.Exists(_spGameFolder) ? _spGameFolder : null;
            // If current _gameFolder is NOT FiveM, it might be SP, so use that if _spGameFolder is empty
            if (spParam == null && !string.IsNullOrEmpty(_gameFolder) && !_gameFolder.Contains("FiveM.app"))
                spParam = _gameFolder;

            using (var form = new UninstallForm(spParam, FiveMHelper.GetFiveMModsFolder()))
            {
                form.ShowDialog(this);
            }
        }

        private void btnDocs_Click(object sender, EventArgs e)
        {
            using (var form = new DocumentationForm())
            {
                form.ShowDialog(this);
            }
        }

        private void linkInstructions_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (_package == null) return;
            using (var form = new InstructionsForm(_package))
            {
                form.ShowDialog(this);
            }
        }

        // Designer wires this handler to tmrMarquee.Tick. Marquee scrolling now lives in
        // MarqueePainter (custom paint with sub-pixel positioning); the timer itself is
        // stopped in SetupMarqueePainter so this body never actually runs. Kept as a
        // no-op so the Designer-generated event subscription still resolves.
        private void tmrMarquee_Tick(object sender, EventArgs e) { }
        
        private void ValidateGameFolder()
        {
            if (string.IsNullOrEmpty(_gameFolder))
            {
                lblGameStatus.Text = "";
                lblGameStatus.ForeColor = Color.Gray;
                lblAsiStatus.Text = "";
                return;
            }

            bool hasLegacy = File.Exists(Path.Combine(_gameFolder, "GTA5.exe"));
            bool hasEnhanced = File.Exists(Path.Combine(_gameFolder, "GTA5_Enhanced.exe"));
            
            bool hasOpenIV = File.Exists(Path.Combine(_gameFolder, "OpenIV.asi"));
            bool hasOpenRPF = File.Exists(Path.Combine(_gameFolder, "OpenRPF.asi"));
            // RageOpenV is a unified open-source replacement (https://github.com/Chiheb-Bacha/RageOpenV)
            // that covers both Legacy (gen8, formerly OpenIV.asi) and Enhanced (gen9, formerly OpenRPF.asi)
            // mods-folder loading. Treat its presence as satisfying either requirement.
            bool hasRageOpenV = File.Exists(Path.Combine(_gameFolder, "RageOpenV.asi"));
            bool hasLegacyLoader = hasOpenIV || hasRageOpenV;
            bool hasEnhancedLoader = hasOpenRPF || hasRageOpenV;
            bool hasDinput8 = File.Exists(Path.Combine(_gameFolder, "dinput8.dll"));
            bool hasXinput = File.Exists(Path.Combine(_gameFolder, "xinput1_4.dll"));
            
            string asiStatus = "";
            
            // Check what version the package requires
            var requiredVersion = _package?.Metadata?.GameVersion ?? GameVersion.Any;
            
            if (_package != null && _package.IsFiveM)
            {
                // FiveM validation
                if (string.IsNullOrEmpty(_gameFolder))
                {
                    lblGameStatus.Text = "⚠ FiveM mods folder not found";
                    lblGameStatus.ForeColor = Color.Orange;
                }
                else
                {
                    lblGameStatus.Text = "✓ FiveM mods folder selected";
                    lblGameStatus.ForeColor = Color.Green;
                }
                lblAsiStatus.Text = "";
                return;
            }
            
            if (hasEnhanced)
            {
                if (requiredVersion == GameVersion.Legacy)
                {
                    lblGameStatus.Text = "⚠ Package requires GTA V Legacy, but this is Enhanced";
                    lblGameStatus.ForeColor = Color.Orange;
                }
                else
                {
                    lblGameStatus.Text = "✓ Valid GTA V folder (Enhanced)";
                    lblGameStatus.ForeColor = Color.Green;
                    
                    if (!hasXinput)
                        asiStatus = "⚠ ASI Loader (xinput1_4.dll) missing";
                    else if (!hasEnhancedLoader)
                        asiStatus = "⚠ OpenRPF.asi or RageOpenV.asi missing - mods folder disabled";
                }
            }
            else if (hasLegacy)
            {
                if (requiredVersion == GameVersion.Enhanced)
                {
                    lblGameStatus.Text = "⚠ Package requires GTA V Enhanced, but this is Legacy";
                    lblGameStatus.ForeColor = Color.Orange;
                }
                else
                {
                    lblGameStatus.Text = "✓ Valid GTA V folder (Legacy)";
                    lblGameStatus.ForeColor = Color.Green;

                    if (!hasDinput8)
                         asiStatus = "⚠ ASI Loader (dinput8.dll) missing";
                    else if (!hasLegacyLoader)
                        asiStatus = "⚠ OpenIV.asi or RageOpenV.asi missing - mods folder disabled";
                }
            }
            else
            {
                lblGameStatus.Text = "⚠ GTA5.exe or GTA5_Enhanced.exe not found";
                lblGameStatus.ForeColor = Color.Orange;
            }
            
            // Update ASI status label
            if (!string.IsNullOrEmpty(asiStatus))
            {
                lblAsiStatus.Text = asiStatus;
                lblAsiStatus.ForeColor = Color.Red;
            }
            else if (hasEnhanced || hasLegacy)
            {
                 // Prefer to surface RageOpenV when present since it's the unified loader
                 // covering both versions; otherwise fall back to the version-specific name.
                 if (hasRageOpenV)
                 {
                     lblAsiStatus.Text = "✓ RageOpenV.asi installed";
                     lblAsiStatus.ForeColor = Color.Green;
                 }
                 else if (hasEnhanced && hasOpenRPF)
                 {
                     lblAsiStatus.Text = "✓ OpenRPF.asi installed";
                     lblAsiStatus.ForeColor = Color.Green;
                 }
                 else if (hasLegacy && hasOpenIV)
                 {
                     lblAsiStatus.Text = "✓ OpenIV.asi installed";
                     lblAsiStatus.ForeColor = Color.Green;
                 }
                 else
                 {
                     lblAsiStatus.Text = "";
                 }
            }
            else
            {
                lblAsiStatus.Text = "";
            }

            
            // Enable Manage Mods if game folder is potentially valid (has exe)
            if (btnUninstall != null)
            {
                btnUninstall.Enabled = hasLegacy || hasEnhanced;
            }
        }

        private void LoadOivPackage(string path)
        {
            try
            {
                // Dispose previous package
                _package?.Dispose();
                _package = null;
                
                // Check if it's a folder (extracted OIV for testing)
                if (Directory.Exists(path))
                {
                    _package = OivPackage.LoadFromFolder(path);
                }
                else
                {
                    _package = OivPackage.Load(path);
                }
                
                DisplayPackageInfo();

                if (_package.IsFiveM)
                {
                    string fivemMods = FiveMHelper.GetFiveMModsFolder();
                    if (!string.IsNullOrEmpty(fivemMods))
                    {
                        _gameFolder = fivemMods;
                        txtGameFolder.Text = _gameFolder;
                    }
                    else
                    {
                        // Fallback or just show empty? 
                        // If FiveM app exists but mods folder doesn't, we can create it?
                        // FiveMHelper checks existance. 
                        // Let's manually construct it if FiveM is installed.
                        if (FiveMHelper.IsFiveMInstalled())
                        {
                            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                            string fiveMAppPath = Path.Combine(localAppData, "FiveM", "FiveM.app");
                            _gameFolder = Path.Combine(fiveMAppPath, "mods");
                            txtGameFolder.Text = _gameFolder;
                        }
                    }
                    ValidateGameFolder();
                }
                
                UpdateInstallButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load OIV package:\n\n{ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisplayPackageInfo()
        {
            if (_package == null) return;

            var meta = _package.Metadata;
            bool wasEmpty = panelEmptyState != null && panelEmptyState.Visible;

            // A package is now loaded — halt the idle pulse so the theme color
            // (default blue or the package's HeaderBackground) holds steady.
            _headerPulser?.Stop();

            // Update window title
            this.Text = $"{meta.Name} - Package Installer";

            // Header
            lblPackageName.Text = meta.Name;
            lblAuthor.Text = meta.AuthorDisplayName;

            // Description
            rtbDescription.Text = meta.Description.Trim();

            // Calculate height with a cap
            Size textSize = TextRenderer.MeasureText(rtbDescription.Text, rtbDescription.Font, new Size(rtbDescription.Width, 0), TextFormatFlags.WordBreak);

            // Cap height at 250px (scroll if larger), min 35px
            int targetHeight = Math.Min(Math.Max(textSize.Height + 20, 35), 250);

            rtbDescription.Height = targetHeight;

            // Compute the *loaded-state* positions for the secondary panels. When leaving
            // the empty state we defer applying these so the crossfade snapshot still
            // shows the empty-state layout — otherwise panelPaths would visibly teleport
            // before the fade kicks in.
            int spacer = 20;
            int linkY = rtbDescription.Bottom + 4;
            int linkH = Math.Max(linkInstructions.Height, 17);
            int loadedPathsTop = linkY + linkH + 10;
            int loadedInfoTop = loadedPathsTop + panelPaths.Height + spacer;
            int requiredClientHeight = 100 + loadedInfoTop + panelInfo.Height + 30;

            // Hardcoded minimum height to ensure basic UI usability
            int minHeight = 460;

            // Apply the required height, respecting the minimum
            int finalHeight = Math.Max(minHeight, requiredClientHeight);

            if (!wasEmpty)
            {
                linkInstructions.Location = new Point(20, linkY);
                panelPaths.Top = loadedPathsTop;
                panelInfo.Top = loadedInfoTop;
                panelAdditional.Top = loadedInfoTop;
                if (this.ClientSize.Height != finalHeight)
                {
                    AnimateClientHeight(finalHeight, durationMs: 260);
                }
            }
            
            // Information section
            linkAuthor.Text = meta.AuthorDisplayName;
            linkAuthor.Tag = meta.AuthorActionLink;
            lblVersion.Text = meta.Version;
            
            // Update supported game based on package version
            switch (meta.GameVersion)
            {
                case GameVersion.Enhanced:
                    lblGame.Text = "GTA V Enhanced";
                    break;
                case GameVersion.Legacy:
                    lblGame.Text = "GTA V Legacy";
                    break;
                default:
                    lblGame.Text = "GTA V";
                    break;
            }

            if (_package.IsFiveM)
            {
                lblGame.Text = "FiveM";
                lblGame.ForeColor = Color.OrangeRed;
            }
            else
            {
                lblGame.ForeColor = Color.Black; 
            }
            
            // Re-validate game folder if already set
            if (!string.IsNullOrEmpty(_gameFolder))
            {
                ValidateGameFolder();
            }
            
            // Set icon if available
            if (_package.IconData != null && _package.IconData.Length > 0)
            {
                try
                {
                    using (var ms = new MemoryStream(_package.IconData))
                    {
                        picIcon.Image = Image.FromStream(ms);
                    }
                }
                catch { }
            }

            // Reset default theme (Standard Blue)
            Color defaultBlue = Color.FromArgb(0, 120, 215);
            linkAuthor.LinkColor = defaultBlue;
            linkWeb.LinkColor = defaultBlue;
            linkYoutube.LinkColor = defaultBlue;
            btnInstall.ForeColor = Color.Black; 
            btnInstall.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            panelHeader.BackColor = defaultBlue;
            lblPackageName.ForeColor = Color.White;
            lblAuthor.ForeColor = Color.White;
            lblWarning.ForeColor = Color.White;

            // Apply header color if specified
            if (!string.IsNullOrEmpty(meta.HeaderBackground))
            {
                try
                {
                    string colorStr = meta.HeaderBackground.TrimStart('$');
                    if (colorStr.Length == 8)
                    {
                        int argb = Convert.ToInt32(colorStr, 16);
                        Color headerColor = Color.FromArgb(argb);
                        // WinForms doesn't support true alpha transparency on panels.
                        // Force fully opaque to avoid mismatched child control backgrounds.
                        headerColor = Color.FromArgb(255, headerColor.R, headerColor.G, headerColor.B);
                        panelHeader.BackColor = headerColor;
                        
                        Color textColor = meta.UseBlackTextColor ? Color.Black : Color.White;
                        lblPackageName.ForeColor = textColor;
                        lblAuthor.ForeColor = textColor;
                        lblWarning.ForeColor = Color.FromArgb(textColor.A, 
                            (int)(textColor.R * 0.8), (int)(textColor.G * 0.8), (int)(textColor.B * 0.8));
                            
                        // Apply dynamic theme to content controls (if Header is dark enough to be visible on white)
                        // If UseBlackTextColor is FALSE, it means Header is Dark (White text used). 
                        // Dark colors work well as text/accents on White backgrounds.
                        if (!meta.UseBlackTextColor)
                        {
                            linkAuthor.LinkColor = headerColor;
                            linkWeb.LinkColor = headerColor;
                            linkYoutube.LinkColor = headerColor;
                            
                            btnInstall.ForeColor = headerColor;
                            btnInstall.FlatAppearance.BorderColor = headerColor;
                        }
                    }
                }
                catch { }
            }
            
            // Additional links
            DisplayAuthorLinks();

            // Sync the marquee with whatever lblPackageName ended up showing — text and
            // (theme-driven) ForeColor — and reset its scroll position for a fresh sweep.
            SyncMarqueeFromLabel();

            // Empty → loaded transition. Crossfade panelContent so the empty-state card
            // smoothly hands off to the description/info/additional sections (and the
            // panelPaths repositioning), while the header grows from 70→100 and slides
            // the title back to its loaded position alongside the form's height tween.
            if (wasEmpty)
            {
                ViewTransitions.CrossFade(panelContent, () =>
                {
                    linkInstructions.Location = new Point(20, linkY);
                    linkInstructions.Visible = true;
                    panelPaths.Top = loadedPathsTop;
                    panelInfo.Top = loadedInfoTop;
                    panelAdditional.Top = loadedInfoTop;
                    panelEmptyState.Visible = false;
                    rtbDescription.Visible = true;
                    panelInfo.Visible = true;
                    panelAdditional.Visible = true;
                    picIcon.Visible = true;
                    lblAuthor.Visible = true;
                });
                AnimateLayoutTransition(finalHeight, targetHeaderHeight: 100, durationMs: 280);
            }
        }

        /// <summary>
        /// One-shot transition tween covering the form's ClientSize.Height, the header's
        /// Height, and the title clip's location — used when leaving the empty state so
        /// the header can grow (70→100) and the title can slide back (24,18 → 93,15)
        /// while the form expands to fit the loaded layout.
        /// </summary>
        private void AnimateLayoutTransition(int targetClientHeight, int targetHeaderHeight, int durationMs)
        {
            // Empty → loaded: reveal Install from just off-screen right; end positions
            // are right-anchored against the current form width.
            //   btnDocs:      Right margin 260 → Left = form_w - 100 - 260 = form_w - 360
            //   btnUninstall: Right margin 150 → Left = form_w - 100 - 150 = form_w - 250
            //   btnInstall:   Right margin  20 → Left = form_w - 120 -  20 = form_w - 140
            int formWidth = this.ClientSize.Width;
            btnInstall.Left = formWidth;
            btnInstall.Visible = true;
            AnimateHeaderTransition(
                targetClientHeight, targetHeaderHeight,
                targetTitle: new Point(93, 15),
                targetDocsX: formWidth - 360,
                targetUninstallX: formWidth - 250,
                targetInstallX: formWidth - 140,
                hideInstallAtEnd: false,
                durationMs);
        }

        private void AnimateLayoutTransitionToEmpty(int durationMs)
        {
            // Loaded → empty: slide Install off-screen right and reset Docs/Manage Mods
            // to their empty-state slots (right margins 130 / 20).
            int formWidth = this.ClientSize.Width;
            AnimateHeaderTransition(
                targetClientHeight: 380, targetHeaderHeight: 70,
                targetTitle: new Point(24, 18),
                targetDocsX: formWidth - 230,
                targetUninstallX: formWidth - 120,
                targetInstallX: formWidth,
                hideInstallAtEnd: true,
                durationMs);
        }

        /// <summary>
        /// Shared header-layout tween for both transition directions. Handles client
        /// height, header height, title clip location, and the three header buttons in
        /// one easing curve so the motion reads as a single coordinated reflow.
        /// </summary>
        private void AnimateHeaderTransition(
            int targetClientHeight, int targetHeaderHeight,
            Point targetTitle,
            int targetDocsX, int targetUninstallX, int targetInstallX,
            bool hideInstallAtEnd, int durationMs)
        {
            int startClient = this.ClientSize.Height;
            int startHeader = panelHeader.Height;
            Point startTitle = pnlTitleClipping.Location;
            int startDocsX = btnDocs.Left;
            int startUninstallX = btnUninstall.Left;
            int startInstallX = btnInstall.Left;

            int dClient = targetClientHeight - startClient;
            int dHeader = targetHeaderHeight - startHeader;
            int dTitleX = targetTitle.X - startTitle.X;
            int dTitleY = targetTitle.Y - startTitle.Y;
            int dDocsX = targetDocsX - startDocsX;
            int dUninstallX = targetUninstallX - startUninstallX;
            int dInstallX = targetInstallX - startInstallX;
            if (dClient == 0 && dHeader == 0 && dTitleX == 0 && dTitleY == 0
                && dDocsX == 0 && dUninstallX == 0 && dInstallX == 0)
            {
                if (hideInstallAtEnd) btnInstall.Visible = false;
                return;
            }

            _formResizeAnimator?.Dispose();
            _formResizeAnimator = new Animator();
            _formResizeAnimator.Tween(durationMs, t =>
            {
                if (this.IsDisposed) return;
                int h = startClient + (int)Math.Round(dClient * t);
                int hh = startHeader + (int)Math.Round(dHeader * t);
                int tx = startTitle.X + (int)Math.Round(dTitleX * t);
                int ty = startTitle.Y + (int)Math.Round(dTitleY * t);
                int dx = startDocsX + (int)Math.Round(dDocsX * t);
                int ux = startUninstallX + (int)Math.Round(dUninstallX * t);
                int ix = startInstallX + (int)Math.Round(dInstallX * t);
                if (panelHeader.Height != hh) panelHeader.Height = hh;
                if (this.ClientSize.Height != h)
                    this.ClientSize = new Size(this.ClientSize.Width, h);
                if (pnlTitleClipping.Left != tx || pnlTitleClipping.Top != ty)
                    pnlTitleClipping.Location = new Point(tx, ty);
                if (btnDocs.Left != dx) btnDocs.Left = dx;
                if (btnUninstall.Left != ux) btnUninstall.Left = ux;
                if (btnInstall.Left != ix) btnInstall.Left = ix;
            }, () =>
            {
                if (this.IsDisposed) return;
                panelHeader.Height = targetHeaderHeight;
                this.ClientSize = new Size(this.ClientSize.Width, targetClientHeight);
                pnlTitleClipping.Location = targetTitle;
                btnDocs.Left = targetDocsX;
                btnUninstall.Left = targetUninstallX;
                btnInstall.Left = targetInstallX;
                if (hideInstallAtEnd) btnInstall.Visible = false;
            }, Easing.EaseOutCubic);
        }

        /// <summary>
        /// Smoothly tweens the form's ClientSize.Height between current and target.
        /// Replaces the previous direct assignment so a new package's content size
        /// settles instead of snapping. Width is preserved.
        /// </summary>
        private void AnimateClientHeight(int targetHeight, int durationMs)
        {
            int start = this.ClientSize.Height;
            int delta = targetHeight - start;
            if (delta == 0) return;

            _formResizeAnimator?.Dispose();
            _formResizeAnimator = new Animator();
            _formResizeAnimator.Tween(durationMs, t =>
            {
                if (this.IsDisposed) return;
                int h = start + (int)Math.Round(delta * t);
                if (this.ClientSize.Height != h)
                    this.ClientSize = new Size(this.ClientSize.Width, h);
            }, () =>
            {
                if (!this.IsDisposed)
                    this.ClientSize = new Size(this.ClientSize.Width, targetHeight);
            }, Easing.EaseOutCubic);
        }

        private void DisplayAuthorLinks()
        {
            if (_package == null) return;
            
            var meta = _package.Metadata;
            
            // Web Link
            if (!string.IsNullOrEmpty(meta.AuthorWeb))
            {
                linkWeb.Tag = meta.AuthorWeb;
                linkWeb.Visible = true;
            }
            else
            {
                linkWeb.Visible = false;
            }
            
            // YouTube
            if (!string.IsNullOrEmpty(meta.AuthorYoutube))
            {
                string youtubeUrl = meta.AuthorYoutube;
                if (!youtubeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    youtubeUrl = $"https://www.youtube.com/c/{meta.AuthorYoutube}";
                }
                linkYoutube.Tag = youtubeUrl;
                linkYoutube.Visible = true;
            }
            else
            {
                linkYoutube.Visible = false;
            }
        }
        
        private void UpdateInstallButton()
        {
            btnInstall.Enabled = _package != null && !string.IsNullOrEmpty(_gameFolder);
        }
        
        private void lblAuthor_Click(object sender, EventArgs e)
        {
            if (_package?.Metadata?.AuthorActionLink != null)
            {
                OpenUrl(_package.Metadata.AuthorActionLink);
            }
        }
        
        private void linkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (sender is LinkLabel link && link.Tag is string url && !string.IsNullOrEmpty(url))
            {
                OpenUrl(url);
            }
        }
        
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnInstall_Click(object sender, EventArgs e)
        {
            if (_package == null || string.IsNullOrEmpty(_gameFolder)) return;

            // Check for running game process
            while (ProcessHelper.IsGameRunning(out string processName))
            {
                var result = MessageBox.Show(
                    $"The game process '{processName}' is currently running.\n\n" +
                    "Please close the game before installing mods to prevent file locking errors.",
                    "Game is Running",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel) return;
            }

            if (_package.IsFiveM)
            {
                // FiveM Installation Logic
                if (!Directory.Exists(_gameFolder))
                {
                    try 
                    {
                        Directory.CreateDirectory(_gameFolder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to create mods folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                string sourcePath = _package.SourceRpf.FilePath;
                string destPath = Path.Combine(_gameFolder, Path.GetFileName(sourcePath));

                if (File.Exists(destPath))
                {
                    var result = MessageBox.Show(
                        $"File '{Path.GetFileName(destPath)}' already exists in FiveM mods folder.\nOverwrite?", 
                        "Confirm Overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                try
                {
                    // Create uninstaller log
                    var manager = new BackupManager(_gameFolder);
                    var session = manager.CreateSession(
                        _package.Metadata.Name, 
                        _package.Metadata.Description, 
                        _package.Metadata.Version, 
                        false // FiveM RPFs are generally not "Gen9" in the console sense, or we don't care about encryption here
                    );

                    string fileName = Path.GetFileName(sourcePath);
                    session.TrackFileAdded(fileName);
                    
                    File.Copy(sourcePath, destPath, true);
                    
                    session.Save();
                    
                    MessageBox.Show("FiveM mod installed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to install mod: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            // Check for existing installations with the same name
            var backupManager = new BackupManager(_gameFolder);
            var existingPackages = backupManager.GetInstalledPackages()
                .Where(x => x.PackageName == _package.Metadata.Name)
                .ToList();

            List<BackupLog> packagesToUninstall = new List<BackupLog>();
            UninstallMode uninstallMode = UninstallMode.Backup;

            if (existingPackages.Count > 0)
            {
                // We need a custom dialog for 3 choices: Uninstall (Backup), Uninstall (Vanilla), Keep
                using (var prompt = new Form())
                {
                    prompt.Width = 500;
                    prompt.Height = 280;
                    prompt.Text = "Conflicting Installation Found";
                    prompt.StartPosition = FormStartPosition.CenterParent;
                    prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                    prompt.MaximizeBox = false;
                    prompt.MinimizeBox = false;

                    var iconBox = new PictureBox { Image = SystemIcons.Warning.ToBitmap(), Size = new Size(32, 32), Location = new Point(20, 20) };
                    prompt.Controls.Add(iconBox);

                    var lbl = new Label 
                    { 
                        Text = $"Found {existingPackages.Count} existing installation(s) of '{_package.Metadata.Name}'.\n\n" +
                               "How would you like to proceed?",
                        Location = new Point(70, 20),
                        Size = new Size(400, 60)
                    };
                    prompt.Controls.Add(lbl);

                    // Option 1: Revert to Backup (Standard)
                    var btnBackup = new Button 
                    { 
                        Text = "Uninstall & Revert to Backups\n(Standard Update)", 
                        Location = new Point(70, 90), 
                        Size = new Size(380, 40),
                        DialogResult = DialogResult.Yes
                    };
                    btnBackup.Click += (s, ev) => { uninstallMode = UninstallMode.Backup; prompt.Close(); };
                    prompt.Controls.Add(btnBackup);

                    // Option 2: Revert to Vanilla
                    var btnVanilla = new Button 
                    { 
                        Text = "Uninstall & Reset to Vanilla\n(Clean Reinstall)", 
                        Location = new Point(70, 135), 
                        Size = new Size(380, 40),
                        DialogResult = DialogResult.OK
                    };
                    btnVanilla.Click += (s, ev) => { uninstallMode = UninstallMode.Vanilla; prompt.Close(); };
                    prompt.Controls.Add(btnVanilla);

                    // Option 3: Keep (Stacking)
                    var btnKeep = new Button 
                    { 
                        Text = "Keep Existing Files\n(Install on top / Stacking)", 
                        Location = new Point(70, 180), 
                        Size = new Size(380, 40),
                        DialogResult = DialogResult.No
                    };
                    btnKeep.Click += (s, ev) => { prompt.Close(); };
                    prompt.Controls.Add(btnKeep);

                    var result = prompt.ShowDialog(this);
                    
                    if (result == DialogResult.Cancel) return; // Closed window
                    
                    if (result == DialogResult.Yes || result == DialogResult.OK)
                    {
                        packagesToUninstall = existingPackages;
                    }
                    else if (result == DialogResult.No)
                    {
                        // Proceed without uninstalling
                    }
                }
            }

            // Snapshot the skip-backup choice before we leave the UI thread.
            bool skipBackup = chkSkipBackup.Checked;

            // Switch to log view
            ShowInstallLog();

            // Bridge OivInstaller progress reports into the SmoothProgressBar on the UI thread.
            var progress = new Progress<InstallProgress>(p =>
            {
                if (progressBar.IsDisposed) return;
                progressBar.SetValue(p.Percent);
            });

            // Run installation in background to keep UI responsive
            Task.Run(() =>
            {
                try
                {
                    Log("Initializing installation...");
                    Log($"Package: {_package.Metadata.Name}");
                    Log($"Target: {_gameFolder}");
                    if (skipBackup)
                        Log("Backup creation: DISABLED (Manage Mods will not see this install)");

                    var installer = new OivInstaller(_gameFolder, _package, message => Log(message));
                    installer.Install(progress, packagesToUninstall, uninstallMode, skipBackup);
                    
                    Log(""); // Spacer
                    Log("Installation completed successfully.");
                    Log("----------------------------------------");
                }
                catch (Exception ex)
                {
                    Log("");
                    Log("ERROR: Installation failed!");
                    Log(ex.Message);
                    Log(ex.StackTrace);
                }
                finally
                {
                    // Enable Done button on UI thread
                    this.Invoke((MethodInvoker)delegate {
                        btnDone.Enabled = true;
                        btnDone.Visible = true;
                    });
                }
            });
        }
        
        private void btnDone_Click(object sender, EventArgs e)
        {
            ShowMainView();
        }
        
        private void ShowInstallLog()
        {
            // Reset the progress bar before the crossfade so the new view comes in clean.
            progressBar.Reset();
            rtbLog.Clear();
            btnDone.Enabled = false;
            btnDone.Visible = false;

            // Disable main buttons during install
            btnInstall.Enabled = false;
            btnUninstall.Enabled = false;

            // Crossfade panelContent's children: hide the main view, reveal the log view.
            ViewTransitions.CrossFade(panelContent, () =>
            {
                panelPaths.Visible = false;
                panelInfo.Visible = false;
                panelAdditional.Visible = false;
                rtbDescription.Visible = false;

                panelLog.Visible = true;
                panelLog.BringToFront();
            });
        }

        private void ShowMainView()
        {
            // Drop the loaded package and roll the form back to its empty-state landing.
            // After a Done click the user's natural next action is "install another mod"
            // or "close the app" — both are served by a clean slate. Keeping the picked
            // package visible would also make the now-enabled Install button ambiguous
            // (already installed? install again?).
            _package?.Dispose();
            _package = null;
            txtOivPath.Text = "";
            this.Text = "CodeWalker - Package Installer";
            if (picIcon.Image != null)
            {
                var old = picIcon.Image;
                picIcon.Image = null;
                old.Dispose();
            }
            lblPackageName.Text = "Select Package";
            lblAuthor.Text = "";

            // Reset theme to default (the loaded package may have themed the header).
            Color defaultBlue = Color.FromArgb(0, 120, 215);
            panelHeader.BackColor = defaultBlue;
            lblPackageName.ForeColor = Color.White;
            lblAuthor.ForeColor = Color.White;
            lblWarning.ForeColor = Color.White;
            linkAuthor.LinkColor = defaultBlue;
            linkWeb.LinkColor = defaultBlue;
            linkYoutube.LinkColor = defaultBlue;
            btnInstall.ForeColor = Color.Black;
            btnInstall.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            SyncMarqueeFromLabel();

            // Crossfade panelContent: hide log + loaded sections, reveal empty-state card.
            // panelPaths is restored to its empty-state Y and made visible again — it
            // was hidden by ShowInstallLog when the install began.
            ViewTransitions.CrossFade(panelContent, () =>
            {
                panelLog.Visible = false;
                panelPaths.Top = 160;
                panelPaths.Visible = true;
                panelEmptyState.Visible = true;
                rtbDescription.Visible = false;
                panelInfo.Visible = false;
                panelAdditional.Visible = false;
                picIcon.Visible = false;
                lblAuthor.Visible = false;
                linkInstructions.Visible = false;
            });

            // Run the inverse header/form animation in lockstep with the crossfade.
            AnimateLayoutTransitionToEmpty(durationMs: 280);

            // Restart the idle pulse now that we're back on the empty landing.
            _headerPulser?.Start();

            // Install is disabled in the empty state until a package is picked again.
            btnInstall.Enabled = false;
            btnUninstall.Enabled = true;
            ValidateGameFolder();
        }
        
        private void Log(string message)
        {
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string>(Log), message);
                return;
            }
            rtbLog.AppendText(message + Environment.NewLine);
            rtbLog.ScrollToCaret();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _package?.Dispose();
            _marquee?.Dispose();
            _formFadeAnimator?.Dispose();
            _formResizeAnimator?.Dispose();
            _headerPulser?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
