using System;

namespace Ravenfall.Updater.Core
{
    public class UpdateData
    {
        public string DownloadUrl { get; set; }
        public string Version { get; set; }
        public bool IsAlpha => Version?.IndexOf("a", StringComparison.OrdinalIgnoreCase) >= 0;
        public bool IsBeta => Version?.IndexOf("b", StringComparison.OrdinalIgnoreCase) >= 0;
        public DateTime Released { get; set; }
    }
}