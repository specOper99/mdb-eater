using System.Text;
using MdbConverter.Core.Models;

namespace MdbConverter.Core.IO;

public static class FileNames
{
    public static string ForObject(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "object";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var result = builder.ToString().Trim('.', ' ');
        return string.IsNullOrEmpty(result) ? "object" : result;
    }

    public static string Unique(string directory, string baseName, string extension, HashSet<string> used)
    {
        var candidate = baseName;
        var index = 2;
        while (!used.Add(candidate))
        {
            candidate = baseName + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            index++;
        }

        return Path.Combine(directory, candidate + extension);
    }
}
