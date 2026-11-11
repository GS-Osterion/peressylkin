using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Sibir.Core;
using Sibir.Native;

namespace Sibir
{
    /// <summary>"Полигон": end-to-end scenarios on real disks (spec §7). Everything is created and removed by itself.</summary>
    internal static class SelfTest
    {
        private sealed class CrashHook : IFaultHook
        {
            public void Before(Step step, string source) { if (step == Step.SwapLink) throw new InvalidOperationException("power cut"); }
        }

        private sealed class NoFreeSpace : IDriveEnv
        {
            private readonly IDriveEnv _e; public NoFreeSpace(IDriveEnv e) { _e = e; }
            public IReadOnlyList<DriveFacts> Drives() => _e.Drives().Select(d => new DriveFacts { Root = d.Root, VolumeId = d.VolumeId, IsFixed = d.IsFixed, IsRemovable = d.IsRemovable, IsNetwork = d.IsNetwork, FreeBytes = 0 }).ToList();
            public string VolumeIdOf(string p) => _e.VolumeIdOf(p);
            public bool IsNetworkPath(string p) => _e.IsNetworkPath(p);
            public long SizeOf(string f) => _e.SizeOf(f);
            public string SystemDrive => _e.SystemDrive;
        }

        public static int Run(string targetRoot, bool json, TextWriter log)
        {
            var env = new RealDriveEnv();
            var work = Path.Combine(Path.GetTempPath(), "sibir-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            targetRoot = targetRoot ?? Destination.DefaultRoot(work, Settings.Load(Settings.DefaultPath), env);
            if (targetRoot == null) { log.WriteLine(Strings.NoDestination); return 4; }
            if (!targetRoot.TrimEnd('\\').EndsWith("_полигон", StringComparison.OrdinalIgnoreCase)) targetRoot = Path.Combine(targetRoot, "_полигон");

            var results = new List<(string Name, string Error)>();
            void Case(string name, Action body)
            {
                string err = null;
                try { body(); } catch (Exception e) { err = e.Message; }
                results.Add((name, err));
                if (json) log.WriteLine(Cli.Cli.Ascii(new JavaScriptSerializer().Serialize(new { scenario = name, ok = err == null, error = err })));
                else log.WriteLine((err == null ? "[+] " : "[x] ") + name + (err == null ? "" : ": " + err));
            }
            void Expect(bool cond, string what) { if (!cond) throw new Exception(what); }

            var archive = new Archive(Path.Combine(work, "archive.jsonl"));
            var opt = new MoverOptions { StampIconPath = @"C:\Windows\System32\shell32.dll" };
            try
            {
                Case("Ссылка и реабилитация смешанного дерева (300 МБ, мелочь, кириллица, длинные пути, вложенный junction)", () =>
                {
                    var src = Path.Combine(work, "Корабль");
                    MakeTree(src, work);
                    var before = Manifest.Build(src);
                    var hashes = Hashes(src, before);
                    var tgt = Destination.MapTarget(src, targetRoot, false);
                    var r = new Mover(archive, opt).Exile(src, tgt, null, CancellationToken.None);
                    Expect(r.Ok, "ссылка не состоялась: " + r.Reason + " " + r.Message);
                    Expect(Junction.IsReparsePoint(src), "на старом месте нет junction");
                    File.WriteAllText(Path.Combine(src, "через_ссылку.txt"), "ok");
                    Expect(File.Exists(Path.Combine(tgt, "через_ссылку.txt")), "запись через junction не дошла до Сибири");
                    File.Delete(Path.Combine(src, "через_ссылку.txt"));
                    var b = new Mover(archive, opt).Rehab(src, null, CancellationToken.None);
                    Expect(b.Ok, "реабилитация не состоялась: " + b.Reason + " " + b.Message);
                    Expect(Manifest.Build(src).FirstDifference(before, true) == null, "после реабилитации дерево отличается");
                    var after = Hashes(src, before);
                    Expect(hashes.All(kv => after[kv.Key] == kv.Value), "после реабилитации не совпали байты");
                    Expect(!Fs.DirExists(tgt), "в Сибири остался мусор");
                });

                Case("Сопротивление: занятый файл — отказ без изменений", () =>
                {
                    var src = Path.Combine(work, "Занятый"); Directory.CreateDirectory(src);
                    var f = Path.Combine(src, "save.dat"); File.WriteAllText(f, "x");
                    MoveResult r;
                    using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        r = new Mover(archive, opt).Exile(src, Destination.MapTarget(src, targetRoot, false), null, CancellationToken.None);
                    Expect(r.Reason == "locked", "ожидался locked, получено " + r.Reason);
                    Expect(!Junction.IsReparsePoint(src) && File.Exists(f), "источник изменён");
                });

                Case("Свет выключили на подмене — доследование восстанавливает", () =>
                {
                    var src = Path.Combine(work, "Обрыв"); Directory.CreateDirectory(src);
                    File.WriteAllText(Path.Combine(src, "save.dat"), "до обрыва");
                    var crash = new Mover(archive, opt, new CrashHook()) { SimulateCrash = true };
                    try { crash.Exile(src, Destination.MapTarget(src, targetRoot, false), null, CancellationToken.None); } catch (InvalidOperationException) { }
                    new Mover(archive, opt).Recover();
                    Expect(!Junction.IsReparsePoint(src) && File.ReadAllText(Path.Combine(src, "save.dat")) == "до обрыва", "доследование не восстановило источник");
                    Expect(archive.Unfinished().Count == 0, "остались незакрытые дела");
                });

                Case("Запреты: прогулка по камере, Политбюро, повторно не судят, мест нет", () =>
                {
                    var src = Path.Combine(work, "Запреты"); Directory.CreateDirectory(src); File.WriteAllText(Path.Combine(src, "a"), "a");
                    var s = new Settings { ProtectCapital = false };
                    string R(IDriveEnv e, string p, string root) => Checks.Run(new[] { p }, root, new CheckOptions { Settings = s }, e, archive, _ => null)[0].Reason;
                    Expect(R(env, src, Path.Combine(work, "Сибирь")) == "in_siberia" || R(env, src, Path.Combine(Path.GetPathRoot(work), "СибирьТест")) == "same_drive", "same_drive не сработал");
                    Expect(R(env, Environment.GetFolderPath(Environment.SpecialFolder.Windows), targetRoot) == "protected", "protected не сработал");
                    var link = Path.Combine(work, "Ссылка"); Junction.Create(link, src);
                    Expect(R(env, link, targetRoot) == "already_exiled", "already_exiled не сработал");
                    Junction.Delete(link);
                    Expect(R(new NoFreeSpace(env), src, targetRoot) == "no_space", "no_space не сработал");
                });
            }
            finally
            {
                try { Fs.DeleteTree(work); } catch { }
                try { Fs.DeleteTree(targetRoot); } catch { }
            }

            int failed = results.Count(r => r.Error != null);
            var summary = failed == 0 ? $"Полигон: {results.Count} из {results.Count}, нарушений нет" : $"Полигон: {results.Count - failed} из {results.Count}, нарушений: {failed}";
            log.WriteLine(json ? Cli.Cli.Ascii(new JavaScriptSerializer().Serialize(new { summary, failed })) : summary);
            return failed == 0 ? 0 : 4;
        }

        private static void MakeTree(string root, string work)
        {
            var rnd = new Random(7);
            for (int i = 0; i < 600; i++)
            {
                var d = Path.Combine(root, "d" + i % 20); Directory.CreateDirectory(d);
                var data = new byte[512 + rnd.Next(8192)]; rnd.NextBytes(data);
                File.WriteAllBytes(Path.Combine(d, $"f{i}.dat"), data);
            }
            Directory.CreateDirectory(Path.Combine(root, @"пусто\глубже"));
            var ro = Path.Combine(root, "только_чтение.txt"); File.WriteAllText(ro, "ro"); File.SetAttributes(ro, FileAttributes.ReadOnly | FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(root, "Сохранение 1.sav"), "корабль");
            var deep = root; while (deep.Length < 300) deep = Path.Combine(deep, "очень_длинное_имя_папки_полигона");
            Directory.CreateDirectory(LongPath.Ex(deep)); File.WriteAllText(LongPath.Ex(Path.Combine(deep, "deep.txt")), "deep");
            var outside = Path.Combine(work, "снаружи"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "x.txt"), "x");
            Junction.Create(Path.Combine(root, "ссылка_наружу"), outside);
            var buf = new byte[4 << 20]; rnd.NextBytes(buf);
            using (var fs = File.Create(Path.Combine(root, "big.bin")))
                for (int i = 0; i < 75; i++) fs.Write(buf, 0, buf.Length);   // 300 MB
        }

        private static Dictionary<string, string> Hashes(string root, Manifest m)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return m.Entries.Where(e => !e.IsDir && !e.IsLink).ToDictionary(e => e.RelPath, e =>
                {
                    using (var fs = File.OpenRead(LongPath.Ex(Path.Combine(root, e.RelPath)))) return Convert.ToBase64String(sha.ComputeHash(fs));
                }, StringComparer.OrdinalIgnoreCase);
        }
    }
}
