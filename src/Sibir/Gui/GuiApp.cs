using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Sibir.Cli;
using Sibir.Core;
using Sibir.Native;
using Sibir.Shell;

namespace Sibir.Gui
{
    internal static class GuiApp
    {
        public static CliContext Ctx;

        public static int Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Ctx = CliContext.Real();

            string verb = args.Length >= 2 ? args[0] : null, path = args.Length >= 2 ? args[1] : null;
            string[] paths = null;
            if (verb == "--menu-exile" || verb == "--menu-rehab")
            {
                paths = Collector.Gather(verb.Substring(7), path, TimeSpan.FromMilliseconds(400));
                if (paths == null) return 0;   // handed to the collecting process
            }

            if (Ctx.Archive.Unfinished().Count > 0) OfferRecovery();

            switch (verb)
            {
                case "--menu-exile": Application.Run(new ExileForm(paths)); break;
                case "--menu-rehab": Rehab(paths); break;
                case "--menu-case": ShowCase(path); break;
                default: Application.Run(new MainForm()); break;
            }
            return 0;
        }

        public static Mover NewMover(bool strict = false, bool stamp = true) =>
            new Mover(Ctx.Archive, new MoverOptions { Strict = strict, Stamp = stamp, StampIconPath = Ctx.StampIconPath });

        private static void OfferRecovery()
        {
            var n = Ctx.Archive.Unfinished().Count;
            if (MessageBox.Show($"Найдено незакрытых дел: {n}. Похоже, прошлый этап прервался.\n\nПровести доследование сейчас? Всё будет либо завершено, либо возвращено как было.",
                    Strings.Recover, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var res = NewMover().Recover();
            MessageBox.Show(string.Join("\n", res.Select(r => $"{r.Source}: {r.Message}")), Strings.Recover);
        }

        private static void Rehab(string[] paths)
        {
            var known = paths.Where(p => Ctx.Archive.FindExiled(p) != null).ToList();
            var unknown = paths.Except(known).ToList();
            if (unknown.Count > 0) MessageBox.Show(string.Join("\n", unknown) + "\n\n" + Strings.NotExiled, Strings.MenuRehab, MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (known.Count == 0) return;
            if (MessageBox.Show("Вернуть по месту прописки?\n\n" + string.Join("\n", known), Strings.MenuRehab, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var mover = NewMover();
            Application.Run(new ProgressForm(known.Select(p => new ProgressForm.Job(p, (pr, ct) => mover.Rehab(p, pr, ct)) { Verb = "--menu-rehab" }).ToList()));
        }

        private static void ShowCase(string path)
        {
            var rec = Ctx.Archive.Current().LastOrDefault(r => LongPath.Same(r.Source, path) || LongPath.Same(r.Target, path));
            if (rec == null) { MessageBox.Show(path + "\n\n" + Strings.NotExiled, Strings.MenuCase); return; }
            var sb = new StringBuilder();
            sb.AppendLine("ЛИЧНОЕ ДЕЛО № " + rec.Id.Substring(0, 8).ToUpperInvariant());
            sb.AppendLine();
            sb.AppendLine("Место прописки: " + rec.Source);
            sb.AppendLine("Место ссылки:   " + rec.Target);
            sb.AppendLine("Вес:            " + Strings.Size(rec.Bytes));
            sb.AppendLine("Статус:         " + StatusText(rec.Status));
            sb.AppendLine("Дата:           " + rec.TimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine();
            sb.AppendLine("ХРАНИТЬ ВЕЧНО");
            MessageBox.Show(sb.ToString(), Strings.MenuCase);
        }

        public static string StatusText(string s)
        {
            switch (s)
            {
                case "exiled": return "спецпоселенец";
                case "rehabilitated": return "реабилитирован";
                case "amnestied": return "амнистирован";
                case "in_transit": return "в пути (незакрытое дело)";
                case "rehabilitating": return "возвращается (незакрытое дело)";
                default: return s;
            }
        }

        public static string SelfPath => Application.ExecutablePath;
    }
}
