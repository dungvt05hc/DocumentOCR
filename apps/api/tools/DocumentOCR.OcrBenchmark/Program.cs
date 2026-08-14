using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using DocumentOCR.Infrastructure.Llm;
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

// Only applicable to PDF input (see ContentTypeMapper) -- prepended per-file below so it always
// runs first for a PDF sample, ahead of the Azure models it's being compared against.
var pdfTextLayerProvider = new PdfTextLayerProvider(
    Options.Create(new PdfTextLayerOptions()), loggerFactory.CreateLogger<PdfTextLayerProvider>());

// Only used by the XML/PDF-pair comparison pass at the end of this file (see PdfXmlPairCsvReader) —
// never added to the main per-file loop below, so a plain (unpaired) benchmark run never spends
// LLM money unless the user explicitly supplies pairs.csv.
var llmOptions = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
var sharedNormalization = new FieldNormalizationService();
var llmStrategy = new PdfTextLayerLlmStrategy(
    pdfTextLayerProvider,
    new GeminiExtractionClient(Options.Create(llmOptions), loggerFactory.CreateLogger<GeminiExtractionClient>()),
    sharedNormalization,
    new NoOpLlmExtractionCache(),
    new NoOpDocumentStorageService(),
    Options.Create(llmOptions),
    Options.Create(new OcrOptions()),
    loggerFactory.CreateLogger<PdfTextLayerLlmStrategy>());

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
    sharedNormalization,
    new FieldValidationService(new DocumentProfileCatalog()));

// Ground truth is optional: comparison columns are populated when present, left blank otherwise.
var groundTruthPath = options.GroundTruthPath ?? Path.Combine(options.InputDir, "ground-truth.csv");
var groundTruth = await GroundTruthCsvReader.LoadAsync(groundTruthPath, CancellationToken.None);
if (groundTruth.Count > 0)
{
    Console.WriteLine($"Loaded {groundTruth.Count} ground-truth row(s) from {groundTruthPath}");
}
else
{
    Console.WriteLine(
        $"No ground truth found at {groundTruthPath} — Expected*/*Matched/FieldAccuracyPercent " +
        "columns will be blank. See docs/product-context.md or pass --ground-truth <file>.");
}

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

    var fileTargets = string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
        ? new List<(IDocumentOcrProvider Provider, string Label)> { (pdfTextLayerProvider, "PdfTextLayer") }.Concat(targets).ToList()
        : targets;

    foreach (var (provider, label) in fileTargets)
    {
        Console.WriteLine($"Processing {fileName} with {label}...");

        // Grouped by file first, then provider/model label, so all target outputs for the same
        // sample document sit as sibling folders — the natural layout for comparison.
        var fileOutputDir = Path.Combine(runDir, fileName, label);

        groundTruth.TryGetValue(fileName, out var fileGroundTruth);

        var row = await processor.ProcessAsync(
            provider, documentId, fileBytes, fileName, contentType, fileOutputDir, CancellationToken.None,
            fileGroundTruth);

        rows.Add(row);
    }
}

var csvPath = Path.Combine(runDir, "summary.csv");
await CsvSummaryWriter.WriteAsync(csvPath, rows, CancellationToken.None);

Console.WriteLine();
// Not a flat files x targets product: PDF samples run one extra target (PdfTextLayer) that
// JPG/PNG samples don't -- see the per-file fileTargets list above.
Console.WriteLine($"Done. {files.Count} file(s), {rows.Count} row(s) total.");
Console.WriteLine($"Output: {runDir}");
Console.WriteLine($"Summary CSV: {csvPath}");

// ── XML/PDF pair comparison: XML parsed with full confidence stands in as ground truth, so
// PdfTextLayerLlmStrategy (and the same Azure/Fake/Paddle targets above) can be scored against a
// verified-correct baseline instead of a hand-typed CSV. See PdfXmlPairCsvReader/XmlGroundTruthBuilder. ─
var pairsPath = options.PairsPath ?? Path.Combine(options.InputDir, "pairs.csv");
var pairs = await PdfXmlPairCsvReader.LoadAsync(pairsPath, CancellationToken.None);

if (pairs.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine(
        $"No XML/PDF pairs found at {pairsPath} -- skipping the pair comparison pass. " +
        "Pass --pairs <file> or add a pairs.csv (columns: XmlFile,PdfFile) inside --input to enable it.");
    return 0;
}

if (!llmOptions.IsConfigured)
{
    Console.WriteLine();
    Console.WriteLine(
        "Note: Llm:Model/Llm:ApiKey are not configured (user-secrets or Llm__Model / __ApiKey env " +
        "vars) -- PdfTextLayerLlm rows in pairs-summary.csv will record a failure. See LOCAL_DEVELOPMENT.md.");
}

Console.WriteLine();
Console.WriteLine($"Loaded {pairs.Count} XML/PDF pair(s) from {pairsPath}");

var xmlParser = new TT78XmlInvoiceParser();
var pairRows = new List<BenchmarkCsvRow>();

