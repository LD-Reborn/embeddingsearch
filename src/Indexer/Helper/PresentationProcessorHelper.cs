using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using Server;
using Indexer.Models;

namespace Indexer.Helper;

public static class PresentationProcessorHelper
{
    public static async Task<IDocumentProcessingResultModel> ExtractFromOdpAsync(string filePath, string? modelToUse, AIProviderService aiProviderService, ILogger logger)
    {
        try
        {
            var fullTextBuilder = new StringBuilder();
            var slideResults = new List<string>();
            var imageResults = new List<string>();

            // ODP files are ZIP archives containing XML
            using var archive = ZipFile.OpenRead(filePath);
            var contentEntry = archive.GetEntry("content.xml") ?? throw new InvalidOperationException("content.xml not found in ODP file");

            using var stream = contentEntry.Open();
            using var reader = new StreamReader(stream);
            var contentXml = await reader.ReadToEndAsync();
            var xdoc = XDocument.Parse(contentXml);

            // ODP namespaces
            var presentationNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:presentation:1.0");
            var drawNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:drawing:1.0");
            var textNs = XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:text:1.0");
            var xLinkNs = XNamespace.Get("http://www.w3.org/1999/xlink");

            // Extract slides
            var slides = xdoc.Descendants(drawNs + "page");
            int slideNumber = 0;

            foreach (var slide in slides)
            {
                slideNumber++;
                var slideTextBuilder = new StringBuilder();
                var slideTitle = slide.Attribute("name")?.Value ?? $"Slide {slideNumber}";

                slideTextBuilder.AppendLine($"--- {slideTitle} ---");

                // Extract text from all text elements in the slide
                var textElements = slide.Descendants(textNs + "p");
                foreach (var textElement in textElements)
                {
                    var elementText = ExtractTextFromElement(textElement, textNs);
                    if (!string.IsNullOrWhiteSpace(elementText))
                    {
                        slideTextBuilder.AppendLine(elementText);
                    }
                }

                // Extract text from text boxes
                var textBoxes = slide.Descendants(drawNs + "text-box");
                foreach (var textBox in textBoxes)
                {
                    var paragraphs = textBox.Descendants(textNs + "p");
                    foreach (var paragraph in paragraphs)
                    {
                        var paragraphText = ExtractTextFromElement(paragraph, textNs);
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            slideTextBuilder.AppendLine(paragraphText);
                        }
                    }
                }

                // Extract text from shapes
                var shapes = slide.Descendants(drawNs + "custom-shape");
                foreach (var shape in shapes)
                {
                    var paragraphs = shape.Descendants(textNs + "p");
                    foreach (var paragraph in paragraphs)
                    {
                        var paragraphText = ExtractTextFromElement(paragraph, textNs);
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            slideTextBuilder.AppendLine(paragraphText);
                        }
                    }
                }

                var slideText = slideTextBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(slideText))
                {
                    slideResults.Add(slideText);
                    fullTextBuilder.AppendLine(slideText);
                    fullTextBuilder.AppendLine();
                }
            }

            // Extract images from ODP if vision model is available
            if (!string.IsNullOrWhiteSpace(modelToUse))
            {
                await ExtractImagesFromOdpAsync(archive, modelToUse, imageResults, fullTextBuilder, aiProviderService);
            }

            logger.LogInformation("Successfully extracted text from {SlideCount} slides in ODP: {FilePath}", slideNumber, filePath);

            return new DocumentProcessingPresentationResultModel(
                fullTextBuilder.ToString(),
                slideResults,
                imageResults.Count > 0 ? imageResults : null
            );
        }
        catch (Exception ex)
        {
            logger.LogError("Error extracting text from ODP file {FilePath}: {Exception}", filePath, ex.Message);
            throw;
        }
    }

    private static string ExtractTextFromElement(XElement element, XNamespace textNs)
    {
        var sb = new StringBuilder();

        // Extract text from spans (styled text)
        var spans = element.Descendants(textNs + "span");
        if (spans.Any())
        {
            foreach (var span in spans)
            {
                var spanText = ExtractTextFromElement(span, textNs);
                if (!string.IsNullOrWhiteSpace(spanText))
                    sb.Append(spanText);
            }
        }
        else
        {
            // Extract direct text content
            var textNodes = element.Nodes();
            foreach (var textNode in textNodes)
            {
                try
                {
                    sb.Append(((dynamic)textNode).Value);
                }
                catch
                {
                    // Skip nodes that don't have a Value property
                }
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

    private static async Task ExtractImagesFromOdpAsync(ZipArchive archive, string modelToUse, List<string> imageResults, StringBuilder fullTextBuilder, AIProviderService aiProviderService)
    {
        // In ODP, images are typically stored in Pictures/ folder
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
