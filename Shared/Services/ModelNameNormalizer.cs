using System;
using System.IO;

namespace BIM.Tools.Services
{
    public static class ModelNameNormalizer
    {
        public static string NormalizeBasename(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                return string.Empty;
            }

            string candidate = pathOrName.Trim();
            try
            {
                string fileName = Path.GetFileName(candidate);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    candidate = fileName.Trim();
                }
            }
            catch (ArgumentException)
            {
            }

            string extension = string.Empty;
            try
            {
                extension = Path.GetExtension(candidate);
            }
            catch (ArgumentException)
            {
            }

            if (extension.Equals(".nwc", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".nwd", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(0, candidate.Length - extension.Length);
            }

            return candidate.Trim();
        }
    }
}
