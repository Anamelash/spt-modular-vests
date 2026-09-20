#nullable disable
using System.IO;

namespace ModularVests.Client.Bones
{
    internal static class AtomicFile
    {
        /// <summary>Writes through a temp file and a replace, so a crash never leaves half a file.</summary>
        public static void WriteAllText(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
