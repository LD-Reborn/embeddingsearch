using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Server;

namespace Indexer.Helper;

public static class SpreadsheetProcessorHelper
{
    public static async Task ExtractFromXlsxAsync(string filePath, string? modelToUse, StringBuilder fullTextBuilder, List<string> pageResults, List<string> imageResults, AIProviderService aiProviderService, ILogger logger)
    {
        using var spreadsheetDoc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = spreadsheetDoc.WorkbookPart ?? throw new InvalidOperationException("Workbook part not found in XLSX file");
        var workbook = workbookPart.Workbook;
        var sheets = workbook?.Sheets ?? throw new InvalidOperationException("No sheets found in workbook");
        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

        bool isFirstSheet = true;

        foreach (var sheet in sheets.Cast<Sheet>())
        {
            if (!isFirstSheet)
                fullTextBuilder.AppendLine(); // Blank line between sheets

            fullTextBuilder.AppendLine($"Sheet: {sheet.Name}");
            var sheetText = new StringBuilder();

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var worksheet = worksheetPart.Worksheet;
            var rows = worksheet.Descendants<Row>();

            foreach (var row in rows)
            {
                var cells = row.Descendants<Cell>().OrderBy(c => c.CellReference?.Value ?? "A1").ToList();
                var cellValues = new List<string>();

                foreach (var cell in cells)
                {
                    string cellValue = GetSpreadsheetCellValue(cell, sharedStringTable);
                    cellValues.Add(cellValue);
                }

                string rowText = string.Join("\t", cellValues);
                sheetText.AppendLine(rowText);
                fullTextBuilder.AppendLine(rowText);
            }

            if (sheetText.Length > 0)
                pageResults.Add(sheetText.ToString().Trim());

            // Extract images from worksheet
            if (!string.IsNullOrWhiteSpace(modelToUse))
            {
                await ExtractImagesFromWorksheetAsync(worksheetPart, modelToUse, imageResults, fullTextBuilder, aiProviderService);
            }

            isFirstSheet = false;
        }
    }

    private static string GetSpreadsheetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.CellValue == null)
            return string.Empty;

        string? cellValue = cell.CellValue.Text;
        
        // Handle shared strings
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(cellValue, out int index) && sharedStringTable != null)
            {
                var sharedStringItem = sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(index);
                if (sharedStringItem != null)
                {
                    var textElements = sharedStringItem.Descendants<Text>();
                    var sb = new StringBuilder();
                    foreach (var text in textElements)
                    {
                        sb.Append(text.Text);
                    }
                    return sb.ToString();
                }
            }
            return string.Empty;
        }

        // Handle booleans
        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return cellValue == "0" ? "FALSE" : "TRUE";
        }

        // Handle numbers and other types
        return cellValue ?? string.Empty;
    }

    private static async Task ExtractImagesFromWorksheetAsync(WorksheetPart worksheetPart, string modelToUse, List<string> imageResults, StringBuilder fullTextBuilder, AIProviderService aiProviderService)
    {
        // Get drawing part associated with this worksheet
        var drawingPart = worksheetPart.DrawingsPart;
        if (drawingPart == null)
            return;

        var imageParts = drawingPart.ImageParts;
        
        foreach (var imagePart in imageParts)
        {
            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var base64 = Convert.ToBase64String(ms.ToArray());
            await OcrProcessorHelper.ProcessImageForOcrAsync(base64, imagePart.Uri.ToString(), modelToUse, imageResults, fullTextBuilder, aiProviderService);
        }
    }

    public static async Task ExtractFromOdsAsync(string filePath, string? modelToUse, StringBuilder fullTextBuilder, List<string> pageResults, List<string> imageResults, AIProviderService aiProviderService, ILogger logger)
    {
        try
        {
            // ODS files are ZIP archives containing XML
            using (var archive = ZipFile.OpenRead(filePath))
            {
                var contentEntry = archive.GetEntry("content.xml") ?? throw new InvalidOperationException("content.xml not found in ODS file");
                
                using (var stream = contentEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    var contentXml = await reader.ReadToEndAsync();
                    var xdoc = XDocument.Parse(contentXml);

                    // ODS namespaces
                    var tableNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:table:1.0");
                    var textNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:text:1.0");
                    var drawNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:drawing:1.0");
                    var xLinkNs = XNamespace.Get("http://www.w3.org/1999/xlink");

                    var tables = xdoc.Descendants(tableNs + "table");
                    bool isFirstSheet = true;

                    foreach (var table in tables)
                    {
                        if (!isFirstSheet)
                            fullTextBuilder.AppendLine(); // Blank line between sheets

                        var sheetName = table.Attribute(tableNs + "name")?.Value ?? "Sheet";
                        fullTextBuilder.AppendLine($"Sheet: {sheetName}");
                        var sheetText = new StringBuilder();

                        var rows = table.Descendants(tableNs + "table-row");

                        foreach (var row in rows)
                        {
                            var cells = row.Descendants(tableNs + "table-cell")
                                .Concat(row.Descendants(tableNs + "covered-table-cell"));
                            var cellValues = new List<string>();

                            foreach (var cell in cells)
                            {
                                string cellValue = ExtractOdsCellValue(cell, textNs);
                                cellValues.Add(cellValue);
                            }

                            string rowText = string.Join("\t", cellValues);
                            sheetText.AppendLine(rowText);
                            fullTextBuilder.AppendLine(rowText);
                        }

                        if (sheetText.Length > 0)
                            pageResults.Add(sheetText.ToString().Trim());

                        isFirstSheet = false;
                    }

                    // Extract images from ODS if vision model is available
                    if (!string.IsNullOrWhiteSpace(modelToUse))
                    {
                        await ExtractImagesFromOdsAsync(archive, modelToUse, imageResults, fullTextBuilder, aiProviderService);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error extracting text from ODS file {FilePath}: {Exception}", filePath, ex.Message);
            throw;
        }
    }

    private static string ExtractOdsCellValue(XElement cell, XNamespace textNs)
    {
        var sb = new StringBuilder();

        // Extract all text from paragraphs and text spans within the cell
        var textElements = cell.Descendants(textNs + "p")
            .SelectMany(p => p.Descendants(textNs + "span"))
            .Union(cell.Descendants(textNs + "span"));

        foreach (var textElement in textElements)
        {
            var textNodes = textElement.Nodes().OfType<XText>();
            foreach (var textNode in textNodes)
            {
                sb.Append(textNode.Value);
            }
        }

        // Also check for direct text content in paragraphs (without span)
        var paragraphs = cell.Descendants(textNs + "p");
        foreach (var para in paragraphs)
        {
            // Get text nodes that are direct children of paragraph (not wrapped in spans)
            foreach (var node in para.Nodes().OfType<XText>())
            {
                sb.Append(node.Value);
            }
        }

        return sb.ToString().Trim();
    }

    private static async Task ExtractImagesFromOdsAsync(ZipArchive archive, string modelToUse, List<string> imageResults, StringBuilder fullTextBuilder, AIProviderService aiProviderService)
    {
        // In ODS, images are typically stored in Pictures/ folder
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
