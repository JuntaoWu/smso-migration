using System;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace smso_migration
{
    class Program
    {
        private static List<string> TableNames { get; set; }

        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: migration.exe srcDBName destDBName unitCode|unitCodeList.txt [outputFileName]");
                return;
            }

            string srcDBName = args[0] ?? "smso";
            string destDBName = args[1] ?? "smso-migration";
            string unitCodeParam = args[2] ?? "510031600";
            string outputFileName = args.Length >= 4 ? args[3] : "script.sql";

            List<string> unitCodeList = new List<string>();

            if (File.Exists(unitCodeParam))
            {
                unitCodeList = File.ReadAllLines(unitCodeParam).ToList();
            }
            else
            {
                unitCodeList = new List<string> { unitCodeParam };
            }

            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("migration.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();

            var connectionString = configuration["connectionString"];
            List<TableDefinition> definitions = configuration.GetSection("definitions").Get<TableDefinition[]>().ToList();

            TableNames = GetTableNames(srcDBName, connectionString);

            string ext = outputFileName.LastIndexOf('.') == -1 ? "sql" : outputFileName.Substring(outputFileName.LastIndexOf('.') + 1);

            string scriptCommonFileName = $"{outputFileName.Split('.')[0]}-common.{ext}";
            string scriptUnitFileName = $"{outputFileName.Split('.')[0]}-unit.{ext}";

            if (File.Exists(scriptCommonFileName))
            {
                File.Delete(scriptCommonFileName);
            }
            if (File.Exists(scriptUnitFileName))
            {
                File.Delete(scriptUnitFileName);
            }

            List<string> scriptCommonList = new List<string>();

            var sqlCreateDatabase = CreateDatabase(destDBName, scriptCommonFileName);
            var sqlCommonData = ExtractCommonData(destDBName, definitions, scriptCommonFileName);

            scriptCommonList.AddRange(sqlCreateDatabase);
            scriptCommonList.AddRange(sqlCommonData);

            WriteLog(scriptCommonFileName, scriptCommonList);

            List<string> scriptUnitList = new List<string>();
            unitCodeList.ForEach(unitCode =>
            {
                Dictionary<string, object> unitDataDictionary = GetUnitData(connectionString, unitCode);
                var sqlList = ExtractUnitData(destDBName, definitions, unitDataDictionary, scriptUnitFileName);
                scriptUnitList.AddRange(sqlList);
            });

            WriteLog(scriptUnitFileName, scriptUnitList);

            ExecuteSqlCommandList(connectionString, scriptCommonList.Concat(scriptUnitList).ToList());

            Console.WriteLine("Migration end. Press any key to continue...");
            Console.ReadKey();
        }

        private static void ExecuteSqlCommandList(string connectionString, List<string> sqlCommandList)
        {
            sqlCommandList.ForEach(sqlCommandText =>
            {
                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandTimeout = 1200;
                    command.CommandText = sqlCommandText;

                    Console.WriteLine(sqlCommandText);

                    int rows = command.ExecuteNonQuery();

                    Console.WriteLine($"Affected rows: {rows}");
                }
            });
        }

        private static List<string> GetTableNames(string srcDBName, string connectionString)
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

            Console.WriteLine($"Affected Rows: {tableNames.Count}");

            return tableNames;
        }

        private static bool IsTableMatched(string tableName, List<TableDefinition> definitions)
        {
            var matchedDefinitions = GetMatchedTableDefinitions(tableName, definitions);
            return matchedDefinitions != null && matchedDefinitions.Count > 0;
        }

        private static void WriteLog(string outputFileName, List<string> textLines)
        {
            Console.WriteLine($"Write scripts to {outputFileName}");
            File.AppendAllLines(outputFileName, textLines);
            //Task.Factory.StartNew(() =>
            //{
            //    File.AppendAllLines(outputFileName, new string[] { text });
            //}, TaskCreationOptions.PreferFairness);
        }

        private static List<string> CreateDatabase(string databaseName, string outputFileName)
        {
            List<string> sqlCommandList = new List<string>();

            string sqlCommandText = $"DROP DATABASE IF EXISTS `{databaseName}`; Create DATABASE `{databaseName}` DEFAULT CHARACTER SET utf8;";

            sqlCommandList.Add(sqlCommandText);

            return sqlCommandList;
        }

        private static List<TableDefinition> GetMatchedTableDefinitions(string tableName, List<TableDefinition> definitions)
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

        private static List<string> ExtractUnitData(string destDBName, List<TableDefinition> definitions, Dictionary<string, object> unitDataDictionary, string outputFileName)
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
                    string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE {tableName}; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM {tableName} JOIN {definition.ForeignTable} ON {tableName}.{definition.Column} = {definition.FK} AND {definition.Filter} {definition.Operator} '{value}' {extraFilter}";

                    sqlCommandList.Add(sqlCommandText);
                });
            });

            return sqlCommandList;
        }

        private static Dictionary<string, object> GetUnitData(string connectionString, string unitCode)
        {
            // read FK value first.
            string preSqlCommandText = $"SELECT * FROM t111 WHERE c01 = '{unitCode}'";
            Dictionary<string, object> pairs = new Dictionary<string, object>();
            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                mySqlConnection.Open();

                MySqlCommand command = mySqlConnection.CreateCommand();
                command.CommandText = preSqlCommandText;

                MySqlDataReader reader = command.ExecuteReader();

                reader.Read();
                pairs = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, reader.GetValue);
            }

            return pairs;
        }

        private static List<string> ExtractCommonData(string destDBName, List<TableDefinition> definitions, string outputFileName)
        {
            List<string> sqlCommandList = new List<string>();

            TableNames.Where(tableName => !IsTableMatched(tableName, definitions)).ToList().ForEach(tableName =>
            {
                // copy table now.
                string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE {tableName}; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM {tableName}";

                sqlCommandList.Add(sqlCommandText);
            });

            return sqlCommandList;
        }
    }
}
