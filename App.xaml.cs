using System.Configuration;
using System.Data;
using System.Windows;

namespace VideoZoom
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
                new MainWindow().Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Startup Error");
            }
        }
    }

}
