using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Ruri.Hook.Config;
using Ruri.Hook;

namespace Ruri.Hook.UI;

public class HookSelectionForm : Form
{
    private CheckedListBox _clbHooks;
    private Button _btnSaveAndLaunch;
    private Button _btnCancel;
    private readonly HookConfig _config;
    private readonly string _configPath;

    public HookSelectionForm(HookConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;

        Text = "Ruri Hook Configuration";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;

        InitializeComponent();
        LoadHooks();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 10));

        _clbHooks = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = new Font("Consolas", 10)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        _btnSaveAndLaunch = new Button
        {
            Text = "Save & Launch",
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5)
        };
        _btnSaveAndLaunch.Click += (s, e) => SaveAndClose();

        _btnCancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5)
        };
        _btnCancel.Click += (s, e) => Application.Exit();

        buttonPanel.Controls.Add(_btnSaveAndLaunch);
        buttonPanel.Controls.Add(_btnCancel);

        layout.Controls.Add(_clbHooks, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(layout);
    }

    private void LoadHooks()
    {
        var hooks = RuriHook.GetAvailableHooks();
        var enabledHooks = _config.EnabledHooks;

        foreach (var (type, attr) in hooks)
        {
            var displayName = attr.GameName;
            if (!string.IsNullOrEmpty(attr.Version))
            {
                displayName += $" {attr.Version}";
            }
            if (!string.IsNullOrEmpty(attr.BaseUnityVersion)) 
            {
               displayName += $" [{attr.BaseUnityVersion}]"; 
            }
            
            // Use GameName_Version as ID
            var id = $"{attr.GameName}_{attr.Version}";
            
            _clbHooks.Items.Add(new HookItem(displayName, id), enabledHooks.Contains(id));
        }
    }

    private void SaveAndClose()
    {
        _config.EnabledHooks.Clear();
        
        foreach (HookItem item in _clbHooks.CheckedItems)
        {
            _config.EnabledHooks.Add(item.Id);
        }

        _config.Save(_configPath);
        Close();
    }

    private class HookItem
    {
        public string DisplayName { get; }
        public string Id { get; }

        public HookItem(string displayName, string id)
        {
            DisplayName = displayName;
            Id = id;
        }

        public override string ToString() => DisplayName;
    }
}
