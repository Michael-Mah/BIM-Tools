using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("BIM Tools Setup")]
[assembly: AssemblyDescription("Installer for version-specific BIM Tools Navisworks Manage add-ins")]
[assembly: AssemblyCompany("BIM Tools")]
[assembly: AssemblyProduct("BIM Tools")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace BimTools.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private const string SetupVersion = "1.1.1";
        private const string ResourceName = "BIM.Tools.Bundle.zip";
        private readonly Button _installButton;
        private readonly Button _closeButton;
        private readonly Label _status;
        private readonly string _supportedVersions;

        public SetupForm()
        {
            SuspendLayout();
            _supportedVersions = string.Join(" / ", GetSupportedVersions());
            Text = "BIM Tools Setup";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = Color.White;
            ClientSize = new Size(540, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 86;
            header.BackColor = Color.FromArgb(18, 61, 115);

            Label logo = new Label();
            logo.Text = "BIM Tools";
            logo.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            logo.ForeColor = Color.White;
            logo.BackColor = Color.FromArgb(18, 91, 70);
            logo.TextAlign = ContentAlignment.MiddleCenter;
            logo.Location = new Point(24, 20);
            logo.Size = new Size(54, 46);

            Label title = new Label();
            title.Text = "BIM Tools";
            title.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(94, 15);

            Label version = new Label();
            version.Text = "Navisworks Manage " + _supportedVersions + "  ·  Version " + SetupVersion;
            version.Font = new Font("Segoe UI", 9F);
            version.ForeColor = Color.FromArgb(210, 230, 222);
            version.AutoSize = true;
            version.Location = new Point(97, 52);

            header.Controls.Add(version);
            header.Controls.Add(title);
            header.Controls.Add(logo);

            Label heading = new Label();
            heading.Text = "安装 BIM Tools";
            heading.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            heading.ForeColor = Color.FromArgb(28, 36, 34);
            heading.AutoSize = true;
            heading.Location = new Point(28, 112);

            Label details = new Label();
            details.Font = Font;
            details.Text =
                "安装程序会把插件复制到当前 Windows 用户的 Autodesk 插件目录。\r\n" +
                "安装前请关闭 Navisworks Manage。安装完成后重新启动 Navisworks。";
            details.ForeColor = Color.FromArgb(76, 86, 83);
            details.Location = new Point(30, 148);
            details.Size = new Size(480, 56);

            Label locationTitle = new Label();
            locationTitle.Text = "安装位置";
            locationTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            locationTitle.ForeColor = Color.FromArgb(28, 36, 34);
            locationTitle.AutoSize = true;
            locationTitle.Location = new Point(30, 213);

            Label location = new Label();
            location.Text = GetInstallFolder();
            location.Font = new Font("Consolas", 8F);
            location.ForeColor = Color.FromArgb(76, 86, 83);
            location.Location = new Point(30, 235);
            location.Size = new Size(480, 34);

            _status = new Label();
            _status.Font = Font;
            _status.Text = "准备安装";
            _status.ForeColor = Color.FromArgb(76, 86, 83);
            _status.Location = new Point(30, 283);
            _status.Size = new Size(290, 24);
            _status.TextAlign = ContentAlignment.MiddleLeft;

            _installButton = new Button();
            _installButton.Text = "安装";
            _installButton.FlatStyle = FlatStyle.Flat;
            _installButton.FlatAppearance.BorderSize = 0;
            _installButton.BackColor = Color.FromArgb(18, 61, 115);
            _installButton.ForeColor = Color.White;
            _installButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _installButton.Location = new Point(326, 280);
            _installButton.Size = new Size(92, 34);
            _installButton.Click += InstallClicked;

            _closeButton = new Button();
            _closeButton.Font = Font;
            _closeButton.Text = "关闭";
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.FlatAppearance.BorderColor = Color.FromArgb(180, 188, 185);
            _closeButton.BackColor = Color.White;
            _closeButton.ForeColor = Color.FromArgb(28, 36, 34);
            _closeButton.Location = new Point(426, 280);
            _closeButton.Size = new Size(84, 34);
            _closeButton.Click += delegate { Close(); };

            Controls.Add(_closeButton);
            Controls.Add(_installButton);
            Controls.Add(_status);
            Controls.Add(location);
            Controls.Add(locationTitle);
            Controls.Add(details);
            Controls.Add(heading);
            Controls.Add(header);
            ResumeLayout(true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BIM.Tools.UI.BimWindowLayout.FitToWorkingArea(this);
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            if (Process.GetProcessesByName("Roamer").Length > 0)
            {
                MessageBox.Show(
                    this,
                    "请先保存工作并关闭 Navisworks Manage，然后再点击安装。",
                    "Navisworks 正在运行",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _installButton.Enabled = false;
            _closeButton.Enabled = false;
            _status.Text = "正在安装...";
            _status.ForeColor = Color.FromArgb(22, 105, 78);
            Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            try
            {
                InstallBundle(GetInstallFolder());
                _status.Text = "安装完成 · Version " + SetupVersion;
                _status.ForeColor = Color.FromArgb(22, 105, 78);
                MessageBox.Show(
                    this,
                    "BIM Tools 已安装成功。\r\n\r\n" +
                    "现在可以启动 Navisworks Manage " + _supportedVersions +
                    "，打开 BIM Tools 选项卡。",
                    "安装完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                _status.Text = "安装失败";
                _status.ForeColor = Color.FromArgb(180, 45, 45);
                MessageBox.Show(
                    this,
                    "安装未完成：\r\n\r\n" + exception.Message,
                    "安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _installButton.Enabled = true;
                _closeButton.Enabled = true;
            }
        }

        private static void InstallBundle(string installFolder)
        {
            string fullInstallFolder = Path.GetFullPath(installFolder)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(fullInstallFolder);

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(ResourceName))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("安装包中缺少插件资源。");
                }

                using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destination = Path.GetFullPath(
                            Path.Combine(fullInstallFolder, entry.FullName));
                        if (!destination.StartsWith(
                            fullInstallFolder,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("安装包包含无效路径。");
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destination);
                            continue;
                        }

                        string parent = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        using (Stream source = entry.Open())
                        using (FileStream target = new FileStream(
                            destination,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None))
                        {
                            source.CopyTo(target);
                        }
                    }
                }
            }

            string manifest = Path.Combine(fullInstallFolder, "PackageContents.xml");
            if (!File.Exists(manifest))
            {
                throw new InvalidDataException("插件文件校验失败，请重新下载安装包。");
            }
            foreach (string year in GetSupportedVersions())
            {
                string contents = Path.Combine(fullInstallFolder, "Contents", year);
                foreach (string file in new[] {
                    "BIM.Tools.dll", "BimToolsRibbon.xaml", "BimToolsRibbon.name",
                    "Images/BimTools16.png", "Images/BimTools32.png",
                    "Images/BimSectionBox16.png", "Images/BimSectionBox32.png",
                    "Images/BimClash16.png", "Images/BimClash32.png" })
                {
                    if (!File.Exists(Path.Combine(contents, file)))
                    {
                        throw new InvalidDataException("插件文件校验失败：" + year + "/" + file);
                    }
                }
            }
        }

        private static string[] GetSupportedVersions()
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (resource == null) { throw new InvalidDataException("安装包中缺少插件资源。"); }
                using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
                {
                    ZipArchiveEntry entry = archive.GetEntry("PackageContents.xml");
                    if (entry == null) { throw new InvalidDataException("安装包中缺少插件清单。"); }
                    XmlDocument manifest = new XmlDocument();
                    manifest.XmlResolver = null;
                    using (Stream source = entry.Open()) { manifest.Load(source); }
                    XmlNodeList components = manifest.SelectNodes("/ApplicationPackage/Components/ComponentEntry");
                    System.Collections.Generic.List<string> years = new System.Collections.Generic.List<string>();
                    foreach (XmlNode component in components)
                    {
                        string module = component.Attributes["ModuleName"].Value;
                        string[] parts = module.Split('/');
                        int year;
                        if (parts.Length != 4 || parts[0] != "." || parts[1] != "Contents" ||
                            !int.TryParse(parts[2], out year) || year < 2024 || year > 2027 ||
                            parts[3] != "BIM.Tools.dll" || archive.GetEntry(module.Substring(2)) == null)
                        {
                            throw new InvalidDataException("安装包中包含无效或缺失的插件版本。");
                        }
                        years.Add(parts[2]);
                    }
                    if (years.Count == 0) { throw new InvalidDataException("安装包中没有可安装的插件版本。"); }
                    return years.ToArray();
                }
            }
        }

        private static string GetInstallFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "ApplicationPlugins",
                "BIM.Tools.bundle");
        }
    }
}
