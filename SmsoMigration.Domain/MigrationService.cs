using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmsoMigration.Domain
{
    public class MigrationService
    {
        public List<string> Databases { get; set; }
        private static List<string> TableNames { get; set; }

        public event EventHandler<SqlGeneratingEventArgs> OnSqlGenerating = (sender, args) => { };
        public event EventHandler<EventArgs> OnSqlGenerated = (sender, args) => { };
        public event EventHandler<MigratingMessageEventArgs> OnMigrating = (sender, args) => { };
        public event EventHandler<MigratingMessageEventArgs> OnMigrated = (sender, args) => { };

        public bool Connect(string connectionString)
        {
            Databases = new List<string>();

            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                try
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandText = $"SHOW DATABASES;";
                    MySqlDataReader reader = command.ExecuteReader();

                    while (reader.Read())
                    {
                        Databases.Add(reader.GetString(0));
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public void Exec(string srcDBName, string destDBName, string connectionString, List<TableDefinition> definitions, List<string> unitCodeList, bool isExtractingCommonTable = false)
        {
            TableNames = GetTableNames(srcDBName, connectionString);

            List<string> scriptCommonList = new List<string>();

            if (isExtractingCommonTable)
            {
                var sqlCreateDatabase = CreateDatabase(destDBName);
                var sqlCommonData = ExtractCommonData(srcDBName, destDBName, definitions);

                scriptCommonList.AddRange(sqlCreateDatabase);
                scriptCommonList.AddRange(sqlCommonData);

                OnSqlGenerating(this, new SqlGeneratingEventArgs { Scripts = scriptCommonList, GeneratingType = GeneratingType.Common });
            }

            List<string> scriptUnitList = new List<string>();
            unitCodeList.ForEach(unitCode =>
            {
                Dictionary<string, object> unitDataDictionary = GetUnitData(srcDBName, connectionString, unitCode);

                if (unitDataDictionary == null || !unitDataDictionary.ContainsKey("id"))
                {
                    throw new Exception("t111中没有对应数据");
                }

                var sqlList = ExtractUnitData(srcDBName, destDBName, definitions, unitDataDictionary);
                scriptUnitList.AddRange(sqlList);
            });

            OnSqlGenerating(this, new SqlGeneratingEventArgs { Scripts = scriptUnitList, GeneratingType = GeneratingType.Unit });

            OnSqlGenerated(this, null);

            Task.Factory.StartNew(() =>
            {
                ExecuteSqlCommandList(connectionString, scriptCommonList.Concat(scriptUnitList).ToList());
            });
        }

        private void ExecuteSqlCommandList(string connectionString, List<string> sqlCommandList)
        {
            sqlCommandList.ForEach(sqlCommandText =>
            {
                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandTimeout = 1200;
                    command.CommandText = sqlCommandText;

                    OnMigrating(this, new MigratingMessageEventArgs { Text = sqlCommandText });

                    int rows = command.ExecuteNonQuery();

                    OnMigrating(this, new MigratingMessageEventArgs { Text = $"Affected rows: {rows}" });
                }
            });

            OnMigrated(this, new MigratingMessageEventArgs { Text = $"Migration Completed." });
        }

        public List<string> GetTableNames(string srcDBName, string connectionString)
        {
            List<string> tableNames = new List<string>();
            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                mySqlConnection.Open();

                MySqlCommand command = mySqlConnection.CreateCommand();
                command.CommandText = $"SELECT table_name FROM information_schema.TABLES WHERE table_schema = '{srcDBName}' and table_type = 'BASE TABLE';";
                MySqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            OnMigrating(this, new MigratingMessageEventArgs { Text = $"Affected Rows: {tableNames.Count}" });

            return tableNames;
        }

        private bool IsTableMatched(string tableName, List<TableDefinition> definitions)
        {
            var matchedDefinitions = GetMatchedTableDefinitions(tableName, definitions);
            return matchedDefinitions != null && matchedDefinitions.Count > 0;
        }

        private void WriteLog(string outputFileName, List<string> textLines)
        {
            // Console.WriteLine($"Write scripts to {outputFileName}");
            // todo:
            // File.AppendAllLines(outputFileName, textLines);

            //Task.Factory.StartNew(() =>
            //{
            //    File.AppendAllLines(outputFileName, new string[] { text });
            //}, TaskCreationOptions.PreferFairness);
        }

        private List<string> CreateDatabase(string databaseName)
        {
            List<string> sqlCommandList = new List<string>();

            string sqlCommandText = $"Create DATABASE IF NOT EXISTS `{databaseName}` DEFAULT CHARACTER SET utf8 ;";

            sqlCommandList.Add(sqlCommandText);

            return sqlCommandList;
        }

        private List<TableDefinition> GetMatchedTableDefinitions(string tableName, List<TableDefinition> definitions)
        {
            var filteredDefinitions = definitions.Where(def =>
            {
                bool isMatch = false;
                switch (def.Condition)
                {
                    case "equals":
                        isMatch = def.TableName == tableName;
                        break;
                    case "contains":
                        isMatch = tableName.Contains(def.TableName);
                        break;
                }
                return isMatch;
            });

            return filteredDefinitions.ToList();
        }

        private List<string> ExtractUnitData(string srcDBName, string destDBName, List<TableDefinition> definitions, Dictionary<string, object> unitDataDictionary)
        {
            List<string> sqlCommandList = new List<string>();

            TableNames.Where(tableName => IsTableMatched(tableName, definitions)).ToList().ForEach(tableName =>
            {
                var filteredDefinitions = GetMatchedTableDefinitions(tableName, definitions);

                filteredDefinitions.ForEach(definition =>
                {
                    string value = unitDataDictionary[definition.Value].ToString();
                    string extraFilter = string.IsNullOrWhiteSpace(definition.ExtraFilter) ? "" : $"AND {definition.ExtraFilter}";

                    // copy table sqlCommandText.
                    string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE `{srcDBName}`.`{tableName}`; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM `{srcDBName}`.`{tableName}` JOIN `{srcDBName}`.`{definition.ForeignTable}` ON {tableName}.{definition.Column} = {definition.FK} AND {definition.Filter} {definition.Operator} '{value}' {extraFilter}";

                    sqlCommandList.Add(sqlCommandText);
                });
            });

            return sqlCommandList;
        }

        private Dictionary<string, object> GetUnitData(string srcDBName, string connectionString, string unitCode)
        {
            // read FK value first.
            string preSqlCommandText = $"SELECT * FROM `{srcDBName}`.`t111` WHERE c01 = '{unitCode}'";
            Dictionary<string, object> pairs = new Dictionary<string, object>();
            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                mySqlConnection.Open();

                MySqlCommand command = mySqlConnection.CreateCommand();
                command.CommandText = preSqlCommandText;

                MySqlDataReader reader = command.ExecuteReader();

                if (reader.HasRows)
                {
                    // todo: HasRows then do something.
                    reader.Read();

                    pairs = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, reader.GetValue);
                }
            }

            return pairs;
        }

        private List<string> ExtractCommonData(string srcDBName, string destDBName, List<TableDefinition> definitions)
        {
            List<string> sqlCommandList = new List<string>();

            TableNames.Where(tableName => !IsTableMatched(tableName, definitions)).ToList().ForEach(tableName =>
            {
                // copy table now.
                string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE `{srcDBName}`.`{tableName}`; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM `{srcDBName}`.`{tableName}`";

                sqlCommandList.Add(sqlCommandText);
            });

            return sqlCommandList;
        }
    }

}
