namespace CodeWalker.OIVInstaller
{
    partial class UninstallForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.tabs = new System.Windows.Forms.TabControl();
            this.tabPackages = new System.Windows.Forms.TabPage();
            this.tabAddons = new System.Windows.Forms.TabPage();
            this.lstPackages = new System.Windows.Forms.ListBox();
            this.btnUninstall = new System.Windows.Forms.Button();
            this.lstAddons = new System.Windows.Forms.ListView();
            this.colAddonName = new System.Windows.Forms.ColumnHeader();
            this.colAddonStatus = new System.Windows.Forms.ColumnHeader();
            this.colAddonPath = new System.Windows.Forms.ColumnHeader();
            this.btnRefreshAddons = new System.Windows.Forms.Button();
            this.btnApplyAddons = new System.Windows.Forms.Button();
            this.lblAddonsHint = new System.Windows.Forms.Label();
            this.btnClose = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.tabs.SuspendLayout();
            this.tabPackages.SuspendLayout();
            this.tabAddons.SuspendLayout();
            this.SuspendLayout();
            //
            // tabs
            //
            this.tabs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.tabs.Controls.Add(this.tabPackages);
            this.tabs.Controls.Add(this.tabAddons);
            this.tabs.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.tabs.Location = new System.Drawing.Point(12, 12);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(616, 360);
            this.tabs.TabIndex = 0;
            this.tabs.SelectedIndexChanged += new System.EventHandler(this.tabs_SelectedIndexChanged);
            //
            // tabPackages
            //
            this.tabPackages.Controls.Add(this.btnUninstall);
            this.tabPackages.Controls.Add(this.lstPackages);
            this.tabPackages.Location = new System.Drawing.Point(4, 24);
            this.tabPackages.Name = "tabPackages";
            this.tabPackages.Padding = new System.Windows.Forms.Padding(8);
            this.tabPackages.Size = new System.Drawing.Size(608, 332);
            this.tabPackages.TabIndex = 0;
            this.tabPackages.Text = "Installed Packages";
            this.tabPackages.UseVisualStyleBackColor = true;
            //
            // lstPackages
            //
            this.lstPackages.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.lstPackages.Font = new System.Drawing.Font("Segoe UI", 9.75F);
            this.lstPackages.FormattingEnabled = true;
            this.lstPackages.ItemHeight = 17;
            this.lstPackages.Location = new System.Drawing.Point(12, 12);
            this.lstPackages.Name = "lstPackages";
            this.lstPackages.Size = new System.Drawing.Size(584, 276);
            this.lstPackages.TabIndex = 0;
            this.lstPackages.SelectedIndexChanged += new System.EventHandler(this.lstPackages_SelectedIndexChanged);
            //
            // btnUninstall
            //
            this.btnUninstall.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnUninstall.Enabled = false;
            this.btnUninstall.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnUninstall.Location = new System.Drawing.Point(496, 296);
            this.btnUninstall.Name = "btnUninstall";
            this.btnUninstall.Size = new System.Drawing.Size(100, 28);
            this.btnUninstall.TabIndex = 1;
            this.btnUninstall.Text = "Uninstall";
            this.btnUninstall.UseVisualStyleBackColor = true;
            this.btnUninstall.Click += new System.EventHandler(this.btnUninstall_Click);
            //
            // tabAddons
            //
            this.tabAddons.Controls.Add(this.lblAddonsHint);
            this.tabAddons.Controls.Add(this.btnApplyAddons);
            this.tabAddons.Controls.Add(this.btnRefreshAddons);
            this.tabAddons.Controls.Add(this.lstAddons);
            this.tabAddons.Location = new System.Drawing.Point(4, 24);
            this.tabAddons.Name = "tabAddons";
            this.tabAddons.Padding = new System.Windows.Forms.Padding(8);
            this.tabAddons.Size = new System.Drawing.Size(608, 332);
            this.tabAddons.TabIndex = 1;
            this.tabAddons.Text = "DLC Add-ons";
            this.tabAddons.UseVisualStyleBackColor = true;
            //
            // lstAddons
            //
            this.lstAddons.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.lstAddons.CheckBoxes = true;
            this.lstAddons.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.colAddonName,
                this.colAddonStatus,
                this.colAddonPath});
            this.lstAddons.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lstAddons.FullRowSelect = true;
            this.lstAddons.HideSelection = false;
            this.lstAddons.Location = new System.Drawing.Point(12, 12);
            this.lstAddons.Name = "lstAddons";
            this.lstAddons.Size = new System.Drawing.Size(584, 276);
            this.lstAddons.TabIndex = 0;
            this.lstAddons.UseCompatibleStateImageBehavior = false;
            this.lstAddons.View = System.Windows.Forms.View.Details;
            this.lstAddons.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.lstAddons_ItemCheck);
            this.lstAddons.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.lstAddons_ItemChecked);
            //
            // colAddonName
            //
            this.colAddonName.Text = "Name";
            this.colAddonName.Width = 200;
            //
            // colAddonStatus
            //
            this.colAddonStatus.Text = "Status";
            this.colAddonStatus.Width = 90;
            //
            // colAddonPath
            //
            this.colAddonPath.Text = "Folder";
            this.colAddonPath.Width = 280;
            //
            // btnRefreshAddons
            //
            this.btnRefreshAddons.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRefreshAddons.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnRefreshAddons.Location = new System.Drawing.Point(12, 296);
            this.btnRefreshAddons.Name = "btnRefreshAddons";
            this.btnRefreshAddons.Size = new System.Drawing.Size(100, 28);
            this.btnRefreshAddons.TabIndex = 1;
            this.btnRefreshAddons.Text = "Reload";
            this.btnRefreshAddons.UseVisualStyleBackColor = true;
            this.btnRefreshAddons.Click += new System.EventHandler(this.btnRefreshAddons_Click);
            //
            // btnApplyAddons
            //
            this.btnApplyAddons.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnApplyAddons.Enabled = false;
            this.btnApplyAddons.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnApplyAddons.Location = new System.Drawing.Point(496, 296);
            this.btnApplyAddons.Name = "btnApplyAddons";
            this.btnApplyAddons.Size = new System.Drawing.Size(100, 28);
            this.btnApplyAddons.TabIndex = 3;
            this.btnApplyAddons.Text = "Apply";
            this.btnApplyAddons.UseVisualStyleBackColor = true;
            this.btnApplyAddons.Click += new System.EventHandler(this.btnApplyAddons_Click);
            //
            // lblAddonsHint
            //
            this.lblAddonsHint.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblAddonsHint.AutoSize = true;
            this.lblAddonsHint.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.lblAddonsHint.ForeColor = System.Drawing.Color.Gray;
            this.lblAddonsHint.Location = new System.Drawing.Point(120, 302);
            this.lblAddonsHint.Name = "lblAddonsHint";
            this.lblAddonsHint.Size = new System.Drawing.Size(0, 13);
            this.lblAddonsHint.TabIndex = 2;
            this.lblAddonsHint.Text = "";
            //
            // btnClose
            //
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnClose.Location = new System.Drawing.Point(540, 388);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(88, 28);
            this.btnClose.TabIndex = 1;
            this.btnClose.Text = "Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.lblStatus.ForeColor = System.Drawing.Color.Gray;
            this.lblStatus.Location = new System.Drawing.Point(16, 394);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 13);
            this.lblStatus.TabIndex = 2;
            //
            // progressBar
            //
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(16, 420);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(612, 5);
            this.progressBar.TabIndex = 3;
            this.progressBar.Visible = false;
            //
            // UninstallForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(640, 432);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.tabs);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(560, 380);
            this.Name = "UninstallForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Manage Mods";
            this.tabs.ResumeLayout(false);
            this.tabPackages.ResumeLayout(false);
            this.tabAddons.ResumeLayout(false);
            this.tabAddons.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage tabPackages;
        private System.Windows.Forms.TabPage tabAddons;
        private System.Windows.Forms.ListBox lstPackages;
        private System.Windows.Forms.Button btnUninstall;
        private System.Windows.Forms.ListView lstAddons;
        private System.Windows.Forms.ColumnHeader colAddonName;
        private System.Windows.Forms.ColumnHeader colAddonStatus;
        private System.Windows.Forms.ColumnHeader colAddonPath;
        private System.Windows.Forms.Button btnRefreshAddons;
        private System.Windows.Forms.Button btnApplyAddons;
        private System.Windows.Forms.Label lblAddonsHint;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ProgressBar progressBar;
    }
}
