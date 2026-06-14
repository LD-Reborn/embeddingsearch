using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using Shared.Services;

namespace Indexer.Helper;

public class PdfProcessorHelper
{
    private readonly ILogger _logger;
    private readonly AIProviderService _aIProviderService;
    private readonly string? _defaultVisionModel;

    public PdfProcessorHelper(ILogger logger, AIProviderService aIProviderService, string? defaultVisionModel = null)
    {
        _logger = logger;
        _aIProviderService = aIProviderService;
        _defaultVisionModel = defaultVisionModel;
    }

    public async Task<List<string>> ExtractImagesFromPdfPageAsync(PdfPage page, int pageNumber, string visionModel, HashSet<string>? processedImageNames = null)
    {
        var imageResults = new List<string>();

        try
        {
            var images = ExtractImagesFromPage(page, processedImageNames);

            if (images.Count == 0)
            {
                _logger.LogInformation("No images found on page {PageNumber}", pageNumber);
            }

            foreach (var imageData in images)
            {
                try
                {
                    // Ensure we have valid image data
                    if (imageData == null || imageData.Length == 0)
                        continue;

                    var base64Image = Convert.ToBase64String(imageData);

                    _logger.LogDebug("Sending {Bytes} bytes to vision model for OCR", imageData.Length);

                    var result = OcrProcessorHelper.ProcessImageForOcr(
                        base64Image,
                        visionModel,
                        _aIProviderService,
                        "You are a OCR tool that extracts text from images. When it makes sense to do so, use markdown"
                    );

                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _logger.LogDebug("Successfully extracted text from image: {TextLength} chars", result.Length);
                        imageResults.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error processing image from PDF page {PageNumber}: {Exception}", pageNumber, ex.Message);
                    // Continue with next image on error
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error extracting images from PDF page {PageNumber}: {Exception}", pageNumber, ex.Message);
        }

        return imageResults;
    }

    public List<byte[]> ExtractImagesFromPage(PdfPage page, HashSet<string>? processedImageNames = null)
    {
        var images = new List<byte[]>();

        try
        {
            // Primary method: extract from XObjects in page resources
            images.AddRange(ExtractImagesFromPageResources(page, processedImageNames));

            // Fallback: search content stream for image references
            if (images.Count == 0)
            {
                _logger.LogDebug("No images found in page resources, searching content stream");
                images.AddRange(ExtractImagesFromPageContentStream(page));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error extracting images from page: {Exception}", ex.Message);
        }

        return images;
    }

    public List<byte[]> ExtractImagesFromPageResources(PdfPage page, HashSet<string>? processedImageNames = null)
    {
        var images = new List<byte[]>();

        try
        {
            var resources = page.GetResources();
            if (resources == null)
            {
                _logger.LogDebug("No resources found on page");
                return images;
            }

            var resourceDict = resources.GetPdfObject();
            if (resourceDict == null)
            {
                _logger.LogDebug("Resources PdfObject is not a dictionary");
                return images;
            }

            _logger.LogDebug("Resource dictionary keys: {Keys}", string.Join(", ", resourceDict.KeySet().Select(k => k.ToString())));

            if (resourceDict.Get(PdfName.XObject) is not PdfDictionary xObjects)
            {
                _logger.LogDebug("No XObject dictionary in resources");
                return images;
            }

            _logger.LogInformation("Found {Count} XObjects in page", xObjects.KeySet().Count);

            foreach (var name in xObjects.KeySet())
            {
                try
                {
                    // Skip if we've already processed this image XObject
                    var nameStr = name.ToString();
                    if (processedImageNames != null && processedImageNames.Contains(nameStr))
                    {
                        _logger.LogDebug("Skipping already processed image XObject: {Name}", nameStr);
                        continue;
                    }

                    var xObject = xObjects.Get(name);
                    if (xObject == null)
                    {
                        _logger.LogDebug("XObject {Name} is null", name);
                        continue;
                    }

                    if (xObject is not PdfStream pdfStream)
                    {
                        _logger.LogDebug("XObject {Name} is not a PdfStream (type: {Type})", name, xObject?.GetType().Name);
                        continue;
                    }

                    if (pdfStream.Get(PdfName.Subtype) is not PdfName subtype)
                    {
                        _logger.LogDebug("XObject {Name} has no Subtype", name);
                        continue;
                    }

                    _logger.LogDebug("XObject {Name} subtype: {Subtype}", name, subtype);

                    if (!PdfName.Image.Equals(subtype))
                    {
                        _logger.LogDebug("XObject {Name} is not an Image (subtype: {Subtype})", name, subtype);
                        continue;
                    }

                    _logger.LogInformation("Found image XObject: {Name}", name);
                    PdfImageXObject image = new(pdfStream);
                    // Try multiple extraction strategies
                    var imageBytes = image.GetImageBytes();
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        _logger.LogInformation("Successfully extracted image ({Bytes} bytes) from page", imageBytes.Length);
                        images.Add(imageBytes);

                        // Mark as processed to avoid duplicates on other pages
                        processedImageNames?.Add(nameStr);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to extract image bytes from XObject {Name}", name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error processing PDF image object {Name}: {Exception}", name, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error accessing PDF resources for images: {Exception}", ex.Message);
        }

        return images;
    }

    public List<byte[]> ExtractImagesFromPageContentStream(PdfPage page)
    {
        var images = new List<byte[]>();

        try
        {
            // Look for inline images (BI/ID/EI operators) or XObject references in content stream
            var contentBytes = page.GetContentBytes();
            if (contentBytes == null || contentBytes.Length == 0)
                return images;

            // This is a simplified check for image markers
            var contentStr = Encoding.Latin1.GetString(contentBytes);

            // Check if content contains image references
            bool hasImageRefs = contentStr.Contains("Do") || // XObject reference
                               contentStr.Contains("BI") || // Begin inline image
                               contentStr.Contains("ID");    // Inline image data

            if (hasImageRefs)
            {
                _logger.LogInformation("Page content stream contains image references (might indicate embedded images)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error analyzing content stream: {Exception}", ex.Message);
        }

        return images;
    }
}
