using System.Security.Cryptography;

namespace NetworkShares;

/// <summary>Share and user names Windows accepts, and suggestions for them.</summary>
public static class Naming
{
    const string BadShareChars = "\"/\\[]:|<>+=;,?*";
    const string BadUserChars = "\"/\\[]:;|=,+*?<>@";
    static readonly string[] Reserved = ["IPC$", "ADMIN$", "PRINT$"];

    /// <summary>The folder's own name, made valid: "D:\Projects\Footage" → "Footage", a drive root "D:\" → "D".</summary>
    public static string SuggestShareName(string folder)
    {
        var trimmed = folder.Trim().TrimEnd('\\', '/');
        var last = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        var clean = new string(last.Where(c => !BadShareChars.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        if (clean.Length > 80) clean = clean[..80].TrimEnd();
        return clean.Length == 0 ? "Share" : clean;
    }

    public static string? ShareNameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a share name.";
        if (name.Length > 80) return "Share names can be at most 80 characters.";
        if (name != name.Trim()) return "A share name can't start or end with a space.";
        if (name.Any(c => BadShareChars.Contains(c) || char.IsControl(c))) return $"A share name can't contain any of  {BadShareChars}";
        if (Reserved.Contains(name, StringComparer.OrdinalIgnoreCase) || (name.Length == 2 && char.IsLetter(name[0]) && name[1] == '$'))
            return "That name is reserved by Windows.";
        return null;
    }

    /// <summary>"Footage" → "share-footage": lower case letters, digits and dashes, at most 20 characters.</summary>
    public static string SuggestUserName(string shareName)
    {
        var chars = shareName.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        var core = string.Join('-', new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
        var name = "share-" + core;
        if (name.Length > 20) name = name[..20];
        return name.TrimEnd('-') is { Length: > 6 } n ? n : "share-user";
    }

    public static string? UserNameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a user name.";
        if (name.Length > 20) return "User names can be at most 20 characters.";
        if (name.Any(c => BadUserChars.Contains(c) || char.IsControl(c))) return $"A user name can't contain any of  {BadUserChars}";
        if (name.All(c => c is '.' or ' ')) return "A user name can't be only dots and spaces.";
        return null;
    }
}

public static class Passwords
{
    // No look-alikes (I/l/1, O/0), so a password can be read out or typed from a screen.
    const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", Lower = "abcdefghijkmnpqrstuvwxyz", Digits = "23456789";

    /// <summary>Three groups of four, e.g. "Hq7m-Tz4c-wP9e": 12 random characters with upper case, lower case and digits
    /// (and the dashes), which meets the default Windows complexity rules.</summary>
    public static string Generate()
    {
        const string all = Upper + Lower + Digits;
        var c = new char[12];
        c[0] = Pick(Upper);
        c[1] = Pick(Lower);
        c[2] = Pick(Digits);
        for (int i = 3; i < c.Length; i++) c[i] = Pick(all);
        for (int i = c.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (c[i], c[j]) = (c[j], c[i]);
        }
        var s = new string(c);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
