using Ravenfall.Updater.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Ravenfall.Updater
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var app = Application.Current as App;
            DataContext = app.IoC.Resolve<MainViewModel>();
        }

        // no title bar: the window moves by dragging anywhere on it
        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
