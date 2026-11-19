using System;
using System.IO;

internal static class DistributionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Distribution regression"); }
    public static void Main(string[] args)
    {
        if (args.Length == 2)
        {
            int app = int.Parse(args[0]);
            Check(string.Equals(Distribution.InstalledPath(app), args[1], StringComparison.OrdinalIgnoreCase));
            Version version = Distribution.InstalledVersion(app, false);
            Check(version.Major == Distribution.BundledUpdate(app).Version.Major && version.Minor == Distribution.BundledUpdate(app).Version.Minor
                && version.Build == Distribution.BundledUpdate(app).Version.Build);
            Check(Distribution.ChooseUpdate(app, version, true, null) == null);
            Check(Distribution.ButtonText(app, true, false).StartsWith("Abrir "));
            Console.WriteLine("Installed app is detected and offers Abrir.");
            return;
        }
        Check(Distribution.ButtonText(0, false, false) == "Instalar Excel AW");
        Check(Distribution.ButtonText(0, true, false) == "Abrir Excel AW");
        Check(Distribution.ButtonText(1, true, false) == "Abrir Word AW");
        Check(Distribution.ButtonText(1, true, true) == "Atualizar Word AW");
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 8), true, null).Version == new Version(1, 41, 12));
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 12), true, null) == null);
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 12), false, null) == null);
        Check(Distribution.ChooseUpdate(1, new Version(0, 18, 4), true, null).Version == new Version(0, 18, 8));
        var remote = new Update { Version = new Version(1, 42, 0), Url = new Uri("https://github.com/") };
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 8), true, remote) == remote);
        Check(Distribution.ChooseUpdate(0, new Version(2, 0, 0), true, remote) == null);
        string folder = Path.Combine(Path.GetTempPath(), "AW-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "gridcraft.exe");
            Check(Distribution.ExistingExecutable(path, "gridcraft.exe") == null);
            File.WriteAllText(path, "test fixture, never executed");
            Check(Distribution.ExistingExecutable("\"" + path + "\"", "gridcraft.exe") == path);
            Check(Distribution.ExistingExecutable(path, "wordcraft.exe") == null);
            File.Delete(path);
            Check(Distribution.ExistingExecutable(path, "gridcraft.exe") == null);
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("Distribution tests passed.");
    }
}
