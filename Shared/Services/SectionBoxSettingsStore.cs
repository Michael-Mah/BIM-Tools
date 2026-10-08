using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    internal sealed class SectionBoxBuilderSettings
    {
        public string ModelKey { get; set; }

        public string LevelSource { get; set; }

        public string GridSource { get; set; }

        public string BottomLevel { get; set; }

        public string TopLevel { get; set; }

        public string[] GridBoundaries { get; set; }

        public string SectionBoxSource { get; set; }

        public bool AngleNoticeShown { get; set; }

        public SectionBoxBuilderSettings()
        {
            GridBoundaries = new string[4];
            SectionBoxSource = SectionBoxWorkspaceService.NativeSourceName;
        }
    }

    /// <summary>
    /// Stores Section Box choices separately for each NWC, NWD, or NWF path.
    /// Settings live outside the model so opening and closing Navisworks does not
    /// discard the user's last Level/Grid selections.
    /// </summary>
    internal sealed class SectionBoxSettingsStore
    {
        private static readonly object SyncRoot = new object();

        public SectionBoxBuilderSettings Load(Document document)
        {
            string key = GetModelKey(document);
            SectionBoxBuilderSettings settings = new SectionBoxBuilderSettings();
            settings.ModelKey = key;
            if (string.IsNullOrWhiteSpace(key))
            {
                return settings;
            }

            string path = GetSettingsPath(key);
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                lock (SyncRoot)
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(path);
                    XmlElement root = xml.DocumentElement;
                    if (root == null || root.Name != "SectionBoxBuilder")
                    {
                        return settings;
                    }

                    settings.LevelSource = Read(root, "LevelSource");
                    settings.GridSource = Read(root, "GridSource");
                    settings.BottomLevel = Read(root, "BottomLevel");
                    settings.TopLevel = Read(root, "TopLevel");
                    for (int index = 0; index < settings.GridBoundaries.Length; index++)
                    {
                        settings.GridBoundaries[index] = Read(
                            root,
                            "Grid" + (index + 1));
                    }
                    string source = Read(root, "SectionBoxSource");
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        settings.SectionBoxSource = source;
                    }
                    bool notice;
                    if (bool.TryParse(Read(root, "AngleNoticeShown"), out notice))
                    {
                        settings.AngleNoticeShown = notice;
                    }
                }
            }
            catch
            {
                // A damaged preference file must never prevent the Builder from
                // opening. A later successful save replaces it with valid XML.
            }
            return settings;
        }

        public void Save(SectionBoxBuilderSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.ModelKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                string path = GetSettingsPath(settings.ModelKey);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                XmlDocument xml = new XmlDocument();
                XmlElement root = xml.CreateElement("SectionBoxBuilder");
                root.SetAttribute("model", settings.ModelKey);
                xml.AppendChild(root);
                Write(xml, root, "LevelSource", settings.LevelSource);
                Write(xml, root, "GridSource", settings.GridSource);
                Write(xml, root, "BottomLevel", settings.BottomLevel);
                Write(xml, root, "TopLevel", settings.TopLevel);
                for (int index = 0; index < 4; index++)
                {
                    string value = settings.GridBoundaries != null &&
                                   index < settings.GridBoundaries.Length
                        ? settings.GridBoundaries[index]
                        : string.Empty;
                    Write(xml, root, "Grid" + (index + 1), value);
                }
                Write(xml, root, "SectionBoxSource", settings.SectionBoxSource);
                Write(
                    xml,
                    root,
                    "AngleNoticeShown",
                    settings.AngleNoticeShown.ToString());
                xml.Save(path);
            }
        }

        public static string GetModelKey(Document document)
        {
            if (document == null || document.IsDisposed || document.IsClear)
            {
                return string.Empty;
            }

            string path = document.CurrentFileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = document.FileName;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                return "unsaved|" + (document.Title ?? string.Empty).Trim();
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
            }
            return path.Trim().ToUpperInvariant();
        }

        private static string GetSettingsPath(string modelKey)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(modelKey);
            byte[] hash;
            using (SHA256 algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(bytes);
            }
            StringBuilder name = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                name.Append(value.ToString("x2"));
            }

            return Path.Combine(
                EditionBrand.SettingsRoot,
                "SectionBox",
                name + ".xml");
        }

        private static string Read(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? string.Empty : node.InnerText;
        }

        private static void Write(
            XmlDocument xml,
            XmlElement root,
            string name,
            string value)
        {
            XmlElement element = xml.CreateElement(name);
            element.InnerText = value ?? string.Empty;
            root.AppendChild(element);
        }
    }
}
