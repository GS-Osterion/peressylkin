using System.Globalization;

namespace Sibir
{
    /// <summary>Every human-facing text lives here (spec §2).</summary>
    internal static class Strings
    {
        public const string AppName = "Ссылка в Сибирь";
        public const string MenuExile = "Сослать в Сибирь…";
        public const string MenuRehab = "Реабилитировать";
        public const string MenuCase = "Личное дело";
        public const string WhereTo = "Куда этапировать?";
        public const string GoButton = "С вещами на выход!";
        public const string Summary = "Осуждённых: {0} · Общий вес: {1}";
        public const string Progress = "Столыпин в пути…";
        public const string SameDrive = "Это не ссылка, а прогулка по камере";
        public const string Protected = "Член Политбюро. Неприкосновенен";
        public const string Property = "Имущество тов. Перессылкина. Ссылке не подлежит";
        public const string AlreadyExiled = "Повторно не судят";
        public const string InSiberia = "Уже отбывает срок. Вернуть — через реабилитацию";
        public const string CaseOpen = "Дело уже заведено: по архиву гражданин сослан";
        public const string TargetIsSource = "Нельзя сослать гражданина в его собственную камеру";
        public const string CapitalCheckbox = "{0} — столица";
        public const string Capital = "{0} — столица. Ссыльным — от 101-го километра";
        public const string RegimeCity = "{0} — режимный город";
        public const string NoSpace = "В Сибири мест нет: нужно {0}, свободно {1}";
        public const string Removable = "Шаг влево, шаг вправо… Вынешь флешку — побег";
        public const string Locked = "Подследственный оказывает сопротивление: его держит {0}";
        public const string Launcher = "Дело передано в органы. Гражданин под надзором {0}; переселение — в компетенции КГБ (вашей)";
        public const string StoreApp = "Особый режим, ссылке не подлежит. Параметры → Приложения → Переместить";
        public const string RegisteredApp = "При переустановке гражданин может вернуться по месту прописки";
        public const string NeedsAdmin = "Требуется санкция прокурора";
        public const string Done = "Этап прибыл. Освобождено жилплощади: {0}. Дело сдано в архив — хранить вечно";
        public const string Amnesty = "Амнистия! Все возвращены по месту прописки";
        public const string Recover = "Доследование";
        public const string NoDestination = "Сибирь не найдена. Этап отменён";
        public const string NotExiled = "Не числится в архиве";
        public const string NetworkTarget = "Сибирь должна быть на этой земле: сетевой путь не годится";
        public const string Nested = "Уже едет в составе старшего по делу";
        public const string TargetExists = "Место занято: по этому адресу уже кто-то живёт";
        public const string CopyFailed = "Этап сорван";
        public const string VerifyFailed = "Досмотр не пройден: при пересчёте не сошлись";
        public const string RehabDone = "Реабилитирован. Возвращён по месту прописки";
        public const string HomeNoSpace = "Жилплощадь по месту прописки: нужно {0}, свободно {1}";
        public const string Ok = "Годен к этапу";

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        /// <summary>13207024435 → "12,3 ГБ"; below 1 GiB → whole МБ.</summary>
        public static string Size(long bytes)
        {
            const double GiB = 1024d * 1024 * 1024, MiB = 1024d * 1024;
            return bytes >= GiB
                ? (bytes / GiB).ToString("0.0", Ru) + " ГБ"
                : ((long)System.Math.Round(bytes / MiB)).ToString(Ru) + " МБ";
        }
    }
}
