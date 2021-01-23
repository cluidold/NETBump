using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;

namespace NETBump
{
    public class BumpTask : Task
    {
        [Required]
        public string ProjectPath { get; set; }

        [Output]
        public string NewVersion { get; set; }

        [Output]
        public string NewAssemblyVersion { get; set; }

        [Output]
        public string NewFileVersion { get; set; }

        public string Configuration { get; set; }

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


        public override bool Execute()
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            var currentSettings = new Settings
            {
                Disabled = Disabled,
                BumpMajor = BumpMajor,
                BumpMinor = BumpMinor,
                BumpPatch = BumpPatch,
                BumpRevision = BumpRevision,
                RevisionLabel = RevisionLabel,
                ResetMajor = ResetMajor,
                ResetMinor = ResetMinor,
                ResetPatch = ResetPatch,
                ResetRevision = ResetRevision,
                ResetRevisionLabel = ResetRevisionLabel,
                RevisionLabelDigits = RevisionLabelDigits
            };

            try
            {
                Log.LogMessage(MessageImportance.Normal, "NETBump task started");
                stopwatch.Start();

                var bumper = new VersionBumper(ProjectPath, Configuration, currentSettings);
                bumper.OnMessageReceived += this.OnMessageReceived;
                bumper.OnErrorReceived += this.OnErrorReceived;
                bumper.OnVersionChanged += this.OnVersionChanged;
                bumper.OnAssemblyVersionChanged += this.OnAssemblyVersionChanged;
                bumper.OnFileVersionChanged += this.OnFileVersionChanged;

                bumper.BumpVersion();

                stopwatch.Stop();
                Log.LogMessage(MessageImportance.Normal, $"NETBump finished after {stopwatch.ElapsedMilliseconds} ms");
                return true;
            }
            catch (Exception e)
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                }

                Log.LogMessage(MessageImportance.Normal, $"NETBump task failed after {stopwatch.ElapsedMilliseconds} ms");
                Log.LogErrorFromException(e);
                return false;
            }
            finally
            {
                stopwatch.Reset();
            }
        }

        private void OnErrorReceived(Exception exception)
        {
            Log.LogErrorFromException(exception);
        }

        private void OnMessageReceived(MessageImportance messageImportance, string message)
        {
            Log.LogMessage(messageImportance, message);
        }

        private void OnVersionChanged(string version)
        {
            NewVersion = version;
        }

        private void OnAssemblyVersionChanged(string version)
        {
            NewAssemblyVersion = version;
        }

        private void OnFileVersionChanged(string version)
        {
            NewFileVersion = version;
        }
    }
}
