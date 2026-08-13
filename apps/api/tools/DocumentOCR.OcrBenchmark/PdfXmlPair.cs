namespace DocumentOCR.OcrBenchmark;

/// <summary>One row of <c>pairs.csv</c>: the same invoice available as both a TT78 XML e-invoice and its printed PDF.</summary>
public sealed record PdfXmlPair(string XmlFile, string PdfFile);
