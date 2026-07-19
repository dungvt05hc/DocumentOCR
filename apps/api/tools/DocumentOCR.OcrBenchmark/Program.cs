using DocumentOCR.Application.Interfaces;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Processing;
using DocumentOCR.OcrBenchmark;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ── Dev-only tool: compares FakeOcrProvider, AzureDocumentIntelligenceProvider, and the
// optional free/open-source PaddleOcrProvider on the same sample documents. Never referenced by
// the shipped API/WebApi project. See LOCAL_DEVELOPMENT.md > "OCR Benchmark Tool" for usage and
// cost caution before pointing this at a real Azure resource. ─────────────────────────────────

var options = CliOptions.Parse(args);
if (options is null)
    return 1;

if (!Directory.Exists(options.InputDir))
{
    Console.Error.WriteLine($"Input folder not found: {options.InputDir}");
    return 1;
}

var runDir = Path.Combine(options.OutputDir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(runDir);

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<CliOptions>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var azureOptions = configuration.GetSection(AzureOcrOptions.SectionName).Get<AzureOcrOptions>()
    ?? new AzureOcrOptions();
var paddleOptions = configuration.GetSection(PaddleOcrOptions.SectionName).Get<PaddleOcrOptions>()
    ?? new PaddleOcrOptions();

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(c => c.SingleLine = true)
    .SetMinimumLevel(LogLevel.Information));

// Azure model IDs to run, in priority order: --models CLI override > config
// AzureDocumentIntelligence:BenchmarkModelIds > just the single DefaultModelId.
var azureModelIds = options.Models
    ?? (azureOptions.BenchmarkModelIds.Count > 0 ? azureOptions.BenchmarkModelIds : [azureOptions.DefaultModelId]);

// (Provider, output-folder label) pairs — Azure gets one instance per model id so all of them
// can be benchmarked in the same run; the label (not the constant ProviderName) keeps their
// output folders distinct.
var targets = new List<(IDocumentOcrProvider Provider, string Label)> { (new FakeOcrProvider(), "Fake") };
targets.AddRange(azureModelIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(modelId =>
{
    var modelOptions = new AzureOcrOptions
    {
        Endpoint = azureOptions.Endpoint,
        ApiKey = azureOptions.ApiKey,
        DefaultModelId = modelId,
        Features = azureOptions.Features,
        NetworkTimeoutSeconds = azureOptions.NetworkTimeoutSeconds,
        OperationTimeoutSeconds = azureOptions.OperationTimeoutSeconds,
        MaxRetries = azureOptions.MaxRetries,
        RetryDelaySeconds = azureOptions.RetryDelaySeconds
    };
    var provider = new AzureDocumentIntelligenceProvider(
        Options.Create(modelOptions), loggerFactory.CreateLogger<AzureDocumentIntelligenceProvider>());
    return ((IDocumentOcrProvider)provider, $"AzureDocumentIntelligence-{modelId}");
}));

var paddleProvider = new PaddleOcrProvider(
    Options.Create(paddleOptions), loggerFactory.CreateLogger<PaddleOcrProvider>());
targets.Add((paddleProvider, "Paddle"));

if (!azureOptions.IsConfigured)
{
    Console.WriteLine(
        "Note: AzureDocumentIntelligence:Endpoint/ApiKey are not configured (user-secrets or " +
        "AzureDocumentIntelligence__Endpoint / __ApiKey env vars). Azure rows in the summary " +
        "will record a failure — see LOCAL_DEVELOPMENT.md to set credentials.");
}

if (!paddleOptions.IsConfigured)
{
    Console.WriteLine(
        "Note: PaddleOcr:BaseUrl is not configured (user-secrets or PaddleOcr__BaseUrl env var). " +
        "Paddle rows in the summary will record a failure — see LOCAL_DEVELOPMENT.md to run the " +
        "PaddleOCR service locally.");
}

Console.WriteLine(
    "Note: FakeOcrProvider ignores file content and always returns the same deterministic " +
    "result, so its rows will be identical across every input file — it is included for CSV " +
    "shape/format comparison only, not as a meaningful OCR-quality baseline.");

