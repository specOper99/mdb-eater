using MdbConverter.Core.Postgres;

namespace MdbConverter.Core.Tests;

public class PostgresIdentifierTests
{
    [Fact]
    public void Quotes_spaces()
    {
        Assert.Equal("\"My Table\"", PostgresIdentifier.Quote("My Table"));
    }

    [Fact]
    public void Doubles_embedded_quotes()
    {
        Assert.Equal("\"a\"\"b\"", PostgresIdentifier.Quote("a\"b"));
    }

    [Fact]
    public void Preserves_unicode()
    {
        Assert.Equal("\"اسم\"", PostgresIdentifier.Quote("اسم"));
    }

    [Fact]
    public void String_literals_escape_quotes()
    {
        Assert.Equal("'O''Brien'", PostgresIdentifier.Literal("O'Brien"));
    }
}
