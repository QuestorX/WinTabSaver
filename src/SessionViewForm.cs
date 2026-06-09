using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinTabSaver
{
    /// <summary>
    /// A simple dialog that shows all currently open Explorer windows and their
    /// tab paths in a read-only tree view.
    /// </summary>
    public sealed class SessionViewForm : Form
    {
        private readonly TreeView _tree;
        private readonly Button   _btnRefresh;
        private readonly Button   _btnClose;

        public SessionViewForm()
        {
            // -- Form properties ------------------------------------------------
            Text            = "WinTabSaver – Open Explorer Windows";
            Size            = new Size(560, 460);
            MinimumSize     = new Size(420, 320);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon            = IconFactory.CreateAppIcon();

            // -- Controls -------------------------------------------------------
            _tree = new TreeView
            {
                Dock        = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                ShowLines   = true,
                ShowPlusMinus = true,
                FullRowSelect = true,
                Font        = new Font("Segoe UI", 9.5f)
            };

            var panel = new Panel
            {
                Dock   = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6)
            };

            _btnRefresh = new Button
            {
                Text   = "Refresh",
                Width  = 90,
                Height = 30,
                Left   = 0,
                Top    = 7,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            _btnRefresh.Click += (_, __) => PopulateTree();

            _btnClose = new Button
            {
                Text     = "Close",
                Width    = 90,
                Height   = 30,
                Anchor   = AnchorStyles.Right | AnchorStyles.Bottom,
                DialogResult = DialogResult.OK
            };
            _btnClose.Left = panel.Width - _btnClose.Width - 8;
            panel.SizeChanged += (_, __) =>
                _btnClose.Left = panel.Width - _btnClose.Width - 8;

            panel.Controls.AddRange(new Control[] { _btnRefresh, _btnClose });

            var separator = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 1,
                BackColor = Color.LightGray
            };

            Controls.AddRange(new Control[] { _tree, separator, panel });

            // -- Initial data ---------------------------------------------------
            PopulateTree();
        }

        /// <summary>
        /// Refreshes the tree view with the current Explorer session data.
        /// </summary>
        private void PopulateTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            try
            {
                ExplorerSession session = ExplorerInterop.CaptureCurrentSession();

                if (session.Windows.Count == 0)
                {
                    _tree.Nodes.Add(new TreeNode("No open Explorer windows detected."));
                }
                else
                {
                    for (int i = 0; i < session.Windows.Count; i++)
                    {
                        ExplorerWindowInfo win = session.Windows[i];

                        string geometry = win.IsMaximized
                            ? "Maximized"
                            : $"{win.Width}×{win.Height}  @  {win.Left},{win.Top}";

                        var winNode = new TreeNode($"Window {i + 1}   [{geometry}]")
                        {
                            ImageIndex         = 0,
                            SelectedImageIndex = 0,
                            NodeFont           = new Font("Segoe UI", 9.5f, FontStyle.Bold)
                        };

                        for (int t = 0; t < win.Tabs.Count; t++)
                        {
                            string label = t == 0
                                ? $"[Active]  {win.Tabs[t]}"
                                : $"           {win.Tabs[t]}";

                            winNode.Nodes.Add(new TreeNode(label));
                        }

                        _tree.Nodes.Add(winNode);
                        winNode.Expand();
                    }
                }

                // Status line
                _tree.Nodes.Add(new TreeNode(
                    $"─── {session.Windows.Count} window(s) captured at {DateTime.Now:HH:mm:ss} ───")
                {
                    ForeColor = Color.Gray
                });
            }
            catch (Exception ex)
            {
                _tree.Nodes.Add(new TreeNode($"Error: {ex.Message}") { ForeColor = Color.Red });
            }
            finally
            {
                _tree.EndUpdate();
            }
        }
    }
}
