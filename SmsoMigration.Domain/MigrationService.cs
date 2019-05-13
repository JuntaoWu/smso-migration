using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Common;
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

        public async Task<bool> Connect(string connectionString)
        {
            Databases = new List<string>();

            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                try
                {
                    await mySqlConnection.OpenAsync();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandText = $"SHOW DATABASES;";
                    DbDataReader reader = await command.ExecuteReaderAsync();

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
                Dictionary<string, object> unitDataDictionary = GetUnitInformation(srcDBName, connectionString, unitCode);

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

        public void ExecCopyHistory(string srcDBName, string destDBName, string connectionString, List<TableDefinition> definitions, List<string> unitCodeList)
        {
            TableNames = GetTableNames(srcDBName, connectionString);

            List<string> scriptUnitList = new List<string>();
            unitCodeList.ForEach(unitCode =>
            {
                Dictionary<string, object> unitDataDictionary = GetUnitInformation(srcDBName, connectionString, unitCode);

                if (unitDataDictionary == null || !unitDataDictionary.ContainsKey("id"))
                {
                    throw new Exception("t111中没有对应数据");
                }

                var sqlList = ExtractUnitData(srcDBName, destDBName, definitions, unitDataDictionary);
                scriptUnitList.AddRange(sqlList);
            });

            ExtractOtherHistoryData(srcDBName, destDBName, definitions);

            OnSqlGenerating(this, new SqlGeneratingEventArgs { Scripts = scriptUnitList, GeneratingType = GeneratingType.Unit });

            OnSqlGenerated(this, null);

            Task.Factory.StartNew(() =>
            {
                ExecuteSqlCommandList(connectionString, scriptUnitList.ToList());
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
                switch (def.MatchCondition)
                {
                    case MatchCondition.ConditionEquals:
                        isMatch = tableName == def.TableName;
                        break;
                    case MatchCondition.ConditionStartsWith:
                        isMatch = tableName.StartsWith(def.TableName);
                        break;
                    case MatchCondition.ConditionContains:
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
                    if (definition.Type == DefinitionType.Skip)
                    {
                        return;  // Skip: continue to next definition.
                    }

                    // Default: Copy whole table.
                    string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE `{srcDBName}`.`{tableName}`; " +
                                            $"INSERT INTO `{destDBName}`.`{tableName}` " +
                                            $"SELECT {tableName}.* FROM `{srcDBName}`.`{tableName}` ";

                    // Extract: Append Join & Where
                    if (definition.Type == DefinitionType.Extract)
                    {
                        string value = unitDataDictionary[definition.Value].ToString();
                        string extraWhereClause = string.IsNullOrWhiteSpace(definition.ExtraWhereClause) ? "" : $"AND {definition.ExtraWhereClause}";

                        if (string.IsNullOrWhiteSpace(definition.DataClassTable))
                        {
                            sqlCommandText += $"JOIN `{srcDBName}`.`{definition.ForeignTable}` ON {tableName}.{definition.ForeignKey} =  {definition.ForeignTablePrimaryKey} " +
                                              $"WHERE {definition.Filter} {definition.Operator} '{value}' {extraWhereClause}";
                        }
                        else
                        {
                            // If DataClassTable exists:
                            // SELECT * FROM Table
                            // JOIN DataClassTable ON Table.dataId = DataClassTable.id
                            // JOIN ForeignTable ON DataClassTable.Column = ForeignTable.FK
                            sqlCommandText += $"JOIN `{srcDBName}`.`{definition.DataClassTable}` ON {tableName}.dataId = {definition.DataClassTable}.id " +
                                              $"JOIN `{srcDBName}`.`{definition.ForeignTable}` ON {definition.DataClassTable}.{definition.ForeignKey} = {definition.ForeignTablePrimaryKey} " +
                                              $"WHERE {definition.Filter} {definition.Operator} '{value}' {extraWhereClause}";
                        }
                    }

                    sqlCommandList.Add(sqlCommandText);
                });
            });

            return sqlCommandList;
        }

        private List<string> ExtractOtherHistoryData(string srcDBName, string destDBName, List<TableDefinition> definitions)
        {
            TableDefinition tableDefinition = definitions.First();
            string tableName = tableDefinition.TableName;
            List<string> sqlCommandList = new List<string>();

            List<string> dataClassCodeList = definitions.Select(definition => $"'{definition.TableName}'").ToList();

            string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE `{srcDBName}`.`{tableName}`; " +
                                    $"INSERT INTO `{destDBName}`.`{tableName}` " +
                                    $"SELECT {tableName}.* FROM `{srcDBName}`.`{tableName}` " +
                                    $"WHERE {tableName}.dataclassCode NOT IN ({string.Join(",", dataClassCodeList)})";
            sqlCommandList.Add(sqlCommandText);

            return sqlCommandList;
        }

        private Dictionary<string, object> GetUnitInformation(string srcDBName, string connectionString, string unitCode)
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
