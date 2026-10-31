using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Sidebar;

/// <summary>A desktop rail remains visually active even when focus moves to another window.</summary>
public sealed class SidebarBackdrop : SystemBackdrop
{
    private readonly bool mica;
    private SystemBackdropConfiguration? configuration;
    private DesktopAcrylicController? acrylicController;
    private MicaController? micaController;

    public SidebarBackdrop(bool mica) => this.mica = mica;
    public bool UsesMica => mica;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        configuration = new SystemBackdropConfiguration { IsInputActive = true };
        UpdateConfiguration(target, root);
        if (mica && MicaController.IsSupported())
        {
            micaController = new MicaController();
            micaController.SetSystemBackdropConfiguration(configuration);
            micaController.AddSystemBackdropTarget(target);
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            acrylicController = new DesktopAcrylicController();
            acrylicController.SetSystemBackdropConfiguration(configuration);
            acrylicController.AddSystemBackdropTarget(target);
        }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, root);
        UpdateConfiguration(target, root);
    }

    private void UpdateConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        if (configuration is null) return;
        var defaults = GetDefaultSystemBackdropConfiguration(target, root);
        configuration.Theme = defaults.Theme;
        configuration.IsHighContrast = defaults.IsHighContrast;
        configuration.HighContrastBackgroundColor = defaults.HighContrastBackgroundColor;
        configuration.IsInputActive = true;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        acrylicController?.RemoveSystemBackdropTarget(target);
        micaController?.RemoveSystemBackdropTarget(target);
        acrylicController?.Dispose();
        micaController?.Dispose();
        acrylicController = null;
        micaController = null;
        configuration = null;
        base.OnTargetDisconnected(target);
    }
}
