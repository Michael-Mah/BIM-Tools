using System;
using System.Globalization;
using System.IO;
using Autodesk.Navisworks.Api;

namespace BIM.Tools.Services
{
    internal static class SharingNativeTiming
    {
        private static string Profile(string format)
        { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),EditionBrand.StorageFolder,"SharingPerformance",(typeof(Document).Assembly.GetName().Version.Major+2003)+"-"+format+".csv"); }
        internal static void Record(string format,string metrics)
        {
            if(!File.Exists(metrics)) return;
            try {string file=Profile(format); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file,File.ReadAllText(metrics));} catch(IOException) { }
        }
        internal static bool TryEstimate(string format,double bytes,out double seconds)
        {
            seconds=0;
            try
            {
                string file=Profile(format); if(!File.Exists(file)) return false;
                string[] values=File.ReadAllText(file).Split(',');
                double startup=double.Parse(values[0],CultureInfo.InvariantCulture),convert=double.Parse(values[1],CultureInfo.InvariantCulture),input=double.Parse(values[2],CultureInfo.InvariantCulture);
                if(input<=0 || startup<0 || convert<0) return false;
                seconds=startup+convert*bytes/input;
                return !double.IsNaN(seconds) && !double.IsInfinity(seconds);
            }
            catch(Exception error) { if(error is IOException || error is FormatException || error is IndexOutOfRangeException) return false; throw; }
        }
    }
}
