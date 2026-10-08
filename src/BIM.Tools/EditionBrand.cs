using System.Drawing;
using System;
using System.IO;
namespace BIM.Tools
{
    internal static class EditionBrand
    {
        internal const string ProductName="BIM Tools";
        internal const string SupportMessage="If the issue persists, contact the tool maintainer and include the steps performed and an error screenshot.";
        internal static readonly string SettingsRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BIM-Tools");
        internal const string CompanyName="BIM";
        internal const string StorageFolder="BIM-Tools";
        internal const string ViewpointPrefix="[BIM]";
        internal const string NativeConverterName="BIM.NativeSharingConverter.exe";
        internal const string SharingIconResource="BIM.ModelSharingIcon";
        internal static readonly Color BrandColor=Color.FromArgb(18,61,115);
    }
}
