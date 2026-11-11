using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sibir.Core;

namespace Sibir.Gui
{
    /// <summary>"Куда этапировать?" — destination, verdicts per folder, options, go.</summary>
    internal sealed class ExileForm : Form
    {
        private readonly string[] _paths;
        private readonly Settings _settings;
        private readonly TextBox _root = new TextBox { Dock = DockStyle.Fill };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
        private readonly CheckBox _strict = new CheckBox { Text = "Строгий досмотр (сверка по хэшам, медленнее)", AutoSize = true };
        private readonly CheckBox _byName = new CheckBox { Text = "Класть по имени (без полного пути)", AutoSize = true };
        private readonly CheckBox _stamp = new CheckBox { Text = "Ставить штамп", AutoSize = true, Checked = true };
        private readonly CheckBox _capital = new CheckBox { AutoSize = true };
        private readonly Label _summary = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        private readonly Button _go = new Button { Text = Strings.GoButton, AutoSize = true, Height = 34 };
        private readonly Timer _debounce = new Timer { Interval = 400 };
        private List<CheckResult> _checks = new List<CheckResult>();
        private bool _refreshing;

        public ExileForm(string[] paths)
        {
            _paths = paths;
            _settings = Settings.Load(GuiApp.Ctx.SettingsPath);
            Text = Strings.AppName + " — " + Strings.WhereTo;
            Size = new Size(820, 480);
            StartPosition = FormStartPosition.CenterScreen;
            if (System.IO.File.Exists(System.IO.Path.Combine(Shell.ShellIntegration.InstallDir, "sibir.ico")))
                Icon = new Icon(System.IO.Path.Combine(Shell.ShellIntegration.InstallDir, "sibir.ico"));

            _capital.Text = string.Format(Strings.CapitalCheckbox, GuiApp.Ctx.Drives.SystemDrive.Substring(0, 1));
            _capital.Checked = _settings.ProtectCapital;
            _root.Text = Destination.DefaultRoot(paths[0], _settings, GuiApp.Ctx.Drives) ?? "";

            var browse = new Button { Text = "Обзор…", AutoSize = true };
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = Strings.WhereTo, SelectedPath = _root.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) _root.Text = d.SelectedPath;
            };
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 3, Padding = new Padding(4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(new Label { Text = "Место ссылки:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            top.Controls.Add(_root, 1, 0); top.Controls.Add(browse, 2, 0);

            _list.Columns.Add("Осуждённый", 330); _list.Columns.Add("Вес", 80); _list.Columns.Add("Приговор", 380);
            var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
            opts.Controls.AddRange(new Control[] { _stamp, _capital, _strict, _byName });
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            bottom.Controls.Add(_go); bottom.Controls.Add(_summary);

            Controls.Add(_list); Controls.Add(top); Controls.Add(opts); Controls.Add(bottom);

            _debounce.Tick += (s, e) => { _debounce.Stop(); Recheck(); };
            _root.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
            _byName.CheckedChanged += (s, e) => Recheck();
            _capital.CheckedChanged += (s, e) => Recheck();
            _list.ItemCheck += OnItemCheck;
            _list.ItemChecked += (s, e) => UpdateSummary();
            _go.Click += (s, e) => Go();
            Shown += (s, e) => Recheck();
        }

        private void Recheck()
        {
            _refreshing = true;
            _settings.ProtectCapital = _capital.Checked;
            _list.Items.Clear();
            if (string.IsNullOrWhiteSpace(_root.Text))
            {
                _checks = _paths.Select(p => new CheckResult { Path = p, Verdict = Verdict.Refused, Reason = "no_destination", Message = Strings.NoDestination }).ToList();
            }
            else
            {
                try
                {
                    _checks = Checks.Run(_paths, _root.Text, new CheckOptions { ByName = _byName.Checked, Settings = _settings }, GuiApp.Ctx.Drives, GuiApp.Ctx.Archive,
                        p => LauncherDetect.Detect(p, GuiApp.Ctx.Launchers));
                }
                catch (Exception ex)
                {
                    _checks = _paths.Select(p => new CheckResult { Path = p, Verdict = Verdict.Refused, Reason = "bad_args", Message = ex.Message }).ToList();
                }
            }
            foreach (var c in _checks)
            {
                var it = new ListViewItem(new[] { c.Path, c.Bytes > 0 ? Strings.Size(c.Bytes) : "", c.Message }) { Tag = c, Checked = c.Verdict != Verdict.Refused };
                it.ForeColor = c.Verdict == Verdict.Refused ? (c.Reason == "launcher" ? Color.DarkOrange : Color.Firebrick) : c.Verdict == Verdict.Warn ? Color.DarkGoldenrod : SystemColors.WindowText;
                if (c.Reason == "launcher") it.ToolTipText = "Поставьте галочку, чтобы всё равно сослать";
                _list.Items.Add(it);
            }
            _refreshing = false;
            UpdateSummary();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_refreshing) return;
            var c = (CheckResult)_list.Items[e.Index].Tag;
            if (c.Verdict == Verdict.Refused && c.Reason != "launcher") e.NewValue = CheckState.Unchecked;   // only launcher refusals can be overridden
            if (c.Reason == "launcher" && e.NewValue == CheckState.Checked &&
                MessageBox.Show(this, c.Message + "\n\nВсё равно сослать?", Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.NewValue = CheckState.Unchecked;
        }

        private List<CheckResult> Selected() => _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (CheckResult)i.Tag).ToList();

        private void UpdateSummary()
        {
            var sel = Selected();
            _summary.Text = string.Format(Strings.Summary, sel.Count, Strings.Size(sel.Sum(c => c.Bytes)));
            _go.Enabled = sel.Count > 0;
        }

        private void Go()
        {
            var sel = Selected();
            // Launcher overrides: re-check those paths with IgnoreLauncher so size/space are computed too.
            var overridden = sel.Where(c => c.Reason == "launcher").Select(c => c.Path).ToList();
            if (overridden.Count > 0)
            {
                var again = Checks.Run(overridden, _root.Text, new CheckOptions { ByName = _byName.Checked, IgnoreLauncher = true, Settings = _settings },
                    GuiApp.Ctx.Drives, GuiApp.Ctx.Archive, p => LauncherDetect.Detect(p, GuiApp.Ctx.Launchers));
                sel = sel.Where(c => c.Reason != "launcher").Concat(again.Where(c => c.Verdict != Verdict.Refused)).ToList();
            }
            var saved = Settings.Load(GuiApp.Ctx.SettingsPath);
            saved.LastRoot = _root.Text; saved.ProtectCapital = _capital.Checked; saved.Save(GuiApp.Ctx.SettingsPath);
            var mover = GuiApp.NewMover(_strict.Checked, _stamp.Checked);
            Hide();
            new ProgressForm(sel.Select(c => new ProgressForm.Job(c.Path, (p, ct) => mover.Exile(c.Path, c.Target, p, ct))).ToList()).ShowDialog();
            Close();
        }
    }
}
