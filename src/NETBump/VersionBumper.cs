using Microsoft.Build.Framework;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;

namespace NETBump
{
    public class VersionBumper
    {
        private string _projectFilePath;
        private XmlDocument _projectFile;
        private string _configuration;
        private Settings _defaultSettings;
        private Settings _settings;

        public delegate void MessageHandler(MessageImportance messageImportance, string message);
        public delegate void ErrorHandler(Exception exception);
        public delegate void VersionHandler(string version);
        public delegate void AssemblyVersionHandler(string version);
        public delegate void FileVersionHandler(string version);

        public event MessageHandler OnMessageReceived;
        public event ErrorHandler OnErrorReceived;
        public event VersionHandler OnVersionChanged;
        public event AssemblyVersionHandler OnAssemblyVersionChanged;
        public event FileVersionHandler OnFileVersionChanged;

        public VersionBumper(string projectFile, string configuration, Settings settings)
        {
            _projectFilePath = projectFile;
            _configuration = configuration;
            _defaultSettings = settings;
        }

        public bool BumpVersion()
        {
            try
            {
                // load the project file
                LoadProjectFile();

                LoadSettings();

                if (_settings.Disabled) return false;

                var oldVersionString = string.Empty;
                var oldAssemblyVersionString = string.Empty;
                var oldFileVersionString = string.Empty;
                var oldInformationalVersionString = string.Empty;

                // check if .NET Core or .NET Framework
                var isNetFrameworkProject = IsNetFrameworkProject();

                if (isNetFrameworkProject)
                {
                    GetVersionsFromNetFrameworkProject(out oldAssemblyVersionString, out oldFileVersionString, out oldInformationalVersionString);
                    //throw new NotImplementedException(".NET Framework projects currently not supported!");
                }
                else
                {
                    GetVersionsFromNetCoreProject(out oldVersionString, out oldAssemblyVersionString, out oldFileVersionString, out oldInformationalVersionString);
                }

                var oldVersion = oldVersionString == string.Empty ? null : new NuGetVersion(oldVersionString);
                var oldAssemblyVersion = oldAssemblyVersionString == string.Empty ? null : new NuGetVersion(oldAssemblyVersionString);
                var oldFileVersion = oldFileVersionString == string.Empty ? null : new NuGetVersion(oldFileVersionString);
                var oldInformationalVersion = oldInformationalVersionString == string.Empty ? null : new NuGetVersion(oldInformationalVersionString);

                var newInformationalVersion = GetNextVersion(oldInformationalVersion);

                var newVersion = new SemanticVersion(newInformationalVersion.Major, newInformationalVersion.Minor, newInformationalVersion.Patch);
                var newAssemblyVersion = new SemanticVersion(newInformationalVersion.Major, 0, 0);
                var newFileVersion = new SemanticVersion(newInformationalVersion.Major, newInformationalVersion.Minor, newInformationalVersion.Patch);

                var newVersionString = newVersion == null ? "" : newVersion.ToString();
                var newAssemblyVersionString = newAssemblyVersion == null ? string.Empty : newAssemblyVersion.ToString(); //newAssemblyVersion.ReleaseLabels.Count() == 0 ? newAssemblyVersion.Version.ToString() : newAssemblyVersion.ToString();
                var newFileVersionString = newFileVersion == null ? string.Empty : newFileVersion.ToString();
                var newInformationalVersionString = newInformationalVersion == null ? string.Empty : newInformationalVersion.ToString();

                if (isNetFrameworkProject)
                {
                    SetVersionsForNetFrameworkProject(newAssemblyVersionString, newFileVersionString, newInformationalVersionString);
                }
                else
                {
                    SetVersionsForNetCoreProject(newVersionString, newAssemblyVersionString, newFileVersionString, newInformationalVersionString);
                }

                if (newVersion != oldVersion)
                {
                    SendMessage(MessageImportance.High, $"Changed Version from {oldVersionString} to {newVersionString}");
                    ChangeVersion(newVersionString);
                }
                if (newAssemblyVersion != oldAssemblyVersion)
                {
                    SendMessage(MessageImportance.High, $"Changed Version from {oldAssemblyVersionString} to {newAssemblyVersionString}");
                    ChangeAssemblyVersion(newAssemblyVersionString);
                }
                if (newFileVersion != oldFileVersion)
                {
                    SendMessage(MessageImportance.High, $"Changed Version from {oldFileVersionString} to {newFileVersionString}");
                    ChangeFileVersion(newFileVersionString);
                }
                if (newInformationalVersion != oldInformationalVersion)
                {
                    SendMessage(MessageImportance.High, $"Changed Version from {oldInformationalVersionString} to {newInformationalVersionString}");
                    ChangeFileVersion(newFileVersionString);
                }

                _projectFile.Save(_projectFilePath);
                return true;
            }
            // FileNotFoundException
            // NotImplementedException
            // ArgumentException
            catch (Exception exception)
            {
                SendErrorFromException(exception);
                return false;
            }
        }

