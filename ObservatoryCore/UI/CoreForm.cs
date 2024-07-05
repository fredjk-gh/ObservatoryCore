﻿using Observatory.Framework.Interfaces;
using Observatory.PluginManagement;
using Observatory.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Observatory.UI
{
    public partial class CoreForm : Form
    {
        private readonly ThemeManager themeManager;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, Int32 wMsg, bool wParam, Int32 lParam);
        private const int WM_SETREDRAW = 11;
        private static void SuspendDrawing(Control control)
        {
            if (SendMessage(control.Handle, WM_SETREDRAW, false, 0) != 0)
                throw new Exception("Unexpected error when suspending form draw events.");
        }

        private static void ResumeDrawing(Control control)
        {
            if (SendMessage(control.Handle, WM_SETREDRAW, true, 0) != 0)
                throw new Exception("Unexpected error when resuming form draw events.");

            control.Refresh();
        }

        public CoreForm()
        {
            DoubleBuffered = true;
            InitializeComponent();

            PopulateDropdownOptions();
            PopulateNativeSettings();

            ColourListHeader(ref PluginList, Color.DarkSlateGray, Color.LightGray);
            PopulatePluginList();
            FitColumns();
            string version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0";
            Text += $" - v{version}";

            DisableOverriddenNotification();

            themeManager = ThemeManager.GetInstance;
            themeManager.RegisterControl(this);

            foreach (var theme in themeManager.GetThemes)
            {
                ThemeDropdown.Items.Add(theme);
            }
            ThemeDropdown.SelectedItem = themeManager.CurrentTheme;
            CreatePluginTabs();
            RestoreSavedTab();
        }

        public void FocusPlugin(string pluginShortName)
        {
            var pluginTab = FindMenuItemForPlugin(pluginShortName);
            if (pluginTab != null)
            {
                SuspendDrawing(this);
                CoreTabControl.SelectedTab = pluginTab;
                ResumeDrawing(this);
            }
        }

        private TabPage? FindMenuItemForPlugin(string pluginShortName)
        {
            foreach (TabPage tab in CoreTabControl.TabPages)
            {
                if (tab.Text == pluginShortName)
                {
                    return tab;
                }
            }
            return null;
        }

        private readonly Dictionary<TabPage, IObservatoryPlugin> pluginList = [];

        private void ToggleMonitorButton_Click(object sender, EventArgs e)
        {
            if ((LogMonitor.GetInstance.CurrentState & Framework.LogMonitorState.Realtime) == Framework.LogMonitorState.Realtime)
            {
                LogMonitor.GetInstance.Stop();
                ToggleMonitorButton.Text = "Start Monitor";
            }
            else
            {
                LogMonitor.GetInstance.Start();
                ToggleMonitorButton.Text = "Stop Monitor";
            }
        }

        private static void ColourListHeader(ref NoHScrollList list, Color backColor, Color foreColor)
        {
            list.OwnerDraw = true;

            list.DrawColumnHeader +=
                new DrawListViewColumnHeaderEventHandler
                (
                    (sender, e) => HeaderDraw(sender, e, backColor, foreColor)
                );
            list.DrawItem += new DrawListViewItemEventHandler(BodyDraw);
        }

        private static void HeaderDraw(object? _, DrawListViewColumnHeaderEventArgs e, Color backColor, Color foreColor)
        {
            using (SolidBrush backBrush = new(backColor))
            {
                e.Graphics.FillRectangle(backBrush, e.Bounds);
            }

            using (Pen borderBrush = new(Color.Black))
            {
                e.Graphics.DrawLine(borderBrush, e.Bounds.Left, e.Bounds.Top, e.Bounds.Left, e.Bounds.Bottom);
                e.Graphics.DrawLine(borderBrush, e.Bounds.Right, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
            }

            if (e.Font != null && e.Header != null)
                using (SolidBrush foreBrush = new(foreColor))
                {
                    var format = new StringFormat
                    {
                        Alignment = (StringAlignment)e.Header.TextAlign,
                        LineAlignment = StringAlignment.Center
                    };

                    var paddedBounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);

                    e.Graphics.DrawString(e.Header?.Text, e.Font, foreBrush, paddedBounds, format);
                }
        }

        private static void BodyDraw(object? _, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void PluginList_Resize(object sender, EventArgs e)
        {
            FitColumns();
        }

        private void FitColumns()
        {
            int totalWidth = 0;
            foreach (ColumnHeader col in PluginList.Columns)
                totalWidth += col.Width;

            PluginList.Columns[3].Width += PluginList.Width - totalWidth; // - SystemInformation.VerticalScrollBarWidth;
        }

        private void ReadAllButton_Click(object sender, EventArgs e)
        {
            var readAllDialogue = new ReadAllForm();
            ThemeManager.GetInstance.RegisterControl(readAllDialogue);
            readAllDialogue.StartPosition = FormStartPosition.Manual;
            readAllDialogue.Location = Point.Add(Location, new Size(100, 100));
            SuspendDrawing(this);
            SuspendSorting();
            readAllDialogue.ShowDialog();
            ResumeSorting();
            ResumeDrawing(this);
        }

        private Dictionary<PluginListView, object> PluginComparer;

        private void SuspendSorting()
        {
            PluginComparer = [];
            foreach (TabPage tab in CoreTabControl.TabPages)
            {
                foreach (var control in tab.Controls)
                {
                    if (control?.GetType() == typeof(PluginListView))
                    {
                        var listView = (PluginListView)control;
                        PluginComparer.Add(listView, listView.ListViewItemSorter);
                        listView.ListViewItemSorter = null;
                    }
                }
            }
        }

        private void ResumeSorting()
        {
            if (PluginComparer.Count != 0)
                foreach (var panel in PluginComparer.Keys)
                {
                    panel.ListViewItemSorter = (IObservatoryComparer)PluginComparer[panel];
                }
            PluginComparer?.Clear();
        }

        private NativeNotification.NativePopup? nativePopup;
        private NativeNotification.NativeVoice? nativeVoice;



        private void GithubLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenURL("https://github.com/Xjph/ObservatoryCore");
        }

        private void DonateLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // OpenURL("https://www.paypal.com/donate/?hosted_button_id=XYQWYQ337TBP4");
            var donateForm = new DonateForm();
            ThemeManager.GetInstance.RegisterControl(donateForm);
            donateForm.ShowDialog();
        }

        private static void OpenURL(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            // Find currently selected item for export
            if (CoreTabControl.SelectedTab != null && pluginList.ContainsKey(CoreTabControl.SelectedTab))
            {
                var selectedItem = pluginList[CoreTabControl.SelectedTab];
                PluginExport(selectedItem);
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            if (CoreTabControl.SelectedTab != null && pluginList.ContainsKey(CoreTabControl.SelectedTab))
            {
                var selectedItem = pluginList[CoreTabControl.SelectedTab];
                PluginClear(selectedItem);
            }
        }

        private void CoreForm_Shown(object sender, EventArgs e)
        {
            PluginManager.GetInstance.ObservatoryReady();


            if (Properties.Core.Default.StartReadAll)
                ReadAllButton_Click(ReadAllButton, EventArgs.Empty);

            if (Properties.Core.Default.StartMonitor)
                ToggleMonitorButton_Click(ToggleMonitorButton, EventArgs.Empty);
        }

        private void PluginFolderButton_Click(object sender, EventArgs e)
        {
            var pluginDir = Application.StartupPath + "plugins";

            if (!Directory.Exists(pluginDir))
            {
                Directory.CreateDirectory(pluginDir);
            }

            var fileExplorerInfo = new ProcessStartInfo() { FileName = pluginDir, UseShellExecute = true };
            Process.Start(fileExplorerInfo);
        }

        private void CoreForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save location
            Properties.Core.Default.MainWindowPosition = Location;
            Properties.Core.Default.MainWindowSize = Size;
            Properties.Core.Default.CoreSplitterDistance = CoreSplitter.SplitterDistance;
            SettingsManager.Save();
        }

        private void CoreForm_Load(object sender, EventArgs e)
        {
            CoreSplitter.SplitterDistance = Math.Clamp(Properties.Core.Default.CoreSplitterDistance, 20, CoreSplitter.Height - 20);
            var savedLocation = Properties.Core.Default.MainWindowPosition;
            var savedSize = Properties.Core.Default.MainWindowSize;

            // Ensure we're on screen
            bool onscreen = false;
            var formBounds = new Rectangle(savedLocation, savedSize);
            foreach (var screen in Screen.AllScreens)
            {
                onscreen = onscreen || screen.WorkingArea.Contains(formBounds);
            }

            if (onscreen)
            {
                Location = savedLocation;
                Size = savedSize;
            }
        }

        private void CoreTabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(BackColor), CoreTabControl.ClientRectangle);

            for (int i = 0; i < CoreTabControl.TabPages.Count; i++)
            {
                var tab = CoreTabControl.TabPages[i];
                var selected = CoreTabControl.SelectedIndex == i;
                var tabArea = CoreTabControl.GetTabRect(i);
                var stringFormat = new StringFormat()
                {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Center
                };
                if (selected)
                {
                    try
                    {
                        e.Graphics.FillRectangle(new SolidBrush(CoreTabControl.SelectedTabColor), tabArea);
                    }
                    catch (ExternalException ex) // A generic error occurred in GDI+.
                    {
                        // This happens sometimes randomly when resizing things a bunch, but doesn't seem to break anything.
                    }
                    tabArea.Offset(-1, -1);
                }
                else
                {
                    try
                    {
                        e.Graphics.FillRectangle(new SolidBrush(CoreTabControl.TabColor), tabArea);
                    }
                    catch (ExternalException ex) // A generic error occurred in GDI+.
                    {
                        // This happens sometimes randomly when resizing things a bunch, but doesn't seem to break anything.
                    }
                    tabArea.Offset(1, 1);
                }

                if (CoreTabControl.Alignment == TabAlignment.Left)
                {
                    stringFormat.FormatFlags = StringFormatFlags.DirectionVertical;
                }

                e.Graphics.DrawString(tab.Text, e.Font ?? new Font("Segoe UI", 9), new SolidBrush(tab.ForeColor), tabArea, stringFormat);
            }
        }

        private void RestoreSavedTab()
        {
            CoreTabControl.SelectedIndex = Properties.Core.Default.LastTabIndex < CoreTabControl.TabPages.Count
                ? Properties.Core.Default.LastTabIndex
                : 0;
            CoreTabControl_SelectedIndexChanged(CoreTabControl, EventArgs.Empty);
        }

        private void CoreTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedTab = CoreTabControl.SelectedTab;
            if (selectedTab != null)
            {
                pluginList.TryGetValue(selectedTab, out var plugin);

                // Named bools for clarity
                bool notCoreTab = selectedTab != CoreTabPage;
                bool hasExportMethod = notCoreTab && HasCustomExport(plugin);
                bool isBasicUI = notCoreTab && plugin?.PluginUI.PluginUIType == Framework.PluginUI.UIType.Basic;

                bool canExport = isBasicUI || hasExportMethod;
                bool canClear = isBasicUI;
                ExportButton.Enabled = canExport;
                ClearButton.Enabled = canClear;
                Properties.Core.Default.LastTabIndex = CoreTabControl.SelectedIndex;
                SettingsManager.Save();
            }
        }

        private static bool HasCustomExport(IObservatoryPlugin? plugin) => ((Delegate)plugin.ExportContent).Method != typeof(IObservatoryPlugin).GetMethod("ExportContent");
        
    }
}