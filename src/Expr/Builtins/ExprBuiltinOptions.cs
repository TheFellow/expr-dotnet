using System;

namespace Expr.Builtins;

/// <summary>Configures deterministic services and resource limits used by the standard built-ins.</summary>
public sealed record ExprBuiltinOptions
{
    /// <summary>Gets the default options.</summary>
    public static ExprBuiltinOptions Default { get; } = new();

    /// <summary>Gets the clock used by <c>now</c>.</summary>
    public TimeProvider TimeProvider
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = TimeProvider.System;

    /// <summary>Gets the timezone used when a date has no offset and no explicit timezone.</summary>
    public TimeZoneInfo TimeZone
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = TimeZoneInfo.Utc;

    /// <summary>Gets the maximum supported nested collection or JSON depth.</summary>
    public int MaximumDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 10_000;

    /// <summary>Gets the maximum number of elements or output bytes allocated by one built-in.</summary>
    public int MaximumAllocation
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000_000;
}
