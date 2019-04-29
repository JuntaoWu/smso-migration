using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace smso_migration_client
{
    public class DatabaseNameEntry
    {
        public string Name { get; set; }
        public DatabaseNameEntry(string name)
        {
            Name = name;
        }
    }

}
