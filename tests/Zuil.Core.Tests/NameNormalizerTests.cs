using Zuil.Core.Text;

namespace Zuil.Core.Tests;

/// <summary>
/// The storage layer normalizes on write and the search endpoint normalizes on
/// read. If these two ever disagree, exact-match lookup silently returns nothing
/// and the kiosk tells every visitor their meeting does not exist — so the rules
/// are pinned here.
/// </summary>
public class NameNormalizerTests
{
    [Theory]
    [InlineData("Jonathan Nap", "jonathan nap")]
    [InlineData("  JONATHAN   NAP  ", "jonathan nap")]
    [InlineData("Jonáthan Näp", "jonathan nap")]
    [InlineData("Jean-Pierre O'Brien", "jean pierre o brien")]
    [InlineData("Sanne de Vries", "sanne de vries")]
    public void Normalizes_to_canonical_form(string input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Fact]
    public void Is_idempotent()
    {
        var once = NameNormalizer.Normalize("Émile Dubois");
        Assert.Equal(once, NameNormalizer.Normalize(once));
    }
}