foreach (var pair in pairs)
{
    var xmlPath = Path.Combine(options.InputDir, pair.XmlFile);
    var pdfPath = Path.Combine(options.InputDir, pair.PdfFile);

    if (!File.Exists(xmlPath) || !File.Exists(pdfPath))
    {
        Console.Error.WriteLine($"Skipping pair ({pair.XmlFile}, {pair.PdfFile}): one or both files not found under {options.InputDir}");
        continue;
    }

    StructuredExtractionResult xmlResult;
    await using (var xmlStream = File.OpenRead(xmlPath))
    {
        xmlResult = await xmlParser.ParseAsync(xmlStream, CancellationToken.None);
    }

    if (!xmlResult.Success)
    {
        Console.Error.WriteLine($"Skipping pair ({pair.XmlFile}, {pair.PdfFile}): XML did not parse ({xmlResult.ErrorMessage})");
        continue;
    }

    var groundTruthRow = XmlGroundTruthBuilder.FromParsedXmlFields(pair.PdfFile, xmlResult.Fields);
    var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
    var documentId = DeterministicDocumentId.ForFileName(pair.PdfFile);

    Console.WriteLine($"Processing pair {pair.PdfFile} (ground truth: {pair.XmlFile}) with PdfTextLayerLlm...");
    var llmOutputDir = Path.Combine(runDir, "pairs", pair.PdfFile, "PdfTextLayerLlm");
    pairRows.Add(await processor.ProcessStrategyAsync(
        llmStrategy, documentId, pdfBytes, pair.PdfFile, "application/pdf", llmOutputDir, CancellationToken.None, groundTruthRow));

    foreach (var (provider, label) in targets)
    {
        Console.WriteLine($"Processing pair {pair.PdfFile} (ground truth: {pair.XmlFile}) with {label}...");
        var outputDir = Path.Combine(runDir, "pairs", pair.PdfFile, label);
        pairRows.Add(await processor.ProcessAsync(
            provider, documentId, pdfBytes, pair.PdfFile, "application/pdf", outputDir, CancellationToken.None, groundTruthRow));
    }
}

var pairsCsvPath = Path.Combine(runDir, "pairs-summary.csv");
await CsvSummaryWriter.WriteAsync(pairsCsvPath, pairRows, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Done. {pairs.Count} pair(s), {pairRows.Count} row(s) total.");
Console.WriteLine($"Pairs summary CSV: {pairsCsvPath}");

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

    /// <summary>
    /// Path to the ground-truth CSV, from --ground-truth. Null means "look for
    /// ground-truth.csv directly inside --input" (see Program.cs).
    /// </summary>
    public string? GroundTruthPath { get; init; }

    /// <summary>
    /// Path to the XML/PDF pairs CSV, from --pairs. Null means "look for pairs.csv directly
    /// inside --input" (see Program.cs). Missing/empty is fine — that comparison pass is skipped.
    /// </summary>
    public string? PairsPath { get; init; }

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
        string? groundTruth = null;
        string? pairs = null;

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
                case "--ground-truth" or "-g" when i + 1 < args.Length:
                    groundTruth = args[++i];
                    break;
                case "--pairs" or "-p" when i + 1 < args.Length:
                    pairs = args[++i];
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
            Models = models?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            GroundTruthPath = groundTruth is null ? null : Path.GetFullPath(groundTruth),
            PairsPath = pairs is null ? null : Path.GetFullPath(pairs)
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: dotnet run --project apps/api/tools/DocumentOCR.OcrBenchmark -- [--input <folder>] [--output <folder>] [--models <ids>] [--ground-truth <file>] [--pairs <file>]

              --input, -i         Folder containing sample .pdf/.jpg/.jpeg/.png/.xml documents.
                                  Default: apps/api/tools/DocumentOCR.OcrBenchmark/data
              --output, -o        Folder to write benchmark results into.
                                  Default: apps/api/tools/DocumentOCR.OcrBenchmark/benchmark-output
              --ground-truth, -g  Path to a ground-truth CSV (FileName + Expected* columns) to compare
                                  extracted fields against. Default: ground-truth.csv inside --input;
                                  comparison columns are left blank when no ground truth is found.
              --pairs, -p         Path to a pairs CSV (XmlFile,PdfFile columns) naming same-invoice
                                  TT78 XML/PDF pairs. Default: pairs.csv inside --input. When present,
                                  runs an extra pass per pair: parses the XML (full confidence) as
                                  ground truth, then compares PdfTextLayerLlm against it and against
                                  every Azure/Fake/Paddle target above, writing pairs-summary.csv.
                                  Skipped entirely (no error) when no pairs file is found.
              --models, -m        Comma-separated Azure model IDs to run, e.g. "prebuilt-invoice,prebuilt-layout".
                                  Default: AzureDocumentIntelligence:BenchmarkModelIds from config, or just
                                  DefaultModelId if that list is empty.

            Both folder defaults are anchored to this tool's own project folder (not the current
            working directory), matching the .gitignore entries that keep sample data and
            benchmark output out of source control. FakeOcrProvider always runs once per file
            regardless of --models. The --pairs pass calls a real LLM (Llm:Model/Llm:ApiKey via
            user-secrets or env vars) unless left unconfigured, in which case its rows record a
            failure — see LOCAL_DEVELOPMENT.md.
            """);
    }
}
