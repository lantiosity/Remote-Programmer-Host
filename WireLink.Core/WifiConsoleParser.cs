using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WireLink.Core;

// Console echo and unrelated logs never leave this bounded line decoder.
public sealed class WifiConsoleParser
{
    private readonly StringBuilder line = new();
    private bool oversized;
    public JsonObject? Feed(string text, string id)
    {
        JsonObject? result = null;
        foreach (var c in text)
        {
            if (c is '\r' or '\n')
            {
                if (!oversized && result == null)
                {
                    var value = line.ToString();
                    var prefix = value.IndexOf("WLWIFI {", StringComparison.Ordinal);
                    if (prefix >= 0)
                    {
                        try { result = WifiSetupProtocol.ParseResponse(value[(prefix + 7)..].Trim(), id); }
                        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
                    }
                }
                line.Clear(); oversized = false;
            }
            else if (!oversized)
            {
                if (line.Length < 8192) line.Append(c);
                else { line.Clear(); oversized = true; }
            }
        }
        return result;
    }
}
