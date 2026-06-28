using System.Globalization;
using Budgeteer.Accounts;
using Budgeteer.Shared.ValueObjects;
using Xunit;

namespace Budgeteer.Tests;

public class MoneyTests
{
    [Fact]
    public void Arithmetic_and_sign_behave_like_signed_amounts()
    {
        Money a = 10.50m;   // implicit from decimal
        Money b = 4.00m;

        Assert.Equal(14.50m, (a + b).Value);
        Assert.Equal(6.50m, (a - b).Value);
        Assert.Equal(-10.50m, (-a).Value);

        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(a >= 10.50m);

        Assert.True(((Money)(-5m)).IsNegative);
        Assert.True(((Money)5m).IsPositive);
        Assert.True(Money.Zero.IsZero);
    }

    [Fact]
    public void Abs_returns_magnitude()
    {
        Assert.Equal(12.50m, ((Money)(-12.50m)).Abs().Value);
        Assert.Equal(12.50m, ((Money)12.50m).Abs().Value);
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal((Money)9.99m, (Money)9.99m);
        Assert.NotEqual((Money)9.99m, (Money)10.00m);
    }

    [Fact]
    public void Decimal_conversion_is_explicit_out_implicit_in()
    {
        Money m = 3.14m;              // implicit in
        decimal d = (decimal)m;      // explicit out
        Assert.Equal(3.14m, d);
    }

    [Fact]
    public void Formatting_is_invariant_and_respects_explicit_format()
    {
        Assert.Equal("1234.50", ((Money)1234.50m).ToString());
        Assert.Equal("1,234.50", ((Money)1234.50m).ToString("N2"));
        Assert.Equal("1.234,50", ((Money)1234.50m).ToString("N2", CultureInfo.GetCultureInfo("nl-NL")));
    }
}

public class IbanTests
{
    [Fact]
    public void Equality_ignores_spacing_and_case()
    {
        Assert.Equal(Iban.From("nl11 rabo 0123 4567 89"), Iban.From("NL11RABO0123456789"));
        Assert.NotEqual(Iban.From("NL11RABO0123456789"), Iban.From("NL22INGB0009876543"));
    }

    [Fact]
    public void Normalizes_to_alphanumeric_uppercase()
    {
        Assert.Equal("NL11RABO0123456789", Iban.From(" nl11-rabo 0123.456789 ").ToString());
    }

    [Fact]
    public void Blank_or_null_is_empty()
    {
        Assert.True(Iban.From(null).IsEmpty);
        Assert.True(Iban.From("   ").IsEmpty);
        Assert.True(Iban.Empty.IsEmpty);
        Assert.False(Iban.From("NL11RABO0123456789").IsEmpty);
    }

    [Fact]
    public void ToNullableString_is_null_when_empty_else_normalized()
    {
        Assert.Null(Iban.Empty.ToNullableString());
        Assert.Equal("NL11RABO0123456789", Iban.From("nl11 rabo 0123456789").ToNullableString());
    }
}
