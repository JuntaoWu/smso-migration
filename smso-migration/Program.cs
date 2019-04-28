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
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine($"Usage: migration.exe srcDBName destDBName unitCode|unitCodeList.txt [log.txt]");
                return;
            }

            string srcDBName = args[0] ?? "smso";
            string destDBName = args[1] ?? "smso-migration";
            string unitCodeParam = args[2] ?? "510031600";
            string outputFileName = args.Length >= 4 ? args[3] : "log.txt";

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

            List<string> tableNames = GetTableNames(srcDBName, connectionString);

            if (File.Exists(outputFileName))
            {
                File.Delete(outputFileName);
            }

            unitCodeList.ForEach(unitCode =>
            {
                ExtractUnitData(destDBName, connectionString, tableNames, definitions, unitCode, outputFileName);
            });

            ExtractCommonData(destDBName, connectionString, tableNames, definitions, outputFileName);

            Console.WriteLine("Migration end. Press any key to continue...");
            Console.ReadKey();
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

        private static void ExtractUnitData(string destDBName, string connectionString, List<string> tableNames, List<TableDefinition> definitions, string unitCode, string outputFileName)
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

            tableNames.Where(tableName => IsTableMatched(tableName, definitions)).ToList().ForEach(tableName =>
            {
                var filteredDefinitions = GetMatchedTableDefinitions(tableName, definitions);

                filteredDefinitions.ForEach(definition =>
                {
                    string value = pairs[definition.Value].ToString();
                    string extraFilter = string.IsNullOrWhiteSpace(definition.ExtraFilter) ? "" : $"AND {definition.ExtraFilter}";

                    // copy table now.
                    string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE {tableName}; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM {tableName} JOIN {definition.ForeignTable} ON {tableName}.{definition.Column} = {definition.FK} AND {definition.Filter} {definition.Operator} '{value}' {extraFilter}";

                    Console.WriteLine(sqlCommandText);

                    Task.Factory.StartNew(() =>
                    {
                        File.AppendAllLines(outputFileName, new string[] { sqlCommandText });
                    }, TaskCreationOptions.PreferFairness);
                    
                    using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                    {
                        mySqlConnection.Open();

                        MySqlCommand command = mySqlConnection.CreateCommand();
                        command.CommandTimeout = 1200;
                        command.CommandText = sqlCommandText;
                        command.ExecuteNonQuery();
                    }
                });

            });
        }

        private static void ExtractCommonData(string destDBName, string connectionString, List<string> tableNames, List<TableDefinition> definitions, string outputFileName)
        {
            tableNames.Where(tableName => !IsTableMatched(tableName, definitions)).ToList().ForEach(tableName =>
            {
                // copy table now.
                string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `{destDBName}`.`{tableName}` LIKE {tableName}; INSERT INTO `{destDBName}`.`{tableName}` SELECT {tableName}.* FROM {tableName}";

                Console.WriteLine(sqlCommandText);
                Task.Factory.StartNew(() =>
                {
                    File.AppendAllLines(outputFileName, new string[] { sqlCommandText });
                }, TaskCreationOptions.PreferFairness);

                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandTimeout = 1200;
                    command.CommandText = sqlCommandText;
                    command.ExecuteNonQuery();
                }
            });
        }
    }
}
