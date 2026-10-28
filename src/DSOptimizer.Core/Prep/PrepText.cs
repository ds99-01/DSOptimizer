using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Prep;

/// <summary>Teksty strony „Przygotowanie” (zaakceptowane na makiecie Taska 1), w bieżącym języku.</summary>
public static class PrepText
{
    public static string PageTitle => L.T("Przygotowanie");

    public static string PageSubtitle => L.T("Co sprawdzić po instalacji Windows. Program niczego tu nie instaluje ani nie zmienia.");

    public static string RefreshButton => L.T("Odśwież stan");

    // ---------- stany odczytu ----------

    public static string Loading => SpecificationFormatter.LoadingText;

    public static string Failed => L.T("Nie udało się odczytać");

    public static string Unknown => L.T("Nie wiadomo");

    // ---------- tytuły kroków ----------

    public static string ComputerTitle => L.T("Komputer");

    public const string WindowsUpdateTitle = "Windows Update";

    public static string OptionalUpdatesTitle => L.T("Opcjonalne aktualizacje sterowników");

    public static string ChipsetTitle => L.T("Sterowniki chipsetu");

    public static string GraphicsTitle => L.T("Grafika");

    public static string SupportTitle => L.T("Wsparcie producenta");

    public static string DevicesTitle => L.T("Urządzenia bez sterownika");

    // ---------- krok 1 ----------

    public static string ModelLabel => L.T("Producent i model");

    public const string BiosLabel = "BIOS";
    public const string BitLockerLabel = "BitLocker";

    public static string ActivationLabel => L.T("Aktywacja Windows");

    public static string BitLockerOn => L.T("Włączony");

    public static string BitLockerEncrypting => L.T("Szyfrowanie w toku");

    public static string BitLockerEncryptedProtectionOff => L.T("Dysk zaszyfrowany, ochrona wyłączona");

    public static string BitLockerDecrypting => L.T("Odszyfrowywanie w toku");

    public static string BitLockerOff => L.T("Wyłączony");

    public static string BitLockerOnNote => L.T("Zapisz klucz odzyskiwania, zanim zmienisz BIOS albo dysk.");

    public static string ActivationLicensed => L.T("Aktywowany");

    public static string ActivationUnlicensed => L.T("Nieaktywowany");

    public static string ActivationGrace => L.T("Okres próbny");

    public static string ActivationNotification => L.T("Wymaga aktywacji");

    public static string ActivationNoKey => L.T("Brak klucza produktu");

    // ---------- kroki 2–3 ----------

    public static string OpenWindowsUpdate => L.T("Otwórz Windows Update");

    public static string OpenOptionalUpdates => L.T("Otwórz aktualizacje opcjonalne");

    public static string WindowsUpdateNote => L.T("Otwórz Ustawienia › Windows Update.");

    public static string OptionalUpdatesNote => L.T("Otwórz Ustawienia › Windows Update › Opcje zaawansowane › Aktualizacje opcjonalne.");

    // ---------- krok 4 ----------

    public static string ProcessorLabel => L.T("Procesor");

    public static string ChipsetUnknownNote => L.T("Sterowniki chipsetu przyjdą z Windows Update.");

    // ---------- krok 5 ----------

    public static string GraphicsCardsLabel => L.T("Karty graficzne");

    public static string GraphicsCardLabel => L.T("Karta graficzna");

    public static string NoGraphicsCards => L.T("Nie znaleziono karty graficznej");

    public static string BasicDisplayValue => L.T("Brak sterownika (Podstawowa karta graficzna Microsoft)");

    public static string SearchDriver => L.T("Szukaj sterownika");

    // ---------- krok 6 ----------

    public static string ManufacturerLabel => L.T("Producent");

    public static string SerialLabel => L.T("Numer seryjny");

    public static string VirtualMachineNote => L.T("Maszyna wirtualna – brak strony producenta.");

    public static string SearchSupport => L.T("Szukaj strony wsparcia");

    // ---------- krok 7 ----------

    public static string DevicesLabel => L.T("Urządzenia z problemem");

    public static string UnknownDevice => L.T("Nieznane urządzenie");

    public static string NoDriver => L.T("brak sterownika");

    public static string Disabled => L.T("wyłączone");

    public static string AllDevicesOk => L.T("Wszystkie urządzenia mają sterownik.");

    public static string SearchGoogle => L.T("Szukaj w Google");

    /// <summary>„Strona sterowników Intel” itd. (chipset i grafika).</summary>
    public static string DriversPage(string vendor) => L.F("Strona sterowników {0}", vendor);

    /// <summary>„Strona wsparcia Lenovo” itd.</summary>
    public static string SupportPage(string vendor) => L.F("Strona wsparcia {0}", vendor);

    /// <summary>Etykieta Podstawowej karty Microsoft ze znanym producentem z <c>VEN_</c>: „Karta NVIDIA”.</summary>
    public static string CardOf(string vendor) => L.F("Karta {0}", vendor);

    /// <summary>Kod błędu Menedżera urządzeń inny niż 28 i 22.</summary>
    public static string ProblemCode(uint code) => L.F("problem, kod {0}", code.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
