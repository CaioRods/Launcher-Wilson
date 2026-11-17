using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class Update
{
    public Version Version;
    public Uri Url;
    public string Digest;
}

internal static class Distribution
{
    internal static readonly string[] Names = { "Excel AW", "Word AW" };
    private static readonly string[] Slugs = { "Excel-AW", "Word-AW" };
    private static readonly Version[] Bundled = { new Version(1, 41, 6), new Version(0, 18, 2) };

    internal static string LocalPath(int app)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instaladores", Slugs[app] + "-x64.msi");
    }

    internal static Version InstalledVersion(int app, bool machine)
    {
        string key = app == 0 ? "ExcelAW" : "WordCraft";
        string exe = app == 0 ? "gridcraft.exe" : "wordcraft.exe";
        using (var root = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var location = root.OpenSubKey(@"SOFTWARE\" + key))
        {
            string directory = location == null ? null : location.GetValue("InstallDir") as string;
            if (!string.IsNullOrEmpty(directory))
            {
                string path = Path.Combine(directory, exe);
                Version version;
                if (File.Exists(path) && Version.TryParse(FileVersionInfo.GetVersionInfo(path).ProductVersion, out version) &&
                    version > Bundled[app]) return version;
            }
        }
        return Bundled[app];
    }

    internal static Update Latest(int app)
    {
        var repo = "CaioRods/" + Slugs[app];
        string json;
        using (var web = new WilsonWebClient())
        {
            try { json = web.DownloadString("https://api.github.com/repos/" + repo + "/releases/latest"); }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    return null;
                }
                throw;
            }
        }

        var release = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        if (release == null) throw new IOException("Resposta de atualização inválida.");
        var assets = release["assets"] as object[];
        string tag = release["tag_name"] as string;
        Version version;
        if (tag == null || !Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") ||
            !Version.TryParse(tag.TrimStart('v'), out version) || assets == null) return null;
        foreach (object item in assets)
        {
            var asset = item as Dictionary<string, object>;
            if (asset == null || !Equals(asset["name"], Slugs[app] + "-x64.msi")) continue;
            object value;
            if (!asset.TryGetValue("digest", out value)) continue;
            string digest = value as string;
            Uri url;
            if (digest != null && Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$") &&
                Uri.TryCreate(asset["browser_download_url"] as string, UriKind.Absolute, out url) &&
                url.Scheme == "https" && url.Host == "github.com" &&
                url.AbsolutePath.StartsWith("/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                return new Update { Version = version, Url = url, Digest = digest.Substring(7) };
        }
        return null;
    }

    internal static string Download(Update update, string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "atualizacao-x64.msi");
        using (var web = new WilsonWebClient()) web.DownloadFile(update.Url, path);
        using (var sha = SHA256.Create())
        using (var file = File.OpenRead(path))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            if (!string.Equals(actual, update.Digest, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A atualização baixada falhou na verificação de integridade.");
        }
        return path;
    }

    private sealed class WilsonWebClient : WebClient
    {
        internal WilsonWebClient() { Headers[HttpRequestHeader.UserAgent] = "Alimentos-Wilson-App-Central/1.0"; }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = 20000;
            var http = request as HttpWebRequest;
            if (http != null) http.ReadWriteTimeout = 20000;
            return request;
        }
    }
}
