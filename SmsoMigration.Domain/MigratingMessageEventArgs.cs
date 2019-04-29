using System;
using System.Collections.Generic;
using System.Text;

namespace SmsoMigration.Domain
{
    public class MigratingMessageEventArgs : EventArgs
    {
        public string Text { get; set; }
    }
}
