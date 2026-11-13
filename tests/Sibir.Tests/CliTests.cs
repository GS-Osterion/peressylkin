using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Sibir.Cli;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    public class CliTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly CliContext _ctx;
        private readonly string _root;

        public CliTests()
        {
            var env = new FakeEnv().Drive(@"C:\", "volC", 100).Drive(@"T:\", "volT", 100).Drive(@"D:\", "volD", 100);
            env.Map(_t.Sub("c"), "volC").Map(_t.Sub("t"), "volT").Map(_t.Sub("d"), "volD");
            _ctx = new CliContext { Archive = new Archive(_t.Sub(@"arch\archive.jsonl")), Drives = env, Launchers = new FakeLaunchers(), SettingsPath = _t.Sub("settings.json") };
            _root = _t.Sub(@"t\Сибирь");
        }

        public void Dispose() => _t.Dispose();

        private string Src(string rel) { var p = _t.Sub(rel); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "s.sav"), "сейв"); return p; }

        private (int code, List<Dictionary<string, object>> rows, string err) Run(params string[] args)
        {
            var o = new StringWriter(); var e = new StringWriter();
            int code = Sibir.Cli.Cli.Run(args, o, e, _ctx);
            var js = new JavaScriptSerializer();
            var rows = o.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => js.Deserialize<Dictionary<string, object>>(l)).ToList();
            return (code, rows, e.ToString());
        }

        [Fact] public void Check_Json_OneLinePerPath()
        {
            var (code, rows, _) = Run("check", Src(@"c\A"), Src(@"c\B"), "--to", _root, "--json");
            Assert.Equal(0, code);
            Assert.Equal(2, rows.Count);
            Assert.Equal("ok", rows[0]["verdict"]);
            Assert.Equal(Destination.MapTarget(_t.Sub(@"c\A"), _root, false), rows[0]["target"]);
        }

        [Fact] public void Json_IsAsciiOnly()
        {
            var o = new StringWriter();
            Sibir.Cli.Cli.Run(new[] { "check", Src(@"c\Игра"), "--to", _root, "--json" }, o, new StringWriter(), _ctx);
            Assert.True(o.ToString().All(ch => ch < 128), "non-ASCII in JSON output");
        }

        [Fact] public void Exile_PlacesBothPlaques()
        {
            var (code, _, _) = Run("exile", Src(@"c\A"), "--to", _root, "--yes", "--json");
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(_root, Plaque.SiberiaFile)), "Siberia plaque");
            Assert.True(File.Exists(Path.Combine(_t.Sub("arch"), Plaque.ArchiveFile)), "archive plaque next to archive.jsonl");
        }

        [Fact] public void CliInstall_ParsesShortcutFlags()
        {
            Assert.Equal(0, Run("check", Src(@"c\A"), "--to", _root, "--desktop", "--no-start-menu", "--json").code);
            Assert.Equal(1, Run("check", Src(@"c\A"), "--to", _root, "--bogus", "--json").code);
        }

        [Fact] public void Check_PlacesNothing()
        {
            Run("check", Src(@"c\A"), "--to", _root, "--json");
            Assert.False(File.Exists(Path.Combine(_root, Plaque.SiberiaFile)), "no Siberia plaque on check");
            Assert.False(File.Exists(Path.Combine(_t.Sub("arch"), Plaque.ArchiveFile)), "no archive plaque on check");
        }

        [Fact] public void Check_RefusesArchiveDirOfContext()
        {
            var (code, rows, _) = Run("check", Src(@"c\arch"), "--to", _root, "--json");
            Assert.Equal(0, code);
            _ctx.Archive = new Archive(_t.Sub(@"c\arch\archive.jsonl"));
            var (code2, rows2, _) = Run("check", _t.Sub(@"c\arch"), "--to", _root, "--json");
            Assert.Equal(2, code2);
            Assert.Equal("peressylkin_property", rows2[0]["reason"]);
        }

        [Fact] public void Exile_WithoutYes_ChangesNothing()
        {
            var s = Src(@"c\A");
            var (code, rows, _) = Run("exile", s, "--to", _root, "--json");
            Assert.Equal(0, code);
            Assert.Equal("ok", rows[0]["verdict"]);
            Assert.False(Junction.IsReparsePoint(s));
        }

        [Fact] public void Exile_Yes_ExitZero_AndJunction()
        {
            var s = Src(@"c\A");
            var (code, rows, _) = Run("exile", s, "--to", _root, "--yes", "--json");
            Assert.Equal(0, code);
            Assert.Equal("done", rows[0]["verdict"]);
            Assert.True(Junction.IsReparsePoint(s));
            Assert.Equal(_root, Settings.Load(_ctx.SettingsPath).LastRoot);
            var (sc, st, _) = Run("status", s, "--json");
            Assert.Equal(0, sc);
            Assert.Equal("exiled", st[0]["verdict"]);
            var (rc, rr, _) = Run("rehab", s, "--yes", "--json");
            Assert.Equal(0, rc);
            Assert.Equal("done", rr[0]["verdict"]);
            Assert.False(Junction.IsReparsePoint(s));
        }

        [Fact] public void Exile_SameDrive_Exit2_ReasonSameDrive()
        {
            var (code, rows, _) = Run("exile", Src(@"t\A"), "--to", _root, "--yes", "--json");
            Assert.Equal(2, code);
            Assert.Equal("same_drive", rows[0]["reason"]);
        }

        [Fact] public void Exile_ToCapital_Exit2_UnlessAllowCapital()
        {
            var s = Src(@"d\A");
            Assert.Equal(2, Run("exile", s, "--to", _t.Sub(@"c\Сибирь"), "--json").code);
            Assert.Equal(0, Run("exile", s, "--to", _t.Sub(@"c\Сибирь"), "--allow-capital", "--json").code);
        }

        [Fact] public void Exile_SiberiaCopyBack_Exit2_InSiberia()
        {
            var s = Src(@"c\A");
            Assert.Equal(0, Run("exile", s, "--to", _root, "--yes", "--json").code);
            var (code, rows, _) = Run("exile", Destination.MapTarget(s, _root, false), "--to", _t.Sub(@"d\Сибирь"), "--yes", "--json");
            Assert.Equal(2, code);
            Assert.Equal("in_siberia", rows[0]["reason"]);
        }

        [Fact] public void Exile_Locked_Exit3()
        {
            var s = Src(@"c\A");
            using (new FileStream(Path.Combine(s, "s.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var (code, rows, _) = Run("exile", s, "--to", _root, "--yes", "--json");
                Assert.Equal(3, code);
                Assert.Equal("locked", rows[0]["reason"]);
            }
        }

        [Fact] public void Unfinished_BlocksExile_Exit6()
        {
            _ctx.Archive.Write(new ArchiveRecord { Id = "x", Source = _t.Sub(@"c\Z"), Target = _t.Sub(@"t\Z"), Status = "in_transit" });
            Assert.Equal(6, Run("exile", Src(@"c\A"), "--to", _root, "--yes", "--json").code);
            Assert.Equal(0, Run("recover", "--yes", "--json").code);
            Assert.Equal(0, Run("exile", Src(@"c\A"), "--to", _root, "--json").code);
        }

        [Fact] public void Status_NotExiled_ReasonNotExiled()
        {
            var (code, rows, _) = Run("status", Src(@"c\A"), "--json");
            Assert.Equal(2, code);
            Assert.Equal("not_exiled", rows[0]["reason"]);
        }

        [Fact] public void List_ShowsArchive()
        {
            Run("exile", Src(@"c\A"), "--to", _root, "--yes", "--json");
            var (code, rows, _) = Run("list", "--json");
            Assert.Equal(0, code);
            Assert.Equal(1, rows.Count);
            Assert.Equal("exiled", rows[0]["status"]);
        }

        [Fact] public void Schema_ContainsAllReasonsAndExitCodes()
        {
            var o = new StringWriter();
            Assert.Equal(0, Sibir.Cli.Cli.Run(new[] { "schema" }, o, new StringWriter(), _ctx));
            var s = o.ToString();
            foreach (var r in "already_exiled in_siberia case_open target_is_source capital regime_city protected same_drive network_target no_space removable_target launcher store_app registered_app nested target_exists locked needs_admin copy_failed verify_failed not_exiled no_destination bad_args not_found cancelled".Split(' '))
                Assert.Contains("\"" + r + "\"", s);
            foreach (var c in new[] { "\"0\"", "\"1\"", "\"2\"", "\"3\"", "\"4\"", "\"5\"", "\"6\"" }) Assert.Contains(c, s);
        }

        [Fact] public void BadArgs_Exit1()
        {
            Assert.Equal(1, Run("exile", "--json").code);
            Assert.Equal(1, Run("exile", Src(@"c\A"), "--json", "--to").code);
            Assert.Equal(1, Run("check", Src(@"c\A"), "--json", "--bogus").code);
        }
    }
}
