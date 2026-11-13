using System;
using System.IO;
using System.Text;

namespace Sibir.Core
{
    /// <summary>Text "plaques" marking the case archive and each Siberia root as Comrade Peressylkin's property.</summary>
    internal static class Plaque
    {
        public const string ArchiveFile = "АРХИВ ТОВ. ПЕРЕССЫЛКИНА — НЕ ТРОГАТЬ.txt";
        public const string SiberiaFile = "СИБИРЬ ТОВ. ПЕРЕССЫЛКИНА — НЕ ТРОГАТЬ.txt";

        private const string ArchiveText =
            "Здесь архив дел тов. Перессылкина (программа «Ссылка в Сибирь», github.com/GS-Osterion/peressylkin). " +
            "По нему реабилитируют сосланные папки: без него их не вернуть. Имя папки «Sibir» — историческое. " +
            "Не удалять, не переносить, не ссылать.";

        private const string SiberiaText =
            "Здесь отбывают ссылку папки, сосланные тов. Перессылкиным (программа «Ссылка в Сибирь», github.com/GS-Osterion/peressylkin). " +
            "На них ведут ссылки со старых мест. Вручную не удалять и не переименовывать — вернуть через «Реабилитировать».";

        /// <summary>%LOCALAPPDATA%\Sibir — the historical name is kept on purpose.</summary>
        public static string DefaultArchiveDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir");

        /// <summary>True if a plaque lies directly in <paramref name="dir"/> (no subtree walk).</summary>
        public static bool IsMarked(string dir) =>
            File.Exists(Path.Combine(dir, ArchiveFile)) || File.Exists(Path.Combine(dir, SiberiaFile));

        public static void EnsureArchive(string dir) => Ensure(dir, ArchiveFile, ArchiveText);

        public static void EnsureSiberia(string root) => Ensure(root, SiberiaFile, SiberiaText);

        /// <summary>Writes the plaque only if absent; never fails the caller.</summary>
        private static void Ensure(string dir, string file, string text)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) File.WriteAllText(path, text + Environment.NewLine, new UTF8Encoding(true));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