        private void SendMessage(MessageImportance messageImportance, string message)
        {
            OnMessageReceived?.Invoke(messageImportance, message);
        }

        private void SendErrorFromException(Exception exception)
        {
            OnErrorReceived?.Invoke(exception);
        }

        private void ChangeVersion(string version)
        {
            OnVersionChanged?.Invoke(version);
        }

        private void ChangeAssemblyVersion(string version)
        {
            OnAssemblyVersionChanged?.Invoke(version);
        }

        private void ChangeFileVersion(string version)
        {
            OnFileVersionChanged?.Invoke(version);
        }

        private void LoadProjectFile()
        {
            if (File.Exists(_projectFilePath))
            {
                SendMessage(MessageImportance.Low, $"Loading project file \"{_projectFilePath}\"");
                var xmlDocument = new XmlDocument();
                xmlDocument.Load(_projectFilePath);
                _projectFile = xmlDocument;
                return;
            }

            throw new FileNotFoundException($"Project file \"{_projectFilePath}\" not found!");
        }

        private void LoadSettings()
        {
            Settings result = null;

            var settingsFilePath = Path.Combine(Path.GetDirectoryName(_projectFilePath), ".netbump.json");

            if (File.Exists(settingsFilePath))
            {
                Settings settings = null;
                SendMessage(MessageImportance.Low, $"Loading NETBump settings from file \"{settingsFilePath}\"");
                var settingsCollection = JsonSerializer.Create().Deserialize<SettingsCollection>(new JsonTextReader(File.OpenText(settingsFilePath)));
                if (!string.IsNullOrEmpty(_configuration))
                {
                    settingsCollection.Configurations?.TryGetValue(_configuration, out settings);
                }
                result = settings ?? settingsCollection;
            }

            if (result == null)
            {
                SendMessage(MessageImportance.Low, $"No settings found. Using default settings.");
                result = _defaultSettings;
            }
            _settings = result;
        }

        private bool IsNetFrameworkProject()
        {
            var namespaceManager = new XmlNamespaceManager(_projectFile.NameTable);
            namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");

            var frameworkVersionNode = _projectFile.DocumentElement.SelectSingleNode("//msb:TargetFrameworkVersion", namespaceManager);
            return frameworkVersionNode != null;
        }

        private XmlNode GetVersionNode(XmlDocument projectXml, string path)
        {
            /* NET Core:
                <Version>1.0.0</Version>
                <AssemblyVersion>1.0.0.0</AssemblyVersion>
                <FileVersion>1.0.0.0</FileVersion>
            */
            return projectXml.DocumentElement.SelectSingleNode(path);
        }

        private void GetVersionsFromNetCoreProject(out string oldVersion, out string oldAssemblyVersion, out string oldFileVersion, out string oldInformationalVersion)
        {
            oldVersion = string.Empty;
            oldAssemblyVersion = string.Empty;
            oldFileVersion = string.Empty;
            oldInformationalVersion = string.Empty;

            var versionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/Version");
            if (versionNode == null)
            {
                throw new Exception("Version node not found in project configuration!");
            }
            oldVersion = versionNode.InnerText;

            var assemblyVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/AssemblyVersion");
            if (assemblyVersionNode != null)
            {
                oldAssemblyVersion = assemblyVersionNode.InnerText;
            }

            var fileVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/FileVersion");
            if (fileVersionNode != null)
            {
                oldFileVersion = fileVersionNode.InnerText;
            }

            var informalVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/AssemblyInformationalVersion");
            if (informalVersionNode != null)
            {
                oldInformationalVersion = informalVersionNode.InnerText;
            }
        }

