using System.IO.Compression;
using System.Text;

namespace PitWall.Core.LiveTiming;

/// <summary>
/// The <c>.z</c> topics carry base64 of raw DEFLATE (no zlib header) wrapping a JSON document.
/// </summary>
public static class Compression
{
    public static string Inflate(string base64)
    {
        var bytes = Convert.FromBase64String(base64.Trim().Trim('"'));
        using var input = new MemoryStream(bytes);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Inverse of <see cref="Inflate"/>. Used by tests and the replay fixtures.</summary>
    public static string Deflate(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(json));
        }
        return Convert.ToBase64String(output.ToArray());
    }
}
