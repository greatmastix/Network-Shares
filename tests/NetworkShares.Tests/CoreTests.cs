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
    static readonly Grant Mike = new(ShareMode.ExistingUser, "mike", ["S-1-5-21-1-2-3-1001"], Access.ReadWrite);
    static readonly Grant Anna = new(ShareMode.NewUser, "share-anna", ["S-1-5-21-1-2-3-1002"], Access.Read);
    static readonly Grant Anon = new(ShareMode.Anonymous, "Everyone", ["S-1-1-0", "S-1-5-7"], Access.Read);
    static readonly Grant Guest = new(ShareMode.Guest, "share-guest", ["S-1-5-21-1-2-3-1003"], Access.Read);

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
    public void GrantRoundTrips()
    {
        foreach (var g in new[] { Mike, Anna, Anon, Guest })
        {
            var back = Grant.Parse(g.ToString());
            Assert.NotNull(back);
            Assert.Equal(g.Kind, back.Kind);
            Assert.Equal(g.Access, back.Access);
            Assert.Equal(g.Account, back.Account);
            Assert.Equal(g.Sids, back.Sids);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0|0|mike")]
    [InlineData("9|0|mike|S-1-1-0")]
    [InlineData("0|0|mike|")]
    [InlineData("0|0|mike|nope")]
    public void GrantRejectsGarbage(string s) => Assert.Null(Grant.Parse(s));

    [Fact]
    public void ComparesPathsLikeWindows()
    {
        var r = new ShareRecord("x", @"D:\Data\", [Guest], [], false);
        Assert.True(r.SamePath(@"d:\data"));
        Assert.False(r.SamePath(@"D:\Data2"));
        Assert.Equal("Everyone (share-guest)  ·  Read only", r.Who);
    }

    [Fact]
    public void DescribesSeveralPeople()
    {
        Assert.Equal("mike, share-anna", new ShareRecord("x", "D:", [Mike, Anna], [], false).Who);
        Assert.Equal("mike and 2 more", new ShareRecord("x", "D:", [Mike, Anna, Guest], [], false).Who);
    }
}

public class PlanningTests
{
    static Grant User(string name, string sid, Access a = Access.Read) => new(ShareMode.ExistingUser, name, [sid], a);

    [Fact]
    public void FolderAccessTakesTheHighest()
    {
        var d = Planning.FolderAccess([User("a", "S-1"), new Grant(ShareMode.Anonymous, "Everyone", ["S-1-1-0", "S-1"], Access.ReadWrite), User("b", "S-2")]);
        Assert.Equal(Access.ReadWrite, d["S-1"]);
        Assert.Equal(Access.ReadWrite, d["S-1-1-0"]);
        Assert.Equal(Access.Read, d["S-2"]);
    }

    [Fact]
    public void ValidatesGrants()
    {
        Assert.NotNull(Planning.GrantsError([]));
        Assert.NotNull(Planning.GrantsError([User("a", "S-1"), User("A", "S-1")]));
        Assert.NotNull(Planning.GrantsError([
            new Grant(ShareMode.Guest, "share-guest", ["S-9"], Access.Read),
            new Grant(ShareMode.Anonymous, "Everyone", ["S-1-1-0"], Access.Read)]));
        Assert.Null(Planning.GrantsError([User("a", "S-1"), User("b", "S-2")]));
    }

    [Fact]
    public void OffersOnlyUsersNoOtherShareNeeds()
    {
        var anna = new CreatedUser("share-anna", "S-2", false);
        var bob = new CreatedUser("share-bob", "S-3", false);
        var shares = new[]
        {
            new ShareRecord("one", "D:", [User("anna", "S-2"), User("bob", "S-3")], [], false),
            new ShareRecord("two", "E:", [User("bob", "S-3")], [], false),
        };
        var orphans = Planning.OrphanedUsers(shares, [anna, bob], "one", ["S-2", "S-3"]);
        Assert.Equal([anna], orphans);
    }
}

public class WireTests
{
    // The window and the elevated helper exchange these as JSON: they must survive the trip.
    static T RoundTrip<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;

    [Fact]
    public void SharesSurviveJson()
    {
        var record = new ShareRecord("Footage", @"D:\Footage",
            [new Grant(ShareMode.NewUser, "share-footage", ["S-1-5-21-1-2-3-1001"], Access.ReadWrite)],
            [new AclChange("S-1-5-21-1-2-3-1001", 0)], true);
        var result = RoundTrip(new ShareResult(record, [new Login("share-footage", "Abcd-Efgh-2345", false)], ["careful"]));
        Assert.Equal(record.Name, result.Record.Name);
        Assert.Equal(record.Path, result.Record.Path);
        Assert.True(result.Record.NullSession);
        Assert.Equal(record.Grants[0].Sids, result.Record.Grants[0].Sids);
        Assert.Equal(ShareMode.NewUser, result.Record.Grants[0].Kind);
        Assert.Equal(record.Acl, result.Record.Acl);
        Assert.Equal("Abcd-Efgh-2345", result.Logins[0].Password);
        Assert.Equal(["careful"], result.Warnings);
    }

    [Fact]
    public void RequestsAndStateSurviveJson()
    {
        var update = RoundTrip(new UpdateSpec("old", "new", [new GrantSpec(ShareMode.ExistingUser, "mike", null, Access.Read, "S-1-5-21-1")], ["share-x"]));
        Assert.Equal("S-1-5-21-1", update.Grants[0].Sid);
        Assert.Equal(["share-x"], update.DeleteUsers);

        var health = RoundTrip(new Health(true, false, true));
        Assert.False(health.Ok);
        Assert.True(health.HasFixes);

        var snap = RoundTrip(new Snapshot([], ["gone"], [new CreatedUser("share-guest", "S-1-5-21-9", true)], false, "share-guest", "pw", ["mike"]));
        Assert.Equal(["gone"], snap.Missing);
        Assert.True(snap.Users[0].Guest);
        Assert.Equal("pw", snap.GuestPassword);
        Assert.Equal(["mike"], snap.LocalUsers);
    }
}
