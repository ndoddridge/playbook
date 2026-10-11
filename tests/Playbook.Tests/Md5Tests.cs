using System.Security.Cryptography;
using System.Text;
using Playbook.Core.Hashing;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// Player, news and fact ids are derived from MD5. The managed implementation the browser build
/// needs must give exactly the system's bytes, or every stored id would change.
/// </summary>
public class Md5Tests
{
    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("The quick brown fox jumps over the lazy dog", "9e107d9d372bb6826bd81d3542a419d6")]
    public void Matches_Rfc1321_Vectors(string input, string expected)
    {
        Assert.Equal(expected, Convert.ToHexStringLower(Md5.HashData(Encoding.UTF8.GetBytes(input))));
    }

    [Fact]
    public void Matches_System_Md5_Across_Padding_Boundaries()
    {
        var random = new Random(1234);
        for (var length = 0; length <= 300; length++)
        {
            var data = new byte[length];
            random.NextBytes(data);
            Assert.Equal(MD5.HashData(data), Md5.HashData(data));
        }
    }

    [Fact]
    public void Matches_System_Md5_For_Id_Seeds()
    {
        foreach (var seed in new[] { "playbook:sleeper:nfl:4046", "playbook:gsis:nfl:00-0033873", "playbook:espn:news:39123456" })
        {
            var bytes = Encoding.UTF8.GetBytes(seed);
            Assert.Equal(MD5.HashData(bytes), Md5.HashData(bytes));
        }
    }
}