var files = Directory.EnumerateFiles(options.InputDir)
    .Where(f => ContentTypeMapper.TryGetContentType(f) is not null)
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (files.Count == 0)
{
    Console.Error.WriteLine($"No .pdf/.jpg/.jpeg/.png files found in {options.InputDir}");
    return 1;
}

var processor = new BenchmarkFileProcessor(
    new FieldExtractionService(),
    new FieldNormalizationService(),
    new FieldValidationService());

var rows = new List<BenchmarkCsvRow>();

foreach (var filePath in files)
{
    var fileName = Path.GetFileName(filePath);
    var contentType = ContentTypeMapper.TryGetContentType(filePath)!;

    byte[] fileBytes;
    try
    {
        fileBytes = await File.ReadAllBytesAsync(filePath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Skipping {fileName}: failed to read file ({ex.Message})");
        continue;
    }

    // Same documentId for both providers on this file, so their extracted-fields.json /
    // validation-warnings.json outputs can be diffed side by side.
    var documentId = DeterministicDocumentId.ForFileName(fileName);

    foreach (var (provider, label) in targets)
    {
        Console.WriteLine($"Processing {fileName} with {label}...");

        // Grouped by file first, then provider/model label, so all target outputs for the same
        // sample document sit as sibling folders — the natural layout for comparison.
        var fileOutputDir = Path.Combine(runDir, fileName, label);

        var row = await processor.ProcessAsync(
            provider, documentId, fileBytes, fileName, contentType, fileOutputDir, CancellationToken.None);

        rows.Add(row);
    }
}

var csvPath = Path.Combine(runDir, "summary.csv");
await CsvSummaryWriter.WriteAsync(csvPath, rows, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Done. {files.Count} file(s) x {targets.Count} target(s) = {rows.Count} row(s).");
Console.WriteLine($"Output: {runDir}");
Console.WriteLine($"Summary CSV: {csvPath}");

return 0;

/// <summary>Parsed and validated CLI arguments for this tool.</summary>
internal sealed class CliOptions
{
    public required string InputDir { get; init; }
    public required string OutputDir { get; init; }

    /// <summary>
    /// Azure model IDs to benchmark, from --models. Null means "fall back to
    /// AzureDocumentIntelligence:BenchmarkModelIds, then DefaultModelId" (see Program.cs).
    /// </summary>
    public IReadOnlyList<string>? Models { get; init; }

    // Anchored to this source file's own directory (not the invoking shell's working
    // directory) so the default --input/--output always resolve under this tool's project
    // folder, matching the scoped .gitignore entries, regardless of where `dotnet run` is
    // invoked from.
    private static string ToolDir([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetDirectoryName(sourceFile)!;

    public static CliOptions? Parse(string[] args)
    {
        string? input = null;
        string? output = null;
        string? models = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    input = args[++i];
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--models" or "-m" when i + 1 < args.Length:
                    models = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return null;
            }
        }

        return new CliOptions
        {
            InputDir = Path.GetFullPath(input ?? Path.Combine(ToolDir(), "data")),
            OutputDir = Path.GetFullPath(output ?? Path.Combine(ToolDir(), "benchmark-output")),
            Models = models?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: dotnet run --project apps/api/tools/DocumentOCR.OcrBenchmark -- [--input <folder>] [--output <folder>] [--models <ids>]

              --input, -i   Folder containing sample .pdf/.jpg/.jpeg/.png documents.
                            Default: apps/api/tools/DocumentOCR.OcrBenchmark/data
              --output, -o  Folder to write benchmark results into.
                            Default: apps/api/tools/DocumentOCR.OcrBenchmark/benchmark-output
              --models, -m  Comma-separated Azure model IDs to run, e.g. "prebuilt-invoice,prebuilt-layout".
                            Default: AzureDocumentIntelligence:BenchmarkModelIds from config, or just
                            DefaultModelId if that list is empty.

            Both folder defaults are anchored to this tool's own project folder (not the current
            working directory), matching the .gitignore entries that keep sample data and
            benchmark output out of source control. FakeOcrProvider always runs once per file
            regardless of --models.
            """);
    }
}
