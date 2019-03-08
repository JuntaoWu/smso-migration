using System;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
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
            .AddJsonFile("table.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();

            var connectionString = configuration["connectionString"];
            var data = configuration["data"];

            List<TableDefinition> definitions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TableDefinition>>(data);

            List<string> tableNames = new List<string>();

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
                    switch(def.Condition)
                    {
                        case "equal":
                            isMatch = def.TableName == tableName;
                            break;
                        case "contains":
                            isMatch = tableName.Contains(def.TableName);
                            break;
                    }
                    return isMatch;
                });

                if(definition == null)
                {
                    return;
                }

                // read FK value first.
                string preSqlCommandText = $"SELECT * FROM t111 WHERE c01 = {unitCode}";
                string value = "";

                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandText = preSqlCommandText;
                    MySqlDataReader reader = command.ExecuteReader();

                    reader.Read();
                    value = reader.GetString(definition.FK);
                }

                // copy table now.
                string sqlCommandText = $"INSERT INTO `migration`.`{tableName}` SELECT * FROM {tableName} WHERE {tableName}.{definition.Column} {definition.Operator} {value}";

                using (MySqlConnection mySqlConnection = new MySqlConnection(connectionString))
                {
                    mySqlConnection.Open();

                    MySqlCommand command = mySqlConnection.CreateCommand();
                    command.CommandText = sqlCommandText;
                    command.ExecuteNonQuery();
                }
            });
        }
    }
}
