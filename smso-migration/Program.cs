using System;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace smso_migration
{
    class Program
    {
        static void Main(string[] args)
        {
            string databaseName = "smso-dev";
            string unitCode = "510031600";

            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("migration.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();

            var connectionString = configuration["connectionString"];
            List<TableDefinition> definitions = configuration.GetSection("definitions").Get<TableDefinition[]>().ToList();

            List<string> tableNames = new List<string>();

            // read FK value first.
            string preSqlCommandText = $"SELECT * FROM t111 WHERE c01 = {unitCode}";
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

            using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
            {
                mySqlConnection.Open();

                MySqlCommand command = mySqlConnection.CreateCommand();
                command.CommandText = $"SELECT table_name FROM information_schema.TABLES WHERE table_schema = '{databaseName}' and table_type = 'BASE TABLE';";
                MySqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            Console.WriteLine($"Affected Rows: {tableNames.Count}");

            tableNames.ForEach(tableName =>
            {
                var definition = definitions.SingleOrDefault(def =>
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

                if (definition == null)
                {
                    // Console.WriteLine("Exec full table copy.");
                    return;
                }

                string value = pairs[definition.FK.Split(".")[1]].ToString();

                Console.WriteLine($"Exec definition {tableName} {definition.Condition} {definition.TableName} ({definition.Description}) => Filter column: {definition.Column} {definition.Operator} {definition.FK} ('{value}')");

                // copy table now.
                string sqlCommandText = $"CREATE TABLE IF NOT EXISTS `migration`.`{tableName}` LIKE {tableName}; INSERT INTO `migration`.`{tableName}` SELECT * FROM {tableName} WHERE {tableName}.{definition.Column} {definition.Operator} '{value}'";

                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandText = sqlCommandText;
                    command.ExecuteNonQuery();


                }
            });

            Console.WriteLine("Migration end. Press any key to continue...");
            Console.ReadKey();
        }
    }
}
