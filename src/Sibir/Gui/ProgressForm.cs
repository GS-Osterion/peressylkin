using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sibir.Core;
using Sibir.Shell;

namespace Sibir.Gui
{
    /// <summary>Runs jobs one by one ("Столыпин в пути…"), then shows the summary.</summary>
    internal sealed class ProgressForm : Form
    {
        internal sealed class Job
        {
            public string Path; public Func<IProgress<CopyProgress>, CancellationToken, MoveResult> Run;
            public Job(string path, Func<IProgress<CopyProgress>, CancellationToken, MoveResult> run) { Path = path; Run = run; }
        }

        private readonly List<Job> _jobs;
        private readonly Label _status = new Label { Dock = DockStyle.Top, Height = 48, Text = Strings.Progress, Padding = new Padding(4) };
        private readonly ProgressBar _bar = new ProgressBar { Dock = DockStyle.Top, Height = 24, Maximum = 1000 };
        private readonly TextBox _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
        private readonly Button _button = new Button { Text = "Отставить", Dock = DockStyle.Bottom, Height = 32 };
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _done;

        public ProgressForm(List<Job> jobs)
        {
            _jobs = jobs;
            Text = Strings.AppName + " — этап";
            Size = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(_log); Controls.Add(_bar); Controls.Add(_status); Controls.Add(_button);
            _button.Click += (s, e) => { if (_done) Close(); else { _cts.Cancel(); _button.Enabled = false; _status.Text = "Отставить! Возвращаем текущего по месту прописки…"; } };
            FormClosing += (s, e) => { if (!_done) { e.Cancel = true; _cts.Cancel(); } };
            Shown += async (s, e) => await RunAll();
        }

        private async Task RunAll()
        {
            long freed = 0; int ok = 0;
            var results = new List<MoveResult>();
            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                int n = i;
                var progress = new Progress<CopyProgress>(p =>
                {
                    _status.Text = $"{Strings.Progress} [{n + 1}/{_jobs.Count}] {job.Path}\n{Strings.Size(p.BytesDone)} из {Strings.Size(p.BytesTotal)}";
                    _bar.Value = p.BytesTotal == 0 ? 1000 : (int)(1000 * p.BytesDone / p.BytesTotal);
                });
                _status.Text = $"{Strings.Progress} [{n + 1}/{_jobs.Count}] {job.Path}";
                var r = _cts.IsCancellationRequested
                    ? new MoveResult { Source = job.Path, Reason = "cancelled", Message = "Этап отменён" }
                    : await Task.Run(() => job.Run(progress, _cts.Token));
                if (r.Reason == "needs_admin" && AskElevation(job.Path)) { r.Message = "Передано на исполнение с санкцией прокурора"; }
                results.Add(r);
                if (r.Ok) { ok++; freed += r.Bytes; }
                _log.AppendText((r.Ok ? "[+] " : "[x] ") + job.Path + Environment.NewLine + "    " + r.Message +
                    (r.Processes.Length > 0 ? " (" + string.Join(", ", r.Processes) + ")" : "") + Environment.NewLine);
            }
            _done = true;
            _bar.Value = 1000;
            _status.Text = ok > 0 ? $"Прибыло: {ok} из {_jobs.Count}. " + string.Format(Strings.Done, Strings.Size(freed)) : "Этап не состоялся.";
            _button.Text = "Разойтись"; _button.Enabled = true;
        }

        private static bool AskElevation(string path)
        {
            if (MessageBox.Show(Strings.NeedsAdmin + ".\n\nЗапросить права администратора для:\n" + path, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            return Elevation.Relaunch(new[] { "--menu-exile", path });
        }
    }
}
