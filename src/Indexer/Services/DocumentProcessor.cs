using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Indexer.Models;
using Server;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office.CustomUI;
using Quartz.Util;
using Indexer.Helper;
using iText.Kernel.Pdf;

namespace Indexer.Services;

public class DocumentProcessor
{
    private readonly ILogger _logger;
    private readonly AIProviderService _aIProviderService;
    private readonly string? _defaultVisionModel;
    private readonly PdfProcessorHelper _pdfProcessorHelper;
    private readonly Dictionary<string, Func<DocumentProcessingRequest, Task<IDocumentProcessingResultModel>>> _extractors;
    public DocumentProcessor(ILogger logger, AIProviderService aIProviderService, string? defaultVisionModel = null)
    {
        _logger = logger;
        _aIProviderService = aIProviderService;
        _defaultVisionModel = defaultVisionModel;
        _pdfProcessorHelper = new PdfProcessorHelper(logger, aIProviderService, defaultVisionModel);
        List<(Func<DocumentProcessingRequest, Task<IDocumentProcessingResultModel>> Handler, List<string> Extensions)> extractorGroups = new List<(Func<DocumentProcessingRequest, Task<IDocumentProcessingResultModel>> Handler, List<string> Extensions)>
        {
            // Text-based documents (simple text)
            (ExtractTextFromTextfileAsync, new() { ".txt", ".json" }),

            // PDF
            (ExtractTextFromPdfAsync, new() { ".pdf" }),

            // Word documents
            (ExtractTextFromWordDocumentAsync, new() { ".docx", ".odt" }),

            // Spreadsheets (not yet implemented)
            (ExtractTextFromSpreadsheetAsync, new() { ".ods", ".xls", ".xlsx" }),

            // Presentations (not yet implemented)
            (ExtractTextFromPresentationAsync, new() { ".odp", ".ppt", ".pptx" }),

            // Images (OCR via vision model)
            (ExtractTextFromImageAsync, new() { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" })
        };

        _extractors = extractorGroups
            .SelectMany(g => g.Extensions.Select(ext => (Key: ext, Value: g.Handler)))
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase
            );
    }

    public async Task<IDocumentProcessingResultModel> GetTextContentAsync(string filePath)
    {
        return await GetTextContentAsync(new DocumentProcessingRequest(filePath, _defaultVisionModel));
    }

    public async Task<IDocumentProcessingResultModel> GetTextContentAsync(string filePath, string? visionModel)
    {
        return await GetTextContentAsync(new DocumentProcessingRequest(filePath, visionModel));
    }

    public async Task<IDocumentProcessingResultModel> GetTextContentAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        var extension = System.IO.Path.GetExtension(filePath);
        Func<DocumentProcessingRequest, Task<IDocumentProcessingResultModel>> extractor;
        try
        {
            extractor = _extractors.First(x => extension.Equals(x.Key)).Value;
        } catch (Exception)
        {
            throw new Exception($"Unable to retrieve extractor for extension: {extension}");
        }
        
        return await GetTextContentAsync(documentProcessingRequest, extractor);
    }

    public async Task<IDocumentProcessingResultModel> GetTextContentAsync(DocumentProcessingRequest documentProcessingRequest, Func<DocumentProcessingRequest, Task<IDocumentProcessingResultModel>> extractor)
    {
        string filePath = documentProcessingRequest.filePath;
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }
        
