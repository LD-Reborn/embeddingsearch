using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Server;

namespace Indexer.Services;

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

            string ocrPrompt = "Please extract and return all the text visible in this image.";

            var result = aiProviderService.GenerateResponse(
                modelToUse,
                ocrPrompt,
                [base64],
                think: false,
                system: "You are a OCR tool that extracts text from images."
            );

            imageResults.Add(result);
            fullTextBuilder.AppendLine($"[IMAGE: {imagePart.Uri}]");
            fullTextBuilder.AppendLine(result);
        }
    }
}
