using MdbConverter.Core.Mapping;

namespace MdbConverter.Core.Tests;

public class AccessTypeMapperTests
{
    [Theory]
    [InlineData(11, "boolean")]
    [InlineData(17, "smallint")]
    [InlineData(2, "integer")]
    [InlineData(3, "bigint")]
    [InlineData(4, "double precision")]
    [InlineData(5, "double precision")]
    [InlineData(6, "numeric")]
    [InlineData(14, "numeric")]
    [InlineData(131, "numeric")]
    [InlineData(7, "timestamp")]
    [InlineData(135, "timestamp")]
    [InlineData(202, "text")]
    [InlineData(203, "text")]
    [InlineData(205, "bytea")]
    [InlineData(72, "uuid")]
    public void Maps_plan_types(int oleDbType, string postgres)
    {
        var mapped = AccessTypeMapper.Map(oleDbType, null, false);
        Assert.Equal(postgres, mapped.PostgresType);
    }

    [Fact]
    public void Unknown_type_becomes_text()
    {
        var mapped = AccessTypeMapper.Map(999, null, false);
        Assert.Equal("text", mapped.PostgresType);
        Assert.Equal("OleDb(999)", mapped.AccessTypeName);
    }

    [Fact]
    public void Counter_is_bigint_identity()
    {
        var mapped = AccessTypeMapper.Map(3, "COUNTER", false);
        Assert.Equal("bigint", mapped.PostgresType);
        Assert.True(mapped.IsAutoIncrement);
    }

    [Fact]
    public void AutoIncrement_long_stays_bigint_identity()
    {
        var mapped = AccessTypeMapper.Map(3, "Long", true);
        Assert.Equal("bigint", mapped.PostgresType);
        Assert.True(mapped.IsAutoIncrement);
    }
}
