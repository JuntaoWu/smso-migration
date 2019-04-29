using System;
using System.Collections.Generic;
using System.Text;

namespace SmsoMigration.Domain
{
    public enum GeneratingType
    {
        Common,
        Unit
    }

    public class SqlGeneratingEventArgs : EventArgs
    {
        public List<string> Scripts { get; set; }
        public GeneratingType GeneratingType { get; set; }
    }
}
