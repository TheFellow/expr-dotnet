using System;
using System.Globalization;
using System.Text;
using System.Threading;
using Expr.Syntax;

namespace Expr.Fuzz;

internal static class Program
{
    private const int DefaultIterations = 100_000;
    private const ulong DefaultSeed = 0x0000000000c0ffeeUL;
    private const int MaximumSourceLength = 1_000;

    // Sampled from inspiration/expr/test/fuzz/fuzz_corpus.txt at
    // 4b31df3a2e0eefec04c017a82a00e0f08541d3e4. Mutation grows this compact seed
    // set indefinitely while keeping failures reproducible by seed and iteration.
    private static readonly string[] Corpus =
    [
        "!!!false",
        "!!(1 <= f64)",
        "!(\"bar\" not contains \"foo\")",
        "map(array, # > 0)",
        "filter(list, .Bar == \"bar\")",
        "all(array, #index >= 0)",
        "foo?.Bar",
        "1 in [1, 2, 3]",
        "let x = 1; x + 2",
        "if true { 1 } else { 2 }",
        "b\"bytes\\x00\\xff\"",
        "sum(1..10, # * 2)",
    ];

    private static readonly UTF8Encoding ReplacementDecoder = new(false, false);
    private static int cancellationRequested;

    private static int Main(string[] args)
    {
        (int iterations, ulong seed) = ParseArguments(args);
        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            Run(iterations, seed);
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private static void Run(int iterations, ulong seed)
    {
        var random = new StableRandom(seed);
        var parserOptions = new SyntaxParserOptions
        {
            MaximumNodeCount = 1_024,
            MaximumParseDepth = 128,
        };
        var printerOptions = new SyntaxPrinterOptions
        {
            MaximumNodeCount = 1_024,
            MaximumDepth = 128,
        };
        var dumperOptions = new SyntaxDumperOptions
        {
            MaximumNodeCount = 1_024,
            MaximumDepth = 128,
        };

        for (var iteration = 0;
             (iterations == 0 || iteration < iterations) && Volatile.Read(ref cancellationRequested) == 0;
             iteration++)
        {
            string source = Mutate(Corpus[random.Next(Corpus.Length)], random);
            try
            {
                var parser = new SyntaxParser();
                if (!parser.TryParse(source, out SyntaxTree? parsedTree, out _, parserOptions))
                {
                    continue;
                }

                SyntaxTree tree = parsedTree ?? throw new InvalidOperationException("Parser returned success without a syntax tree.");
                string canonical = SyntaxPrinter.Print(tree.Root, printerOptions);
                SyntaxNode reparsed = new SyntaxParser().Parse(canonical, parserOptions).Root;
                string originalDump = SyntaxDumper.Dump(tree.Root, dumperOptions);
                string reparsedDump = SyntaxDumper.Dump(reparsed, dumperOptions);
                if (!string.Equals(canonical, SyntaxPrinter.Print(reparsed, printerOptions), StringComparison.Ordinal)
                    || !string.Equals(originalDump, reparsedDump, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"canonical or structural round-trip mismatch\nCanonical: {canonical}\nOriginal:\n{originalDump}\nReparsed:\n{reparsedDump}"));
                }
            }
            catch (Exception exception)
            {
                ReportFailure(seed, iteration, source, exception.ToString());
                throw;
            }
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Completed Expr parser fuzz run (seed=0x{seed:x16}, iterations={(iterations == 0 ? "unbounded" : iterations)})."));
    }

    private static string Mutate(string source, StableRandom random)
    {
        int rounds = 1 + random.Next(8);
        for (var round = 0; round < rounds; round++)
        {
            int position = random.Next(source.Length + 1);
            source = random.Next(9) switch
            {
                0 => source[..position],
                1 => source.Insert(position, random.NextCodeUnit().ToString()),
                2 when source.Length > 0 => source.Remove(Math.Min(position, source.Length - 1), 1),
                3 => source.Insert(position, "/*"),
                4 => source.Insert(position, "*/"),
                5 => source.Insert(position, "()[]{}?:,;"[random.Next(10)].ToString()),
                6 => source + source[..position],
                7 => source.Insert(position, DecodeRandomBytes(random)),
                _ => $"({source})",
            };
            if (source.Length > MaximumSourceLength)
            {
                source = source[..MaximumSourceLength];
            }
        }

        return source;
    }

    private static string DecodeRandomBytes(StableRandom random)
    {
        var bytes = new byte[1 + random.Next(8)];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)random.Next(256);
        }

        return ReplacementDecoder.GetString(bytes);
    }

    private static (int Iterations, ulong Seed) ParseArguments(string[] args)
    {
        int iterations = DefaultIterations;
        ulong seed = DefaultSeed;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--iterations" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                        || iterations < 0)
                    {
                        throw new ArgumentException("--iterations must be a non-negative Int32; zero runs until interrupted.");
                    }

                    break;
                case "--seed" when index + 1 < args.Length:
                    string value = args[++index];
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        value = value[2..];
                    }

                    if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed))
                    {
                        throw new ArgumentException("--seed must be an unsigned hexadecimal UInt64.");
                    }

                    break;
                default:
                    throw new ArgumentException("Usage: Expr.Fuzz [--iterations COUNT] [--seed HEX]; zero iterations runs until interrupted.");
            }
        }

        return (iterations, seed);
    }

    private static void ReportFailure(ulong seed, int iteration, string source, string reason)
    {
        string sourceUtf16 = Convert.ToBase64String(Encoding.Unicode.GetBytes(source));
        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"Fuzz failure at seed 0x{seed:x16}, iteration {iteration}; UTF-16LE base64 source: {sourceUtf16}\n{reason}");
        Console.Error.WriteLine(report);
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        Volatile.Write(ref cancellationRequested, 1);
    }

    private sealed class StableRandom
    {
        private ulong state;

        internal StableRandom(ulong seed) => state = seed == 0 ? 0x9e3779b97f4a7c15UL : seed;

        internal char NextCodeUnit() => Next(8) switch
        {
            0 => (char)(0xd800 + Next(0x400)),
            1 => (char)(0xdc00 + Next(0x400)),
            2 => (char)Next(0x20),
            _ => (char)Next(char.MaxValue + 1),
        };

        internal int Next(int exclusiveMaximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMaximum, 1);
            ulong value = state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            state = value;
            return (int)((value * 0x2545f4914f6cdd1dUL) % (uint)exclusiveMaximum);
        }
    }
}