        return await extractor(documentProcessingRequest);
    }

    public async Task<IDocumentProcessingResultModel> ExtractTextFromTextfileAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        try
        {
            var fileContent = await File.ReadAllTextAsync(filePath);
            return new DocumentProcessingTextResultModel(fileContent);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from text file: {FilePath}, {Exception}", filePath, ex.Message);
            throw;
        }
    }

    public async Task<IDocumentProcessingResultModel> ExtractTextFromPdfAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        string? visionModel = documentProcessingRequest.visionModel;
        string? modelToUse = visionModel ?? _defaultVisionModel;
        
        try
        {
            _logger.LogInformation("Extracting text from PDF: {FilePath}", filePath);

            var textBuilder = new StringBuilder();
            var pageResults = new List<string>();
            var imageResults = new List<string>();

            using (var reader = new PdfReader(filePath))
            using (var pdfDoc = new PdfDocument(reader))
            {
                var pdfTextExtractor = new iText.Kernel.Pdf.Canvas.Parser.Listener.LocationTextExtractionStrategy();

                // Track processed image XObject names to avoid duplicates across pages
                var processedImageNames = new HashSet<string>();
                var pageImageResults = new Dictionary<int, List<string>>();

                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(
                        pdfDoc.GetPage(i), 
                        pdfTextExtractor);

                    // Approximate paragraphs (split by double newline or line breaks)
                    var paragraphs = Regex.Split(pageText, @"\r?\n\r?\n+")
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

                    pageResults.AddRange(string.Join('\n', paragraphs));
                    textBuilder.AppendLine(pageText);
                    textBuilder.AppendLine("--- Page " + i + " ---");

                    // Extract images from the current page
                    if (!string.IsNullOrWhiteSpace(modelToUse))
                    {
                        var pageImages = await _pdfProcessorHelper.ExtractImagesFromPdfPageAsync(pdfDoc.GetPage(i), i, modelToUse, processedImageNames);
                        pageImageResults[i] = pageImages;
                        imageResults.AddRange(pageImages);
                        foreach (var imageText in pageImages)
                        {
                            textBuilder.AppendLine($"[IMAGE PAGE {i}]").AppendLine(imageText);
                        }
                    }
                }
            }

            return new DocumentProcessingPdfResultModel(
                textBuilder.ToString(),
                pageResults,
                imageResults
            );
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from PDF file {FilePath}: {Exception}", filePath, ex);
            throw;
        }
    }

    public async Task<IDocumentProcessingResultModel> ExtractTextFromWordDocumentAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        List<string> paragraphResults = [];
        List<string> headerResults = [];
        List<string> footerResults = [];
        List<string> textBoxResults = [];
        List<string> tableResults = [];
        List<string> commentResults = [];
        List<string> imageResults = [];
        WordprocessingCommentsPart? commentsPart;
        try
        {
            _logger.LogInformation("Extracting text from word document: {FilePath}", filePath);

            using var package = WordprocessingDocument.Open(filePath, false) ?? throw new Exception("package is null");
            MainDocumentPart? mainDocumentPart = package.MainDocumentPart;
            var fullTextTextBuilder = new StringBuilder();

            if (mainDocumentPart is null)
            {
                _logger.LogWarning("MainDocumentPart is null for {FilePath}. Attempting fallback extraction from parts.", filePath);
                IEnumerable<OpenXmlPart> parts = package.GetAllParts();

                // Fallback: Scan all parts for text (headers, footers, comments, etc.)
                foreach (IdPartPair idPartPair in package.Parts)
                {
                    OpenXmlPart part = idPartPair.OpenXmlPart;
                    if (part is HeaderPart headerPart)
                    {
                        var headerText = ExtractTextFromPart(headerPart);
                        headerResults.Add(headerText);
                        fullTextTextBuilder.AppendLine($"[HEADER: {headerPart.Uri}]").AppendLine(headerText);
                    }
                    else if (part is FooterPart footerPart)
                    {
                        var footerText = ExtractTextFromPart(footerPart);
                        footerResults.Add(footerText);
                        fullTextTextBuilder.AppendLine($"[FOOTER: {footerPart.Uri}]").AppendLine(footerText);
                    }
                }

                // Try to read comments
                commentsPart = package.MainDocumentPart?.WordprocessingCommentsPart;
                if (commentsPart?.Comments != null)
                {
                    foreach (var comment in commentsPart.Comments.Elements<Comment>())
                    {
                        var commentText = ExtractTextFromElement(comment);
                        commentResults.Add(commentText);
                        fullTextTextBuilder.AppendLine($"[COMMENT: {comment.Id?.Value}] {commentText}");
                    }
                }

                if (fullTextTextBuilder.Length == 0)
                    throw new InvalidDataException($"No text found in DOCX. File may be empty or malformed: {filePath}");

                return new DocumentProcessingWordDocumentResultModel(
                    fullTextTextBuilder.ToString(),
                    paragraphResults, headerResults, footerResults,
                    textBoxResults, tableResults, commentResults, imageResults);
            }
            if (mainDocumentPart.Document is null) throw new Exception("mainDocumentPart.Document is null");

            package.MainDocumentPart?.ExtractTextFromParts(p => p.HeaderParts, str => {
                if (str != "\n") headerResults.Add(str);
            });
            
            package.MainDocumentPart?.ExtractTextFromParts(p => p.FooterParts, str => {
                if (str != "\n") footerResults.Add(str);
            });
            
            var textBoxes = package.MainDocumentPart?.Document?.Descendants<TextBoxContent>()
                .Where(t => !t.Ancestors<AlternateContentFallback>().Any());
            if (textBoxes != null)
            {
                foreach (var textBox in textBoxes)
                {
                    var runs = textBox.Descendants<Text>();
                    foreach (var run in runs)
                    {
                        fullTextTextBuilder.Append(run.Text);
                        textBoxResults.Add(run.Text);
                    }
                    fullTextTextBuilder.AppendLine();
                }
            }
            
            var tables = package.MainDocumentPart?.Document?.Descendants<Table>();
            if (tables != null)
            {
                foreach (var table in tables)
                {
                    StringBuilder tableString = new();
                    var rows = table.Descendants<TableRow>();
                    foreach (var row in rows)
                    {
                        var cells = row.Descendants<TableCell>();
                        foreach (var cell in cells)
                        {
                            var cellParagraphs = cell.Descendants<Paragraph>();
                            foreach (var paragraph in cellParagraphs)
                            {
                                var runs = paragraph.Descendants<Text>();
                                foreach (var run in runs)
                                {
                                    tableString.Append(run.Text);
                                }
                                tableString.Append('\t');
                            }
                        }
                        tableString.AppendLine();
                    }
                    fullTextTextBuilder.Append(tableString);
                    tableResults.Add(tableString.ToString());
                    fullTextTextBuilder.AppendLine();
                }
            }
            
            commentsPart = package.MainDocumentPart?.WordprocessingCommentsPart;
            if (commentsPart?.Comments?.Elements<Comment>() != null)
            {
                foreach (var comment in commentsPart.Comments.Elements<Comment>())
                {
                    var commentText = comment.Descendants<Text>();
                    var commentTextBuilder = new StringBuilder();
                    foreach (var text in commentText)
                    {
                        commentTextBuilder.Append(text.Text);
                    }
                    commentResults.Add(commentTextBuilder.ToString());
                    fullTextTextBuilder.Append(commentTextBuilder);
                    fullTextTextBuilder.AppendLine();
                }
            }

            // Remove "<mc:Fallback>" elements. This removes duplicate TextBox and Image elements.
            mainDocumentPart.Document.Descendants<AlternateContent>().ToList().ForEach(e => e.Remove());
            mainDocumentPart.Document.Descendants<AlternateContentChoice>().ToList().ForEach(e => e.Remove());
            var images = mainDocumentPart.ImageParts.DistinctBy(p => p.Uri.ToString());
            foreach (var imagePart in images)
            {
                using var stream = imagePart.GetStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);

                var base64 = Convert.ToBase64String(ms.ToArray());

                string ocrPrompt = "Please extract and return all the text visible in this image.";

                var result = _aIProviderService.GenerateResponse(
                    documentProcessingRequest.visionModel ?? _defaultVisionModel!,
                    ocrPrompt,
                    [base64],
                    think: false,
                    system: "You are a OCR tool that extracts text from images."
                );

                imageResults.Add(result);
                fullTextTextBuilder.AppendLine(result);
            }
            
            var paragraphs = package.MainDocumentPart?.Document?.Descendants<Paragraph>();
            if (paragraphs != null)
            {
                foreach (var paragraph in paragraphs)
                {
                    bool includeInParagraphList = !paragraph.Ancestors<Table>().Any()
                        && !paragraph.Ancestors<Comment>().Any()
                        && !paragraph.Ancestors<Header>().Any()
                        && !paragraph.Ancestors<Footer>().Any();
                    var paragraphTextBuilder = new StringBuilder();
                    var runs = paragraph.Descendants<Text>();
                    foreach (var run in runs)
                    {
                        fullTextTextBuilder.Append(run.Text);
                        paragraphTextBuilder.Append(run.Text);
                    }
                    if (includeInParagraphList) paragraphResults.Add(paragraphTextBuilder.ToString());
                    fullTextTextBuilder.AppendLine();
                }
            }

            return new DocumentProcessingWordDocumentResultModel(
                fullTextTextBuilder.ToString(),
                [.. paragraphResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. headerResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. footerResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. textBoxResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. tableResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. commentResults.Where(x => !x.IsNullOrWhiteSpace())],
                [.. imageResults.Where(x => !x.IsNullOrWhiteSpace())]
            );
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from DOCX file {FilePath}: {Exception}", filePath, ex.Message);
            throw;
        }
    }

    public async Task<IDocumentProcessingResultModel> ExtractTextFromSpreadsheetAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        string? visionModel = documentProcessingRequest.visionModel;
        string? modelToUse = visionModel ?? _defaultVisionModel;
        
        try
        {
            _logger.LogInformation("Extracting text from spreadsheet: {FilePath}", filePath);

            var fullTextBuilder = new StringBuilder();
            var pageResults = new List<string>();
            var imageResults = new List<string>();

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (extension == ".xlsx")
            {
                await ExtractFromXlsxAsync(filePath, modelToUse, fullTextBuilder, pageResults, imageResults);
            }
            else if (extension == ".xls" || extension == ".ods")
            {
                _logger.LogWarning("Format {Extension} is not yet supported with native libraries. ODS/XLS support requires additional dependencies.", extension);
                throw new NotImplementedException($"Format {extension} requires additional library support. Only .xlsx is currently supported via DocumentFormat.OpenXml.");
            }
            else
            {
                throw new ArgumentException($"Unsupported spreadsheet extension: {extension}");
            }
            
            return new DocumentProcessingSpreadsheetResultModel(
                fullTextBuilder.ToString(),
                pageResults,
                imageResults
            );
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from spreadsheet file {FilePath}: {Exception}", filePath, ex);
            throw;
        }
    }

    private async Task ExtractFromXlsxAsync(string filePath, string? modelToUse, StringBuilder fullTextBuilder, List<string> pageResults, List<string> imageResults)
    {
        using var spreadsheetDoc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = spreadsheetDoc.WorkbookPart ?? throw new InvalidOperationException("Workbook part not found in XLSX file");
        var workbook = workbookPart.Workbook;
        var sheets = workbook?.Sheets ?? throw new InvalidOperationException("No sheets found in workbook");
        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

        bool isFirstSheet = true;

        foreach (var sheet in sheets.Cast<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            if (!isFirstSheet)
                fullTextBuilder.AppendLine(); // Blank line between sheets

            fullTextBuilder.AppendLine($"Sheet: {sheet.Name}");
            var sheetText = new StringBuilder();

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var worksheet = worksheetPart.Worksheet;
            var rows = worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>();

            foreach (var row in rows)
            {
                var cells = row.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().OrderBy(c => c.CellReference?.Value ?? "A1").ToList();
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
                await ExtractImagesFromWorksheetAsync(worksheetPart, modelToUse, imageResults, fullTextBuilder);
            }

            isFirstSheet = false;
        }
    }

    private string GetSpreadsheetCellValue(DocumentFormat.OpenXml.Spreadsheet.Cell cell, DocumentFormat.OpenXml.Spreadsheet.SharedStringTable? sharedStringTable)
    {
        if (cell.CellValue == null)
            return string.Empty;

        string? cellValue = cell.CellValue.Text;
        
        // Handle shared strings
        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString)
        {
            if (int.TryParse(cellValue, out int index) && sharedStringTable != null)
            {
                var sharedStringItem = sharedStringTable.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().ElementAtOrDefault(index);
                if (sharedStringItem != null)
                {
                    var textElements = sharedStringItem.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>();
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
        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.Boolean)
        {
            return cellValue == "0" ? "FALSE" : "TRUE";
        }

        // Handle numbers and other types
        return cellValue ?? string.Empty;
    }

    private async Task ExtractImagesFromWorksheetAsync(WorksheetPart worksheetPart, string modelToUse, List<string> imageResults, StringBuilder fullTextBuilder)
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

            var result = _aIProviderService.GenerateResponse(
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

    public async Task<IDocumentProcessingResultModel> ExtractTextFromPresentationAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        try
        {
            _logger.LogInformation("Extracting text from PowerPoint: {FilePath}", filePath);
            // PowerPoint extraction would require a specific library
            // For now, delegate to Python which has python-pptx
            throw new NotImplementedException("PowerPoint extraction requires calling Python. Use Python's python-pptx library in your script for best results.");
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from PowerPoint: {FilePath}, {Exception}", filePath, ex.Message);
            throw;
        }
    }

    public async Task<IDocumentProcessingResultModel> ExtractTextFromImageAsync(DocumentProcessingRequest documentProcessingRequest)
    {
        string filePath = documentProcessingRequest.filePath;
        string? visionModel = documentProcessingRequest.visionModel;
        string? modelToUse = visionModel ?? _defaultVisionModel;

        if (string.IsNullOrWhiteSpace(modelToUse))
        {
            throw new InvalidOperationException($"Cannot extract text from image '{filePath}' without a vision model. Either provide a vision model parameter (e.g., 'ollama:qwen3-vl:latest') or configure a default vision model in settings.");
        }

        try
        {
            _logger.LogInformation("Extracting text from image {FilePath} using model {Model}", filePath, modelToUse);

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var base64Image = Convert.ToBase64String(fileBytes);
            var mediaType = GetImageMediaType(filePath);

            var ocrPrompt = "Please extract and return all the text visible in this image.";
            
            string response = _aIProviderService.GenerateResponse(modelToUse, ocrPrompt, [base64Image], think: false, system: "You are a OCR tool that extracts text from images. When it makes sense to do so, use markdown");

            _logger.LogInformation("Successfully extracted text from image: {FilePath}", filePath);
            return new DocumentProcessingImageResultModel(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting text from image {FilePath}: {Exception}", filePath, ex.Message);
            throw;
        }
    }

    private string GetImageMediaType(string filePath)
    {
        return System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "image/jpeg"
        };
    }

    private string ExtractTextFromPart(OpenXmlPart part)
    {
        return part switch
        {
            HeaderPart hp => ExtractTextFromElement(hp.Header ?? throw new Exception("Header was null")),
            FooterPart fp => ExtractTextFromElement(fp.Footer ?? throw new Exception("Footer was null")),
            // Add other part types if needed (e.g., CommentParts, etc.)
            _ => string.Empty
        };
    }


    private string ExtractTextFromElement(OpenXmlElement element)
    {
        var sb = new StringBuilder();
        foreach (var child in element.Descendants<Text>())
        {
            sb.Append(child.Text);
        }
        // Handle breaks/tabs in generic elements
        foreach (var br in element.Descendants<Break>()) sb.Append('\n');
        foreach (var tab in element.Descendants<Tab>()) sb.Append('\t');
        return sb.ToString();
    }

}

public class DocumentProcessingRequest(string filePath, string? visionModel)
{
    public readonly string filePath = filePath;
    public readonly string? visionModel = visionModel;
}