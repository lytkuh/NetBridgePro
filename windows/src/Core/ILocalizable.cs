namespace NetBridge.Windows.Core;

/// <summary>Implemented by pages that can re-apply their localized strings
/// without being recreated (live language switching).</summary>
public interface ILocalizable
{
    void ApplyTexts();
}