        private void GetVersionsFromNetFrameworkProject(out string oldAssemblyVersion, out string oldFileVersion, out string oldInformationalVersion)
        {
            oldAssemblyVersion = string.Empty;
            oldFileVersion = string.Empty;
            oldInformationalVersion = String.Empty;

            var directory = new DirectoryInfo(_projectFilePath);

            var assemblyInfo = Path.Combine(directory.Parent.FullName, "Properties", "assemblyinfo.cs");

            if (!File.Exists(assemblyInfo))
            {
                throw new FileNotFoundException($"AssemblyInfo.cs at \"{assemblyInfo}\" not found!");
            }

            var fileContent = File.ReadAllLines(assemblyInfo);

            var assemblyVersionSearchText = "[assembly: AssemblyVersion(\"";
            var assemblyFileVersionSearchText = "[assembly: AssemblyFileVersion(\"";
            var assemblyInformationalVersionSearchText = "[assembly: AssemblyInformationalVersion(\"";

            foreach (var line in fileContent)
            {
                if(line.StartsWith("//")) continue;

                if (line.StartsWith(assemblyVersionSearchText))
                {
                    var positionAssemblyVersion = line.IndexOf(assemblyVersionSearchText);
                    if (positionAssemblyVersion != -1)
                    {
                        positionAssemblyVersion += assemblyVersionSearchText.Length;
                    }
                    var assemblyVersionEnd = line.IndexOf("\")", positionAssemblyVersion);
                    var assemblyVersion = line.Substring(positionAssemblyVersion, assemblyVersionEnd - positionAssemblyVersion);
                    oldAssemblyVersion = assemblyVersion;
                }

                if (line.StartsWith(assemblyFileVersionSearchText))
                {
                    var positionAssemblyFileVersion = line.IndexOf(assemblyFileVersionSearchText);
                    if (positionAssemblyFileVersion != -1)
                    {
                        positionAssemblyFileVersion += assemblyFileVersionSearchText.Length;
                    }
                    var assemblyFileVersionEnd = line.IndexOf("\")", positionAssemblyFileVersion);
                    var assemblyFileVersion = line.Substring(positionAssemblyFileVersion, assemblyFileVersionEnd - positionAssemblyFileVersion);
                    oldFileVersion = assemblyFileVersion;
                }

                if (line.StartsWith(assemblyInformationalVersionSearchText))
                {
                    var positionInformationalVersion = line.IndexOf(assemblyInformationalVersionSearchText);
                    if (positionInformationalVersion != -1)
                    {
                        positionInformationalVersion += assemblyInformationalVersionSearchText.Length;
                    }
                    var informationalVersionEnd = line.IndexOf("\")", positionInformationalVersion);
                    var informationalVersion = line.Substring(positionInformationalVersion, informationalVersionEnd - positionInformationalVersion);
                    oldInformationalVersion = informationalVersion;
                }
            }
        }

        private List<string> ResetLabels(SemanticVersion oldVersion)
        {
            var labels = oldVersion.ReleaseLabels.ToList();

            if (!string.IsNullOrEmpty(_settings.ResetRevisionLabel))
            {
                if (!_settings.ResetRevisionLabel.All(char.IsLetterOrDigit))
                {
                    throw new ArgumentException($"Invalid reset label \"{_settings.ResetRevisionLabel}\". Only alphanumeric characters are allowed!");
                }

                var regex = new Regex($"^{_settings.ResetRevisionLabel}(\\d*)$");
                foreach (var label in labels)
                {
                    var match = regex.Match(label);
                    if (match.Success)
                    {
                        labels.Remove(label);
                        break;
                    }
                }
            }
            return labels;
        }

