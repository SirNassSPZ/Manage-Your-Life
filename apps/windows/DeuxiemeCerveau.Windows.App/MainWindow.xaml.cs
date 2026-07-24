using System.IO;
using System.Windows;
using DeuxiemeCerveau.Windows.App.ViewModels;
using DeuxiemeCerveau.Windows.Core.Depot;

namespace DeuxiemeCerveau.Windows.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "DeuxiemeCerveau");
        Directory.CreateDirectory(appFolder);

        var dbPath = Path.Combine(appFolder, "deuxieme_cerveau.db");
        var depot = new DepotLocalSqlite($"Data Source={dbPath}");

        DataContext = new MainViewModel(depot);
    }
}
