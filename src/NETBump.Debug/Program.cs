using System;
using System.Reflection;

namespace NETBump.Debug
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("NETBump Debug Tool");

            var projectFile = @"C:\Development\NETBump\src\NETBump.Debug\NETBump.Debug.csproj";

            var assemblyConfigurationAttribute = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var configuration = assemblyConfigurationAttribute?.Configuration;

            //configuration = "Patch";
            var settings = new Settings();
            var bump = new VersionBumper(projectFile, configuration, settings);
            // bump.ProjectPath = @"C:\Development\MSBump\_testfiles\MSBump.Test.csproj";


            var result = bump.BumpVersion();
            Console.WriteLine($"Bump version result: {result}");
            Console.ReadKey();
        }
    }
}