        private SemanticVersion GetNextVersion(SemanticVersion oldVersion)
        {
            if (oldVersion == null) return null;

            var nextMajor = GetNextValue(oldVersion.Major, _settings.ResetMajor, _settings.BumpMajor);
            var nextMinor = GetNextValue(oldVersion.Minor, _settings.ResetMinor, _settings.BumpMinor);
            var nextPatch = GetNextValue(oldVersion.Patch, _settings.ResetPatch, _settings.BumpPatch);

            // if label present and ResetLabel = true -> do not increment patch to ensure from 1.2.0-dev1 to 1.2.0 instead of 1.2.0-dev1 to 1.2.1
            if (oldVersion.ReleaseLabels.Count() != 0 && _settings.ResetRevision && (_settings.BumpRevision || _settings.BumpPatch))
            {
                --nextPatch;
            }

            var labels = ResetLabels(oldVersion);

            // Find and modify the release label selected with `BumpLabel`
            // If ResetLabel is true, remove only the specified label.
            if (!string.IsNullOrEmpty(_settings.RevisionLabel) && _settings.RevisionLabel != _settings.ResetRevisionLabel)
            {
                if (!_settings.RevisionLabel.All(char.IsLetterOrDigit))
                {
                    throw new ArgumentException($"Invalid revision label \"{_settings.RevisionLabel}\". Only alphanumeric characters are allowed!");
                }
                var regex = new Regex($"^{_settings.RevisionLabel}(\\d*)$");
                var value = 0;

                //SendMessage(MessageImportance.Low, oldVersion.IsPrerelease.ToString());

                //if (!oldVersion.IsPrerelease)
                //{
                //    SendMessage(MessageImportance.High, $"Incrementing patch from {nextPatch} to {++nextPatch}");
                //}

                foreach (var label in labels)
                {
                    var match = regex.Match(label);
                    if (match.Success)
                    {
                        if (!string.IsNullOrEmpty(match.Groups[1].Value))
                            value = int.Parse(match.Groups[1].Value);
                        labels.Remove(label);
                        break;
                    }
                }

                value++;
                labels.Add(_settings.RevisionLabel + value.ToString(new string('0', _settings.RevisionLabelDigits)));
                if (labels.Count != 0 && oldVersion.ReleaseLabels.Count() == 0)
                {
                    ++nextPatch;
                }
            }

            var newVersion = new SemanticVersion(nextMajor, nextMinor, nextPatch, labels, oldVersion.Metadata);
            return newVersion;
        }

        private int GetNextValue(int oldValue, bool reset, bool bump)
        {
            if (reset)
                return 0;
            if (bump)
                return oldValue + 1;
            return oldValue;
        }

        private void SetVersionsForNetCoreProject(string version, string assemblyVersion, string fileVersion, string assemblyInformationalVersion)
        {
            var versionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/Version");
            if (versionNode == null)
            {
                throw new Exception("Version node not found in project configuration!");
            }
            versionNode.InnerText = version;

            var assemblyVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/AssemblyVersion");
            if (assemblyVersionNode != null)
            {
                assemblyVersionNode.InnerText = assemblyVersion;
            }

            var fileVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/FileVersion");
            if (fileVersionNode != null)
            {
                fileVersionNode.InnerText = fileVersion;
            }

            var assemblyInformationalVersionNode = GetVersionNode(_projectFile, "/Project/PropertyGroup/AssemblyInformationalVersion");
            if (assemblyInformationalVersionNode != null)
            {
                assemblyInformationalVersionNode.InnerText = assemblyInformationalVersion;
            }

        }

        private void SetVersionsForNetFrameworkProject(string assemblyVersion, string fileVersion, string informationalVersion)
        {
            var directory = new DirectoryInfo(_projectFilePath);

            var assemblyInfo = Path.Combine(directory.Parent.FullName, "Properties", "assemblyinfo.cs");

            if (!File.Exists(assemblyInfo))
            {
                throw new FileNotFoundException($"AssemblyInfo.cs at \"{assemblyInfo}\" not found!");
            }

            var fileContent = File.ReadAllLines(assemblyInfo, Encoding.Default);

            var assemblyVersionSearchText = "[assembly: AssemblyVersion(\"";
            var assemblyFileVersionSearchText = "[assembly: AssemblyFileVersion(\"";
            var informationlVersionSearchText = "[assembly: AssemblyInformationalVersion(\"";

            var newAssemblyVersion = $"[assembly: AssemblyVersion(\"{assemblyVersion}\")]";
            var newAssemblyFileVersion = $"[assembly: AssemblyFileVersion(\"{fileVersion}\")]";
            var newInformationalVersion = $"[assembly: AssemblyInformationalVersion(\"{informationalVersion}\")]";

            for (var i = 0; i< fileContent.Length; i++)
            {
                var line = fileContent[i];

                if (line.StartsWith("//")) continue;

                if (line.StartsWith(assemblyVersionSearchText))
                {
                    fileContent[i] = newAssemblyVersion;
                }

                if (line.StartsWith(assemblyFileVersionSearchText))
                {
                    fileContent[i] = newAssemblyFileVersion;
                }

                if (line.StartsWith(informationlVersionSearchText))
                {
                    fileContent[i] = newInformationalVersion;
                }
            }

            File.WriteAllLines(assemblyInfo, fileContent, Encoding.Default);
        }
    }
}
