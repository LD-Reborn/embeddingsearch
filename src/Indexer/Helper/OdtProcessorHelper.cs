using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using Server;

namespace Indexer.Helper;

public static class OdtProcessorHelper
{
    public static async Task ExtractFromOdtAsync(string filePath, string? modelToUse, StringBuilder fullTextBuilder, List<string> pageResults, List<string> imageResults, List<string> textBoxResults, List<string> tableResults, List<string> commentResults, List<string> headerResults, List<string> footerResults, AIProviderService aiProviderService, ILogger logger)
    {
        try
        {
            // ODT files are ZIP archives containing XML
            using var archive = ZipFile.OpenRead(filePath);
            var contentEntry = archive.GetEntry("content.xml") ?? throw new InvalidOperationException("content.xml not found in ODT file");

            using var stream = contentEntry.Open();
            using var reader = new StreamReader(stream);
            var contentXml = await reader.ReadToEndAsync();
            var xdoc = XDocument.Parse(contentXml);

            // ODT namespaces
            var textNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:text:1.0");
            var tableNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:table:1.0");
            var drawNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:drawing:1.0");
            var xLinkNs = XNamespace.Get("http://www.w3.org/1999/xlink");
            var styleNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:style:1.0");
            var officeNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:office:1.0");

            // Extract headers and footers from styles.xml
            var stylesEntry = archive.GetEntry("styles.xml");
            if (stylesEntry != null)
            {
                using var stylesStream = stylesEntry.Open();
                using var stylesReader = new StreamReader(stylesStream);
                var stylesXml = await stylesReader.ReadToEndAsync();
                var stylesDoc = XDocument.Parse(stylesXml);

                var headers = stylesDoc.Descendants(styleNs + "header");
                foreach (var header in headers)
                {
                    var headerText = ExtractOdtText(header, textNs);
                    if (!string.IsNullOrWhiteSpace(headerText))
                    {
                        fullTextBuilder.AppendLine("[HEADER]");
                        fullTextBuilder.AppendLine(headerText);
                        headerResults.Add(headerText);
                    }
                }

                var footers = stylesDoc.Descendants(styleNs + "footer");
                foreach (var footer in footers)
                {
                    var footerText = ExtractOdtText(footer, textNs);
                    if (!string.IsNullOrWhiteSpace(footerText))
                    {
                        fullTextBuilder.AppendLine("[FOOTER]");
                        fullTextBuilder.AppendLine(footerText);
                        footerResults.Add(footerText);
                    }
                }
            }

            // Extract text content
            var textSections = xdoc.Descendants(textNs + "p");
            bool isFirstSection = true;

            foreach (var section in textSections)
            {
                if (!isFirstSection)
                    fullTextBuilder.AppendLine();

                var sectionText = ExtractOdtText(section, textNs);
                fullTextBuilder.Append(sectionText);

                if (!string.IsNullOrWhiteSpace(sectionText))
                    pageResults.Add(sectionText);

                isFirstSection = false;
            }

            // Extract text boxes
            var textBoxes = xdoc.Descendants(drawNs + "custom-shape");
            foreach (var textBox in textBoxes)
            {
                var textBoxText = ExtractOdtText(textBox, drawNs);
                if (!string.IsNullOrWhiteSpace(textBoxText))
                {
                    fullTextBuilder.AppendLine("[TEXT BOX]");
                    fullTextBuilder.AppendLine(textBoxText);
                    textBoxResults.Add(textBoxText);
                }
            }

            // Extract tables
            var tables = xdoc.Descendants(tableNs + "table");
            foreach (var table in tables)
            {
                var tableText = ExtractOdtTable(table, tableNs, textNs);
                if (!string.IsNullOrWhiteSpace(tableText))
                {
                    fullTextBuilder.AppendLine("[TABLE]");
                    fullTextBuilder.AppendLine(tableText);
                    tableResults.Add(tableText);
                }
            }

            // Extract comments
            var comments = xdoc.Descendants(officeNs + "annotation");
            foreach (var comment in comments)
            {
                var commentText = ExtractOdtText(comment, textNs);
                if (!string.IsNullOrWhiteSpace(commentText))
                {
                    fullTextBuilder.AppendLine("[COMMENT]");
                    fullTextBuilder.AppendLine(commentText);
                    commentResults.Add(commentText);
                }
            }

            // Extract images from ODT if vision model is available
            if (!string.IsNullOrWhiteSpace(modelToUse))
            {
                await ExtractImagesFromOdtAsync(archive, modelToUse, imageResults, fullTextBuilder, aiProviderService);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error extracting text from ODT file {FilePath}: {Exception}", filePath, ex.Message);
            throw;
        }
    }

    private static string ExtractOdtText(XElement element, XNamespace textNs)
    {
        var sb = new StringBuilder();

        // Extract text from spans (styled text)
        var spans = element.Descendants(textNs + "span");
        if (spans.Any())
        {
            foreach (var span in spans)
            {
                var spanText = ExtractOdtText(span, textNs);
                if (!string.IsNullOrWhiteSpace(spanText))
                    sb.Append(spanText);
            }
        } else
        {
            // Extract direct text content
            var textNodes = element.Nodes();
            foreach (var textNode in textNodes)
            {
                sb.Append(((dynamic)textNode).Value);
            }
        }


        // Handle line breaks
        var lineBreaks = element.Descendants(textNs + "s"); // space elements
        foreach (var lb in lineBreaks)
        {
            sb.Append(' ');
        }

        var lineBreakBr = element.Descendants(textNs + "line-break");
        foreach (var lb in lineBreakBr)
        {
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ExtractOdtTable(XElement table, XNamespace tableNs, XNamespace textNs)
    {
        var sb = new StringBuilder();
        
        // Extract table data
        var tableRows = table.Descendants(tableNs + "table-row");
        foreach (var row in tableRows)
        {
            var tableCells = row.Descendants(tableNs + "table-cell");
            foreach (var cell in tableCells)
            {
                var cellText = ExtractOdtText(cell, textNs);
                if (!string.IsNullOrWhiteSpace(cellText))
                {
                    sb.Append(cellText);
                }
                sb.Append('\t'); // Tab separator between cells
            }
            sb.AppendLine(); // New line for each row
        }
        
        return sb.ToString();
    }

    private static async Task ExtractImagesFromOdtAsync(ZipArchive archive, string modelToUse, List<string> imageResults, StringBuilder fullTextBuilder, AIProviderService aiProviderService)
    {
        // In ODT, images are typically stored inPictures/ folder
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
        
        var imageEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("Pictures/", StringComparison.OrdinalIgnoreCase) && 
                       imageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .ToList();

        foreach (var imageEntry in imageEntries)
        {
            try
            {
                using (var stream = imageEntry.Open())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    var base64 = Convert.ToBase64String(ms.ToArray());
                    await OcrProcessorHelper.ProcessImageForOcrAsync(base64, imageEntry.FullName, modelToUse, imageResults, fullTextBuilder, aiProviderService);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail on individual image extraction errors
                fullTextBuilder.AppendLine($"[IMAGE EXTRACTION ERROR: {imageEntry.FullName} - {ex.Message}]");
            }
        }
    }
}
