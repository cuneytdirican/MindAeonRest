using MindAeonRest.Helpers;
using Xunit;

namespace MindAeonRest.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesValueDifferentFromPlaintext()
    {
        const string pw = "Sifrem123!";
        string hash = PasswordHasher.Hash(pw);

        Assert.NotNull(hash);
        Assert.NotEqual(pw, hash);
        Assert.DoesNotContain(pw, hash);
    }

    [Fact]
    public void Hash_HasThreePartFormat_IterationsSaltHash()
    {
        string hash = PasswordHasher.Hash("abc");
        var parts = hash.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.True(int.TryParse(parts[0], out int iterations));
        Assert.True(iterations > 0);
        // salt ve hash geçerli base64 olmalı
        Assert.NotEmpty(Convert.FromBase64String(parts[1]));
        Assert.NotEmpty(Convert.FromBase64String(parts[2]));
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        const string pw = "P@ssw0rd-Correct";
        string hash = PasswordHasher.Hash(pw);

        Assert.True(PasswordHasher.Verify(pw, hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        string hash = PasswordHasher.Hash("dogru-parola");

        Assert.False(PasswordHasher.Verify("yanlis-parola", hash));
    }

    [Fact]
    public void Hash_UsesRandomSalt_SameInputProducesDifferentHash()
    {
        const string pw = "aynı-parola";
        string h1 = PasswordHasher.Hash(pw);
        string h2 = PasswordHasher.Hash(pw);

        Assert.NotEqual(h1, h2); // farklı salt → farklı çıktı

        // farklı salt'a rağmen ikisi de aynı parolayı doğrulamalı
        Assert.True(PasswordHasher.Verify(pw, h1));
        Assert.True(PasswordHasher.Verify(pw, h2));
    }

    [Fact]
    public void Hash_EmptyPassword_CanBeVerified()
    {
        string hash = PasswordHasher.Hash(string.Empty);

        Assert.True(PasswordHasher.Verify(string.Empty, hash));
        Assert.False(PasswordHasher.Verify("non-empty", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_ReturnsFalse_ForNullOrWhitespaceStored(string? stored)
    {
        Assert.False(PasswordHasher.Verify("herhangi", stored!));
    }

    [Theory]
    [InlineData("not-a-hash")]               // ayraç yok
    [InlineData("100000.onlytwo")]           // 2 parça
    [InlineData("notint.c2FsdA==.aGFzaA==")] // iterations sayı değil
    public void Verify_ReturnsFalse_ForMalformedStored(string stored)
    {
        Assert.False(PasswordHasher.Verify("herhangi", stored));
    }
}
