using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GaltekClassroom.Bootstrapper
{
    public static class BundleIdentity
    {
        private const string DataFileName = "BootstrapperApplicationData.xml";

        public static string ReadCurrentBundleId(string baseDirectory)
        {
            if (String.IsNullOrWhiteSpace(baseDirectory)) throw new InvalidOperationException("BA_DATA_DIRECTORY_MISSING");
            return ParseCurrentBundleId(XDocument.Load(Path.Combine(baseDirectory, DataFileName), LoadOptions.None));
        }

        public static string ParseCurrentBundleId(string xml)
        {
            if (String.IsNullOrWhiteSpace(xml)) throw new InvalidOperationException("BA_DATA_EMPTY");
            return ParseCurrentBundleId(XDocument.Parse(xml, LoadOptions.None));
        }

        private static string ParseCurrentBundleId(XDocument document)
        {
            var properties = document.Descendants().SingleOrDefault(x => x.Name.LocalName == "WixBundleProperties");
            var raw = properties?.Attribute("Id")?.Value;
            if (!Guid.TryParse(raw, out var parsed)) throw new InvalidOperationException("BA_DATA_BUNDLE_ID_INVALID");
            return parsed.ToString("B").ToUpperInvariant();
        }
    }
}
