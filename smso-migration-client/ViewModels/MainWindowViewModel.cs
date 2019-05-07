using Prism.Mvvm;
using smso_migration_client.ViewModels;
using SmsoMigration.Domain;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace smso_migration_client
{
    public class MainWindowViewModel : BindableBase
    {
        public List<TableDefinition> TableDefinitions { get; set; }

        public void UpdateDefaultConnectionString(string defaultConnectionString)
        {
            // $"Server={Host};Database=information_schema;Uid={UserName};Pwd={Password};"
            if (string.IsNullOrWhiteSpace(defaultConnectionString))
            {
                return;
            }

            ConnectionString = defaultConnectionString;

            var dictionary = defaultConnectionString.Split(';').Where(section => !string.IsNullOrWhiteSpace(section)).ToDictionary(section =>
            {
                var pairString = section.Split('=');
                return pairString[0];
            }, (section) =>
            {
                var pairString = section.Split('=');
                return pairString[1];
            });

            Host = dictionary["Server"];
            UserName = dictionary["Uid"];
            Password = dictionary["Pwd"];
        }

        private MigrationService migrationService;
        public MainWindowViewModel()
        {
            this.migrationService = new MigrationService();
            this.migrationService.OnSqlGenerating += MigrationService_OnSqlGenerating;
            this.migrationService.OnSqlGenerated += MigrationService_OnSqlGenerated;
            this.migrationService.OnMigrating += MigrationService_OnMigratingMessage;
            this.migrationService.OnMigrated += MigrationService_OnMigrated;
        }

        private void MigrationService_OnMigrated(object sender, MigratingMessageEventArgs e)
        {
            this.StatusMessage = e.Text;
            this.IsExecuting = false;
        }

        private void MigrationService_OnMigratingMessage(object sender, MigratingMessageEventArgs e)
        {
            Console.WriteLine(e.Text);
            this.Message += e.Text + "\n";
        }

        private void MigrationService_OnSqlGenerating(object sender, SqlGeneratingEventArgs e)
        {
            if (e.GeneratingType == GeneratingType.Common)
            {
                this.ScriptsCommon = this.ScriptsCommon.Concat(e.Scripts).ToList();
            }
            else
            {
                this.ScriptsUnit = this.ScriptsUnit.Concat(e.Scripts).ToList();
            }
        }

        private void MigrationService_OnSqlGenerated(object sender, EventArgs e)
        {
            Thread.Sleep(1000);
            this.IsMessageTabSelected = true;
        }

        public string ConnectionString { get; set; }

        #region Binding Properties

        // Binding Host
        private string host = "localhost";
        public string Host
        {
            get { return host; }
            set
            {
                SetProperty(ref host, value, () =>
                {
                    RaisePropertyChanged(nameof(IsButtonConnectEnabled));
                    RaisePropertyChanged(nameof(IsExecEnabled));
                });
            }
        }

        // Binding UserName
        private string userName = "sa";
        public string UserName
        {
            get { return userName; }
            set
            {
                SetProperty(ref userName, value, () =>
                {
                    RaisePropertyChanged(nameof(IsButtonConnectEnabled));
                    RaisePropertyChanged(nameof(IsExecEnabled));
                });
            }
        }

        // Binding Password
        private string password = "0penSUSE";
        public string Password
        {
            get { return password; }
            set
            {
                SetProperty(ref password, value, () =>
                {
                    RaisePropertyChanged(nameof(IsButtonConnectEnabled));
                    RaisePropertyChanged(nameof(IsExecEnabled));
                });
            }
        }

        // Binding DatabaseViewModel
        private DatabaseViewModel databaseViewModel = new DatabaseViewModel();
        public DatabaseViewModel DatabaseViewModel
        {
            get { return databaseViewModel; }
            set { SetProperty(ref databaseViewModel, value); }
        }

        // Binding ConnectionStatus
        private bool isConnected = false;
        public bool IsConnected
        {
            get { return isConnected; }
            set
            {
                SetProperty(ref isConnected, value, () =>
                {
                    RaisePropertyChanged(nameof(ConnectionStatus));
                    RaisePropertyChanged(nameof(IsExecEnabled));
                });
            }
        }
        public string ConnectionStatus => IsConnected ? "数据库已连接!" : "数据库未连接!";

        // Binding IsButtonConnectEnabled
        public bool IsButtonConnectEnabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);

        // Binding UnitCodeParam
        private string unitCodeParam = "输入单位代码或打开列表文件";
        public string UnitCodeParam
        {
            get { return unitCodeParam; }
            set
            {
                SetProperty(ref unitCodeParam, value);
            }
        }

        private bool isExtractingCommonTable;
        public bool IsExtractingCommonTable
        {
            get { return isExtractingCommonTable; }
            set { SetProperty(ref isExtractingCommonTable, value); }
        }

        // Binding IsExecEnabled
        public bool IsExecEnabled => !string.IsNullOrWhiteSpace(Host)
            && !string.IsNullOrWhiteSpace(UserName)
            && !string.IsNullOrWhiteSpace(Password)
            && !string.IsNullOrWhiteSpace(UnitCodeParam)
            && IsConnected
            && !IsExecuting;

        private List<string> scriptsCommon = new List<string>();
        public List<string> ScriptsCommon
        {
            get { return scriptsCommon; }
            set
            {
                SetProperty(ref scriptsCommon, value, () =>
                {
                    RaisePropertyChanged(nameof(ScriptsText));
                });
            }
        }

        private List<string> scriptsUnit = new List<string>();
        public List<string> ScriptsUnit
        {
            get { return scriptsUnit; }
            set
            {
                SetProperty(ref scriptsUnit, value, () =>
                {
                    RaisePropertyChanged(nameof(ScriptsText));
                });
            }
        }

        public string ScriptsText => String.Join("\n", ScriptsCommon.Concat(ScriptsUnit).ToList());

        private string message = "";
        public string Message
        {
            get { return message; }
            set { SetProperty(ref message, value); }
        }

        private bool isMessageTabSelected = false;
        public bool IsMessageTabSelected
        {
            get { return isMessageTabSelected; }
            set { SetProperty(ref isMessageTabSelected, value); }
        }

        private string statusMessage = "";
        public string StatusMessage
        {
            get { return statusMessage; }
            set { SetProperty(ref statusMessage, value); }
        }

        private bool isExecuting = false;
        public bool IsExecuting
        {
            get { return isExecuting; }
            set
            {
                SetProperty(ref isExecuting, value, () =>
                {
                    RaisePropertyChanged(nameof(IsExecEnabled));
                });
            }
        }

        #endregion

        public async Task<bool> Connect()
        {
            ConnectionString = $"Server={Host};Database=information_schema;Uid={UserName};Pwd={Password};";
            IsConnected = await this.migrationService.Connect(ConnectionString);

            var list = this.migrationService.Databases.Select(name => new DatabaseNameEntry(name)).ToList();

            DatabaseViewModel.SrcDatabaseEntries = new ListCollectionView(list);
            DatabaseViewModel.DestDatabaseEntries = new ListCollectionView(list);

            return IsConnected;
        }

        public void OpenFile()
        {

        }

        public CheckResult CheckIsExecValid()
        {
            if (this.DatabaseViewModel.SrcDatabaseEntry == this.DatabaseViewModel.DestDatabaseEntry)
            {
                return new CheckResult
                {
                    Message = "源数据库不能与目标数据库相同",
                    Severity = Severity.Error,
                };
            }
            string srcDBName = DatabaseViewModel.SrcDatabaseEntry;
            string destDBName = DatabaseViewModel.DestDatabaseEntry;

            // todo: Check if dest database contains table already.
            var destTables = this.migrationService.GetTableNames(destDBName, ConnectionString);

            if (destTables != null && destTables.Count > 0)
            {
                return new CheckResult
                {
                    Message = "目标数据库不为空,拷贝可能失败,是否继续?",
                    Severity = Severity.Warning,
                };
            }

            // All conditions ok.
            return new CheckResult
            {
                Severity = Severity.Information,
            };
        }


        public void Exec()
        {
            string srcDBName = DatabaseViewModel.SrcDatabaseEntry;
            string destDBName = DatabaseViewModel.DestDatabaseEntry;

            List<string> unitCodeList = new List<string> { UnitCodeParam };

            if (File.Exists(UnitCodeParam))
            {
                unitCodeList = File.ReadAllLines(UnitCodeParam).ToList();
            }

            this.IsExecuting = true;
            this.StatusMessage = "Migration Started.";

            this.migrationService.Exec(srcDBName, destDBName, ConnectionString, TableDefinitions, unitCodeList, IsExtractingCommonTable);
        }

        public void Reset()
        {
            this.IsExecuting = false;
            this.StatusMessage = "";
        }
    }
}
