using NetworkShares;
using Xunit;

public class NamingTests
{
    [Theory]
    [InlineData(@"D:\Projects\Footage", "Footage")]
    [InlineData(@"D:\Projects\Footage\", "Footage")]
    [InlineData(@"D:\", "D")]
    [InlineData(@"C:\Users\me\My Music [2024]", "My Music 2024")]
    [InlineData(@"\\server\share\Sub.", "Sub")]
    [InlineData("", "Share")]
    public void SuggestsAValidShareName(string folder, string expected)
    {
        var name = Naming.SuggestShareName(folder);
        Assert.Equal(expected, name);
        Assert.Null(Naming.ShareNameError(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" lead")]
    [InlineData("a/b")]
    [InlineData("a*b")]
    [InlineData("IPC$")]
    [InlineData("admin$")]
    [InlineData("C$")]
    public void RejectsBadShareNames(string name) => Assert.NotNull(Naming.ShareNameError(name));

    [Fact]
    public void RejectsOverlongShareNames() => Assert.NotNull(Naming.ShareNameError(new string('a', 81)));

    [Theory]
    [InlineData("Footage", "share-footage")]
    [InlineData("My Music 2024", "share-my-music-2024")]
    [InlineData("A very long project name", "share-a-very-long-pr")]
    [InlineData("Ünïcødé", "share-n-c-d")]
    [InlineData("!!!", "share-user")]
    public void SuggestsAValidUserName(string share, string expected)
    {
        var name = Naming.SuggestUserName(share);
        Assert.Equal(expected, name);
        Assert.Null(Naming.UserNameError(name));
        Assert.True(name.Length <= 20);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a@b")]
    [InlineData("a\\b")]
    [InlineData("...")]
    [InlineData("abcdefghijklmnopqrstu")]
    public void RejectsBadUserNames(string name) => Assert.NotNull(Naming.UserNameError(name));
}

public class PasswordTests
{
    [Fact]
    public void GeneratesReadableComplexPasswords()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            var p = Passwords.Generate();
            Assert.Matches("^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$", p);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.DoesNotContain(p, c => "Il1O0o".Contains(c));
            Assert.True(seen.Add(p));
        }
    }
}

public class ModelTests
{
    [Fact]
    public void AclChangeRoundTrips()
    {
        var c = new AclChange("S-1-5-21-1-2-3-1001", 1179817);
        Assert.Equal(c, AclChange.Parse(c.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("S-1-1-0|x")]
    [InlineData("X-1|5")]
    public void AclChangeRejectsGarbage(string s) => Assert.Null(AclChange.Parse(s));

    [Fact]
    public void ComparesPathsLikeWindows()
    {
        var r = new ShareRecord("x", @"D:\Data\", ShareMode.Guest, Access.Read, "share-guest", ["S-1-1-0"], [], false);
        Assert.True(r.SamePath(@"d:\data"));
        Assert.False(r.SamePath(@"D:\Data2"));
        Assert.Equal("Everyone · share-guest", r.Who);
        Assert.Equal("Read only", r.AccessText);
    }
}
