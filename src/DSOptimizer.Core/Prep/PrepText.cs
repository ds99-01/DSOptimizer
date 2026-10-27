using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Prep;

/// <summary>Teksty strony „Przygotowanie” (zaakceptowane na makiecie Taska 1).</summary>
public static class PrepText
{
    public const string PageTitle = "Przygotowanie";
    public const string PageSubtitle = "Co sprawdzić po instalacji Windows. Program niczego tu nie instaluje ani nie zmienia.";
    public const string RefreshButton = "Odśwież stan";

    // ---------- stany odczytu ----------

    public const string Loading = SpecificationFormatter.LoadingText;
    public const string Failed = "Nie udało się odczytać";
    public const string Unknown = "Nie wiadomo";

    // ---------- tytuły kroków ----------

    public const string ComputerTitle = "Komputer";
    public const string WindowsUpdateTitle = "Windows Update";
    public const string OptionalUpdatesTitle = "Opcjonalne aktualizacje sterowników";
    public const string ChipsetTitle = "Sterowniki chipsetu";
    public const string GraphicsTitle = "Grafika";
    public const string SupportTitle = "Wsparcie producenta";
    public const string DevicesTitle = "Urządzenia bez sterownika";

    // ---------- krok 1 ----------

    public const string ModelLabel = "Producent i model";
    public const string BiosLabel = "BIOS";
    public const string BitLockerLabel = "BitLocker";
    public const string ActivationLabel = "Aktywacja Windows";

    public const string BitLockerOn = "Włączony";
    public const string BitLockerEncrypting = "Szyfrowanie w toku";
    public const string BitLockerEncryptedProtectionOff = "Dysk zaszyfrowany, ochrona wyłączona";
    public const string BitLockerDecrypting = "Odszyfrowywanie w toku";
    public const string BitLockerOff = "Wyłączony";
    public const string BitLockerOnNote = "Zapisz klucz odzyskiwania, zanim zmienisz BIOS albo dysk.";

    public const string ActivationLicensed = "Aktywowany";
    public const string ActivationUnlicensed = "Nieaktywowany";
    public const string ActivationGrace = "Okres próbny";
    public const string ActivationNotification = "Wymaga aktywacji";
    public const string ActivationNoKey = "Brak klucza produktu";

    // ---------- kroki 2–3 ----------

    public const string OpenWindowsUpdate = "Otwórz Windows Update";
    public const string OpenOptionalUpdates = "Otwórz aktualizacje opcjonalne";
    public const string WindowsUpdateNote = "Otwórz Ustawienia › Windows Update.";
    public const string OptionalUpdatesNote = "Otwórz Ustawienia › Windows Update › Opcje zaawansowane › Aktualizacje opcjonalne.";

    // ---------- krok 4 ----------

    public const string ProcessorLabel = "Procesor";
    public const string ChipsetUnknownNote = "Sterowniki chipsetu przyjdą z Windows Update.";

    // ---------- krok 5 ----------

    public const string GraphicsCardsLabel = "Karty graficzne";
    public const string GraphicsCardLabel = "Karta graficzna";
    public const string NoGraphicsCards = "Nie znaleziono karty graficznej";
    public const string BasicDisplayValue = "Brak sterownika (Podstawowa karta graficzna Microsoft)";
    public const string SearchDriver = "Szukaj sterownika";

    // ---------- krok 6 ----------

    public const string ManufacturerLabel = "Producent";
    public const string SerialLabel = "Numer seryjny";
    public const string VirtualMachineNote = "Maszyna wirtualna – brak strony producenta.";
    public const string SearchSupport = "Szukaj strony wsparcia";

    // ---------- krok 7 ----------

    public const string DevicesLabel = "Urządzenia z problemem";
    public const string UnknownDevice = "Nieznane urządzenie";
    public const string NoDriver = "brak sterownika";
    public const string Disabled = "wyłączone";
    public const string AllDevicesOk = "Wszystkie urządzenia mają sterownik.";
    public const string SearchGoogle = "Szukaj w Google";

    /// <summary>„Strona sterowników Intel” itd. (chipset i grafika).</summary>
    public static string DriversPage(string vendor) => "Strona sterowników " + vendor;

    /// <summary>„Strona wsparcia Lenovo” itd.</summary>
    public static string SupportPage(string vendor) => "Strona wsparcia " + vendor;

    /// <summary>Etykieta Podstawowej karty Microsoft ze znanym producentem z <c>VEN_</c>: „Karta NVIDIA”.</summary>
    public static string CardOf(string vendor) => "Karta " + vendor;

    /// <summary>Kod błędu Menedżera urządzeń inny niż 28 i 22.</summary>
    public static string ProblemCode(uint code) => "problem, kod " + code.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
