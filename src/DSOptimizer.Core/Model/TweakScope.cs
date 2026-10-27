namespace DSOptimizer.Core.Model;

/// <summary>Czy tweak zmienia maszynę (HKLM, usługi), czy profil zalogowanego użytkownika (HKCU).</summary>
public enum TweakScope
{
    Machine,
    User,
}
