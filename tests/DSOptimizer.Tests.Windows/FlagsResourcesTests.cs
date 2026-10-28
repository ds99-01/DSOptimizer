using System;
using System.IO.Packaging;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Flagi okna wyboru języka (Themes/Flags.xaml w DSOptimizer.exe): obrazy 3:2, dołączone do zasobów aplikacji.</summary>
[Trait("Category", "Windows")]
public class FlagsResourcesTests
{
    [Fact]
    public void Flags_Polish_And_UnitedKingdom_Exist()
    {
        RunOnSta(() =>
        {
            // Statyczny konstruktor Application rejestruje schemat pack:// i pakiet „application” (jak NavMenuWidthTests).
            _ = PackUriHelper.UriSchemePack;
            _ = Application.Current;
            var flags = (ResourceDictionary)Application.LoadComponent(new Uri("/DSOptimizer;component/Themes/Flags.xaml", UriKind.Relative));

            foreach (var key in new[] { "FlagPolish", "FlagUnitedKingdom" })
            {
                var image = Assert.IsType<DrawingImage>(flags[key]);
                Assert.Equal(60, image.Width, 3);
                Assert.Equal(40, image.Height, 3);
            }
        });
    }

    private static void RunOnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
