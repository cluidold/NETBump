using System.Collections.Generic;

namespace NETBump
{
    public class SettingsCollection : Settings
    {
        public Dictionary<string, Settings> Configurations { get; set; }
    }
}
