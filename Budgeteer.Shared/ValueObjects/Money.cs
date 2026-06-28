using System.Globalization;

namespace Budgeteer.Shared.ValueObjects;

/// <summary>
/// A monetary amount. Replaces bare <see cref="decimal"/>s in the domain so an amount can't be
/// silently confused with any other number, and money-specific operations (sign checks, magnitude,
/// rounding, formatting) have one home. Signed: positive = money in, negative = money out.
/// Currency is tracked at the account level, so a single account's amounts are always comparable.
/// </summary>
/// <remarks>
/// Construction from <see cref="decimal"/> is implicit for ergonomics; extracting the raw decimal
/// is explicit — a deliberate step out of the type — so <c>Money</c> and <c>decimal</c> never
/// interchange by accident. Events and read models persist the raw <see cref="decimal"/> at the
/// serialization boundary and re-wrap on the way back in.
/// </remarks>
public readonly record struct Money(decimal Value) : IComparable<Money>, IFormattable
{
    public static readonly Money Zero = new(0m);

    public bool IsZero => Value == 0m;
    public bool IsPositive => Value > 0m;
    public bool IsNegative => Value < 0m;

    /// <summary>Magnitude (never negative), e.g. to record an expense as a positive figure.</summary>
    public Money Abs() => new(Math.Abs(Value));

    public Money Round(int decimals = 2) => new(Math.Round(Value, decimals));

    public static Money operator +(Money a, Money b) => new(a.Value + b.Value);
    public static Money operator -(Money a, Money b) => new(a.Value - b.Value);
    public static Money operator -(Money a) => new(-a.Value);

    public static bool operator >(Money a, Money b) => a.Value > b.Value;
    public static bool operator <(Money a, Money b) => a.Value < b.Value;
    public static bool operator >=(Money a, Money b) => a.Value >= b.Value;
    public static bool operator <=(Money a, Money b) => a.Value <= b.Value;

    public static implicit operator Money(decimal value) => new(value);
    public static explicit operator decimal(Money money) => money.Value;

    public int CompareTo(Money other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("0.00", CultureInfo.InvariantCulture);
    public string ToString(string? format) => Value.ToString(format, CultureInfo.InvariantCulture);
    public string ToString(string? format, IFormatProvider? provider) => Value.ToString(format, provider);
}
