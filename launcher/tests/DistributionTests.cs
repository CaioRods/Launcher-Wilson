using System;
using System.IO;

internal static class DistributionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Distribution regression"); }
    public static void Main()
    {
        Check(Distribution.ButtonText(0, false, false) == "Instalar Excel AW");
        Check(Distribution.ButtonText(0, true, false) == "Abrir Excel AW");
        Check(Distribution.ButtonText(1, true, false) == "Abrir Word AW");
        Check(Distribution.ButtonText(1, true, true) == "Atualizar Word AW");
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 6), true, null).Version == new Version(1, 41, 7));
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 7), true, null) == null);
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 7), false, null) == null);
        var remote = new Update { Version = new Version(1, 42, 0), Url = new Uri("https://github.com/") };
        Check(Distribution.ChooseUpdate(0, new Version(1, 41, 6), true, remote) == remote);
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
