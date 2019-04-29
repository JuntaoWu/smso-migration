using Prism.Mvvm;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace smso_migration_client
{

    public class DatabaseViewModel : BindableBase
    {
        public DatabaseViewModel()
        {
            var list = new List<DatabaseNameEntry> { new DatabaseNameEntry("Select") };
            srcDatabaseEntries = new ListCollectionView(list);
            destDatabaseEntries = new ListCollectionView(list);
        }

        private ListCollectionView srcDatabaseEntries;
        public ListCollectionView SrcDatabaseEntries
        {
            get { return srcDatabaseEntries; }
            set { SetProperty(ref srcDatabaseEntries, value); }
        }

        private ListCollectionView destDatabaseEntries;
        public ListCollectionView DestDatabaseEntries
        {
            get { return destDatabaseEntries; }
            set { SetProperty(ref destDatabaseEntries, value); }
        }

        private string srcDatabaseEntry;
        public string SrcDatabaseEntry
        {
            get { return srcDatabaseEntry; }
            set { SetProperty(ref srcDatabaseEntry, value); }
        }

        private string destDatabaseEntry;
        public string DestDatabaseEntry
        {
            get { return destDatabaseEntry; }
            set { SetProperty(ref destDatabaseEntry, value); }
        }
    }
}
