using System;
using System.Collections.Generic;
using System.Text;

namespace SmsoMigration.Domain
{
    public class TableDefinition
    {
        public string TableName { get; set; }

        public string MatchCondition { get; set; }

        public string Description { get; set; }

        public string Type { get; set; }

        public string ForeignKey { get; set; }

        public string ForeignTable { get; set; }

        public string ForeignTablePrimaryKey { get; set; }

        public string Filter { get; set; }

        public string Operator { get; set; }

        public string Value { get; set; }

        public string ExtraWhereClause { get; set; }

        public string DataClassTable { get; set; }

    }
}
