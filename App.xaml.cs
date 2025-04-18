using System.Windows;
using CotrollerDemo.ViewModels;
using CotrollerDemo.Views;
using Prism.Ioc;
using Prism.Navigation.Regions;

namespace CotrollerDemo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<ControllerView, ControllerViewModel>();
        }

        protected override void Initialize()
        {
            base.Initialize();

            var shell = MainWindow;
            shell.Show();

            //导航到ViewA
           var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RequestNavigate("ContentRegion", "ControllerView");
        }
    }
}
