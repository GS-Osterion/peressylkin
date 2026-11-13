using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sibir.Core;
using Sibir.Shell;

namespace Sibir.Gui
{
    /// <summary>Registration ("встать на учёт"), archive "Хранить вечно", regime cities, test range.</summary>
    internal sealed class MainForm : Form
    {
        private readonly Button _register = new Button { AutoSize = true, Height = 32 };
        private readonly CheckBox _startMenu = new CheckBox { Text = "Ярлык в меню Пуск", Checked = true, AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
        private readonly CheckBox _desktop = new CheckBox { Text = "Ярлык на рабочем столе", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
        private readonly ListView _archive = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        private readonly CheckedListBox _regime = new CheckedListBox { Dock = DockStyle.Right, Width = 170, CheckOnClick = true };
        private readonly Settings _settings;

        public MainForm()
        {
            _settings = Settings.Load(GuiApp.Ctx.SettingsPath);
            Text = Strings.AppName;
            Size = new Size(900, 480);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var range = new Button { Text = "Полигон (самопроверка)", AutoSize = true, Height = 32 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4) };
            buttons.Controls.Add(_register); buttons.Controls.Add(_startMenu); buttons.Controls.Add(_desktop); buttons.Controls.Add(range);
            buttons.Controls.Add(new Label { Text = "Правой кнопкой по папке → «Показать дополнительные параметры» → " + Strings.MenuExile, AutoSize = true, Padding = new Padding(8, 9, 0, 0) });

            _archive.Columns.Add("Место прописки", 300); _archive.Columns.Add("Место ссылки", 300); _archive.Columns.Add("Вес", 70); _archive.Columns.Add("Статус", 120);
            var archiveBox = new GroupBox { Text = "Архив. Хранить вечно", Dock = DockStyle.Fill };
            archiveBox.Controls.Add(_archive);
            var regimeBox = new GroupBox { Text = "Режимные города", Dock = DockStyle.Right, Width = 180 };
            regimeBox.Controls.Add(_regime);

            Controls.Add(archiveBox); Controls.Add(regimeBox); Controls.Add(buttons);

            foreach (var d in GuiApp.Ctx.Drives.Drives().Where(d => !string.Equals(d.Root, GuiApp.Ctx.Drives.SystemDrive, StringComparison.OrdinalIgnoreCase)))
                _regime.Items.Add(d.Letter, _settings.RegimeDrives.Any(r => string.Equals(r, d.Letter, StringComparison.OrdinalIgnoreCase)));
            _regime.ItemCheck += (s, e) => BeginInvoke(new Action(SaveRegime));

            _register.Click += (s, e) => ToggleRegistration();
            range.Click += async (s, e) => await RunRange(range);
            RefreshState();
        }

        private void RefreshState()
        {
            using (var k = ShellIntegration.UserClasses())
            {
                var installed = ShellIntegration.IsInstalledAt(k, ShellIntegration.InstalledExe);
                _register.Text = installed ? "Сняться с учёта" : "Встать на учёт";
                _startMenu.Enabled = _desktop.Enabled = !installed;
            }
            _archive.Items.Clear();
            foreach (var r in GuiApp.Ctx.Archive.Current().Reverse())
                _archive.Items.Add(new ListViewItem(new[] { r.Source, r.Target, Strings.Size(r.Bytes), GuiApp.StatusText(r.Status) }));
        }

        private void SaveRegime()
        {
            _settings.RegimeDrives = _regime.CheckedItems.Cast<string>().ToList();
            _settings.Save(GuiApp.Ctx.SettingsPath);
        }

        private void ToggleRegistration()
        {
            using (var k = ShellIntegration.UserClasses())
            {
                if (ShellIntegration.IsInstalledAt(k, ShellIntegration.InstalledExe))
                {
                    ShellIntegration.Uninstall(k);
                    ShellIntegration.RemoveAllShortcuts();
                    MessageBox.Show(this, "Снят с учёта. Пункты меню и ярлыки убраны, архив сохранён.", Strings.AppName);
                }
                else
                {
                    var exe = ShellIntegration.InstallFiles(Application.ExecutablePath);
                    ShellIntegration.Install(k, exe);
                    Sibir.Core.Plaque.EnsureArchive(ShellIntegration.InstallDir);
                    ShellIntegration.CreateShortcuts(exe, _startMenu.Checked, _desktop.Checked);
                    MessageBox.Show(this, "Встал на учёт: " + exe + "\n\nПравой кнопкой по папке → «Показать дополнительные параметры» → " + Strings.MenuExile, Strings.AppName);
                }
            }
            RefreshState();
        }

        private async Task RunRange(Button b)
        {
            var root = Destination.DefaultRoot(Path.GetTempPath(), _settings, GuiApp.Ctx.Drives);
            if (root == null) { MessageBox.Show(this, Strings.NoDestination, Strings.AppName); return; }
            b.Enabled = false; b.Text = "Полигон идёт…";
            var log = new StringWriter();
            int code = await Task.Run(() => SelfTest.Run(Path.Combine(root, "_полигон"), false, log));
            b.Enabled = true; b.Text = "Полигон (самопроверка)";
            MessageBox.Show(this, log.ToString(), "Полигон", MessageBoxButtons.OK, code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
