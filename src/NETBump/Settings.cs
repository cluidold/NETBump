namespace NETBump
{
    public class Settings
    {
        public bool Disabled { get; set; }

        public bool BumpMajor { get; set; }

        public bool BumpMinor { get; set; }

        public bool BumpPatch { get; set; }

        public bool BumpRevision { get; set; }

        public string RevisionLabel { get; set; }

        public bool ResetMajor { get; set; }

        public bool ResetMinor { get; set; }

        public bool ResetPatch { get; set; }

        public bool ResetRevision { get; set; }

        public string ResetRevisionLabel { get; set; }

        public const int DefaultLabelDigits = 2;

        public int RevisionLabelDigits { get; set; } = DefaultLabelDigits;
    }
}
