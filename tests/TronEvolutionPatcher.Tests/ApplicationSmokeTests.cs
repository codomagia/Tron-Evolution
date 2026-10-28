using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using TronEvolutionPatcher.ViewModels;
using NUnit.Framework;

namespace TronEvolutionPatcher.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class ApplicationSmokeTests
    {
        private App application;

        [OneTimeSetUp]
        public void InitializeApplication()
        {
            // The test runner has no WPF entry assembly; resolve relative startup resources in the app.
            Application.ResourceAssembly = typeof(App).Assembly;
            application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.InitializeComponent();
        }

        [Test]
        public void StartupWindowLoadsWithApplicationResources()
        {
            // Load the actual startup URI so broken XAML paths and resource references fail.
            var window = Application.LoadComponent(application.StartupUri) as Window;
            Assert.That(window, Is.Not.Null);
            try
            {
                // Lay out the content without opening a desktop window in the test run.
                var content = window.Content as FrameworkElement;
                Assert.That(content, Is.Not.Null);
                content.Measure(new Size(window.Width, window.Height));
                content.Arrange(new Rect(0, 0, window.Width, window.Height));
                content.UpdateLayout();

                Assert.Multiple(() =>
                {
                    Assert.That(window, Is.InstanceOf<Views.MainWindow>());
                    Assert.That(window.Content, Is.Not.Null);
                    Assert.That(window.Background, Is.InstanceOf<SolidColorBrush>());
                    Assert.That(window.Foreground, Is.InstanceOf<SolidColorBrush>());
                    Assert.That(content.ActualWidth, Is.GreaterThan(0));
                    Assert.That(content.ActualHeight, Is.GreaterThan(0));
                    Assert.That(window.DataContext, Is.InstanceOf<MainViewModel>());
                    Assert.That(((TextBox)window.FindName("GamePathBox")).GetBindingExpression(TextBox.TextProperty).HasError, Is.False);
                    Assert.That(((ComboBox)window.FindName("ResolutionBox")).Text, Is.EqualTo("1920x1080"));
                    Assert.That(((Button)window.FindName("LaunchButton")).IsEnabled, Is.False);
                });
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void ThemeProvidesOpaqueDistinctTextAndBackground()
        {
            var background = application.FindResource("WindowBackground") as SolidColorBrush;
            var foreground = application.FindResource("PrimaryText") as SolidColorBrush;
            var accent = application.FindResource("AccentBrush") as SolidColorBrush;

            Assert.That(background, Is.Not.Null);
            Assert.That(foreground, Is.Not.Null);
            Assert.That(accent, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(background.Color.A, Is.EqualTo(255));
                Assert.That(foreground.Color.A, Is.EqualTo(255));
                Assert.That(accent.Color.A, Is.EqualTo(255));
                Assert.That(foreground.Color, Is.Not.EqualTo(background.Color));
                Assert.That(accent.Color, Is.Not.EqualTo(background.Color));
            });
        }

        [OneTimeTearDown]
        public void ShutdownApplication()
        {
            application?.Shutdown();
        }
    }
}
