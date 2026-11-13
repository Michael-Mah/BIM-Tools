using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace BIM.Tools.Updates
{
    internal sealed class UpdateSettings
    {
        internal bool Automatic = true;
        internal bool Configured;
        internal string Token = "";
        private static readonly string FileName = Path.Combine(EditionBrand.SettingsRoot, "updates.xml");

        internal static UpdateSettings Load()
        {
            UpdateSettings settings = new UpdateSettings();
            if (!File.Exists(FileName)) return settings;
            XmlDocument xml = new XmlDocument { XmlResolver = null };
            xml.Load(FileName);
            settings.Automatic = xml.DocumentElement.GetAttribute("automatic") != "false";
            settings.Configured = true;
            string encrypted = xml.DocumentElement.GetAttribute("credential");
            if (encrypted.Length > 0)
                settings.Token = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
            return settings;
        }

        internal void Save()
        {
            XmlDocument xml = new XmlDocument();
            XmlElement root = xml.CreateElement("updates");
            xml.AppendChild(root);
            root.SetAttribute("automatic", Automatic ? "true" : "false");
            root.SetAttribute("credential", Token.Length == 0 ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(Token), null, DataProtectionScope.CurrentUser)));
            Directory.CreateDirectory(EditionBrand.SettingsRoot);
            string temporary = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                xml.Save(temporary);
                if (File.Exists(FileName)) File.Replace(temporary, FileName, null);
                else File.Move(temporary, FileName);
                Configured = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class ReleaseAsset
    {
        public long id { get; set; }
        public string name { get; set; }
        public long size { get; set; }
    }

    internal sealed class ReleaseInfo
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public List<ReleaseAsset> assets { get; set; }
    }

    internal sealed class UpdateManifest
    {
        public string version { get; set; }
        public int[] supportedYears { get; set; }
        public string installerName { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
    }

    internal sealed class AvailableUpdate
    {
        internal Version Version;
        internal ReleaseAsset Installer;
        internal UpdateManifest Manifest;
    }

    internal static class UpdateClient
    {
        internal const string Repository = "Michael-Mah/BIM-Tools";
        private const string Api = "https://api.github.com/repos/" + Repository;
        internal static readonly Version CurrentVersion = typeof(UpdateClient).Assembly.GetName().Version;
        internal static readonly int HostYear = typeof(Autodesk.Navisworks.Api.Document).Assembly.GetName().Version.Major + 2003;

        internal static Version ParseVersion(string tag)
        {
            Version version;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out version))
                throw new InvalidDataException("GitHub release version is invalid.");
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        internal static AvailableUpdate Check(string token)
        {
            ReleaseInfo release = Json<ReleaseInfo>(Api + "/releases/latest", token);
            if (release.draft || release.prerelease) return null;
            Version version = ParseVersion(release.tag_name);
            if (version <= CurrentVersion) return null;
            ReleaseAsset manifestAsset = (release.assets ?? new List<ReleaseAsset>())
                .SingleOrDefault(asset => asset.name == "update.json");
            if (manifestAsset == null)
                throw new InvalidDataException("The latest release has no update package. Contact the maintainer.");
            UpdateManifest manifest = Json<UpdateManifest>(AssetUrl(manifestAsset), token, true);
            ReleaseAsset installer = release.assets.SingleOrDefault(asset => asset.name == manifest.installerName);
            ValidateManifest(manifest, version, installer, HostYear);
            return new AvailableUpdate { Version = version, Installer = installer, Manifest = manifest };
        }

        internal static void ValidateManifest(UpdateManifest manifest, Version version, ReleaseAsset installer, int hostYear)
        {
            if (manifest == null || ParseVersion(manifest.version) != version ||
                manifest.installerName != "BIM-Tools-" + manifest.version + "-Setup.exe" ||
                installer == null || installer.id <= 0 || manifest.size <= 0 || manifest.size > 256L * 1024 * 1024 ||
                installer.size != manifest.size || manifest.sha256 == null ||
                manifest.sha256.Length != 64 || !manifest.sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The GitHub update package is incomplete or invalid.");
            if (manifest.supportedYears == null || !manifest.supportedYears.Contains(hostYear))
                throw new InvalidDataException("The latest release does not include Navisworks " + hostYear + ".");
        }

        internal static string Download(AvailableUpdate update, string token)
        {
            string folder = Path.Combine(EditionBrand.SettingsRoot, "Updates", update.Manifest.version);
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, update.Manifest.installerName);
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using (Stream source = Open(AssetUrl(update.Installer), token, true))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > update.Manifest.size) throw new InvalidDataException("Update download exceeds the expected size.");
                        output.Write(buffer, 0, count);
                    }
                    if (total != update.Manifest.size) throw new InvalidDataException("Update download is incomplete.");
                }
                using (SHA256 sha = SHA256.Create())
                using (FileStream input = File.OpenRead(temporary))
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                    if (!actual.Equals(update.Manifest.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Update checksum verification failed. Please retry.");
                }
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                return target;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string AssetUrl(ReleaseAsset asset)
        {
            if (asset.id <= 0) throw new InvalidDataException("Invalid GitHub release asset.");
            return Api + "/releases/assets/" + asset.id;
        }

        private static T Json<T>(string url, string token, bool asset = false)
        {
            using (Stream source = Open(url, token, asset))
            using (MemoryStream bytes = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (bytes.Length + count > 1024 * 1024) throw new InvalidDataException("Update metadata is too large.");
                    bytes.Write(buffer, 0, count);
                }
                return new JavaScriptSerializer().Deserialize<T>(Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF'));
            }
        }

        private static Stream Open(string url, string token, bool asset)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "BIM-Tools/" + CurrentVersion;
            request.Accept = asset ? "application/octet-stream" : "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            if (!string.IsNullOrWhiteSpace(token)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            try { return request.GetResponse().GetResponseStream(); }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null)
                {
                    HttpStatusCode status = response.StatusCode;
                    response.Dispose();
                    if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                        throw new InvalidOperationException("GitHub access failed. For this private repository, enter a token with Contents read access to " + Repository + ". Check token expiry and GitHub rate limits.");
                }
                throw new InvalidOperationException("Could not reach GitHub. Check your network and try again.");
            }
        }
    }
}
