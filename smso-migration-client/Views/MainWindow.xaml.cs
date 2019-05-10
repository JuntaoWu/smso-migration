using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Win32;
using SmsoMigration.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace smso_migration_client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel viewModel;

        public MainWindow()
        {
            InitializeComponent();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("migration.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();

            string defaultConnectionString = configuration["connectionString"];
            List<TableDefinition> definitions = configuration.GetSection("definitions").Get<TableDefinition[]>().ToList();

            this.viewModel = this.DataContext as MainWindowViewModel;

            this.viewModel.UpdateDefaultConnectionString(defaultConnectionString);

            this.viewModel.TableDefinitions = definitions;
        }

        // 连接
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(async () =>
            {
                bool isConnected = await this.viewModel.Connect();
                if (!isConnected)
                {
                    MessageBox.Show("Unable to connect database.", "Error");
                }
            });
        }

        // 浏览
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDlg = new OpenFileDialog();

            bool? result = openFileDlg.ShowDialog();

            if (result == true)
            {
                this.viewModel.UnitCodeParam = openFileDlg.FileName;
            }
        }

        // 执行
        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            if (!this.viewModel.IsExecEnabled)
            {
                return;
            }

            var checkResult = this.viewModel.CheckIsExecValid();

            if (checkResult.Severity == ViewModels.Severity.Error)
            {
                MessageBox.Show(checkResult.Message);
                return;
            }

            if (checkResult.Severity == ViewModels.Severity.Warning)
            {
                var confirmationResult = MessageBox.Show(checkResult.Message, "Warning", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (confirmationResult == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            try
            {
                this.viewModel.Exec();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.viewModel.Reset();
            }

        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            if (!this.viewModel.IsExecEnabled)
            {
                return;
            }

            var checkResult = this.viewModel.CheckIsExecValid();

            if (checkResult.Severity == ViewModels.Severity.Error)
            {
                MessageBox.Show(checkResult.Message);
                return;
            }

            if (checkResult.Severity == ViewModels.Severity.Warning)
            {
                var confirmationResult = MessageBox.Show(checkResult.Message, "Warning", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (confirmationResult == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            try
            {
                this.viewModel.Exec();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.viewModel.Reset();
            }
        }
    }
}
