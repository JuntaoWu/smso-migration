﻿using System;
using System.Collections.Generic;
using System.Text;

namespace smso_migration
{
    public class TableDefinition
    {
        public string TableName { get; set; }

        public string Condition { get; set; }

        public string Column { get; set; }

        public string FK { get; set; }

        public string Operator { get; set; }

    }
}
