using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sibir.Core;

namespace Sibir.Gui
{
    /// <summary>«Секретные записи» — only in an elevated process.</summary>
    internal sealed class SecretForm : Form
    {
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        private readonly Button _declassify = new Button { Text = "Рассекретить", AutoSize = true, Height = 32 };
        private readonly Button _rehab = new Button { Text = "Реабилитировать", AutoSize = true, Height = 32 };
        private readonly Button _burn = new Button { Text = "Сжечь дело", AutoSize = true, Height = 32 };
        private readonly Secrecy _secrecy;

        public SecretForm(Secrecy secrecy)
        {
            _secrecy = secrecy;
            Text = "Секретные записи — " + Strings.AppName;
            Size = new Size(860, 420);
            StartPosition = FormStartPosition.CenterParent;
            _list.Columns.Add("Место прописки", 300); _list.Columns.Add("Место ссылки", 300); _list.Columns.Add("Вес", 70); _list.Columns.Add("Статус", 120);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4) };
            buttons.Controls.Add(_declassify); buttons.Controls.Add(_rehab); buttons.Controls.Add(_burn);
            Controls.Add(_list); Controls.Add(buttons);

            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _declassify.Click += (s, e) => Each(r => _secrecy.Declassify(r));
            _burn.Click += (s, e) =>
            {
                var sel = Selected().Where(r => r.Status == "rehabilitated").ToList();
                if (sel.Count == 0 || MessageBox.Show(this, string.Format(Strings.BurnConfirm, string.Join(", ", sel.Select(r => r.Source))), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                Each(r => Secrecy.Burn(r, GuiApp.Ctx.Secret), sel);
            };
            _rehab.Click += (s, e) =>
            {
                var sel = Selected().Where(r => r.Status == "exiled").ToList();
                if (sel.Count == 0) return;
                var mover = GuiApp.NewMover();
                using (var f = new ProgressForm(sel.Select(r => new ProgressForm.Job(r.Source, (pr, ct) => mover.Rehab(r.Source, pr, ct)) { Verb = "--menu-rehab" }).ToList()))
                    f.ShowDialog(this);
                Reload();
            };
            GuiApp.Ctx.Secret.EnsureAcl();
            Reload();
        }

        private ArchiveRecord[] Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (ArchiveRecord)i.Tag).ToArray();

        private void Each(Func<ArchiveRecord, MoveResult> act, System.Collections.Generic.IEnumerable<ArchiveRecord> recs = null)
        {
            var errors = (recs ?? Selected()).Select(r =>
            {
                try { return act(r); }
                catch (Exception e) { return new MoveResult { Source = r.Source, Ok = false, Message = e.Message }; }
            }).Where(r => !r.Ok).Select(r => r.Source + ": " + r.Message).ToList();
            if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), Text);
            Reload();
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (var r in GuiApp.Ctx.Secret.Current().Reverse())
                _list.Items.Add(new ListViewItem(new[] { r.Source, r.Target, Strings.Size(r.Bytes), GuiApp.StatusText(r.Status) }) { Tag = r });
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var sel = Selected();
            _declassify.Enabled = sel.Any(r => r.Status == "exiled" || r.Status == "rehabilitated");
            _rehab.Enabled = sel.Any(r => r.Status == "exiled");
            _burn.Enabled = sel.Any(r => r.Status == "rehabilitated");
        }
    }
}
