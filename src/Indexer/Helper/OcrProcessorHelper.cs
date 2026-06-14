using System.Text;
using Shared.Services;

namespace Indexer.Helper;

public static class OcrProcessorHelper
{
    private const string OcrPrompt = "Please extract and return all the text visible in this image.";
    private const string OcrSystemPrompt = "You are a OCR tool that extracts text from images.";

    /// <summary>
    /// Processes a base64-encoded image through the vision model for OCR text extraction and appends to results and builder.
    /// </summary>
    /// <param name="base64Image">The base64-encoded image data.</param>
    /// <param name="imageIdentifier">A human-readable identifier for the image (e.g., file path, URL).</param>
    /// <param name="modelToUse">The vision model to use for OCR.</param>
    /// <param name="imageResults">List to collect extracted text results.</param>
    /// <param name="fullTextBuilder">StringBuilder to append formatted results.</param>
    /// <param name="aiProviderService">The AI provider service for generating responses.</param>
    public static async Task ProcessImageForOcrAsync(
        string base64Image, 
        string imageIdentifier, 
        string modelToUse, 
        List<string> imageResults, 
        StringBuilder fullTextBuilder, 
        AIProviderService aiProviderService)
    {
        var result = ProcessImageForOcr(base64Image, modelToUse, aiProviderService);
        
        imageResults.Add(result);
        fullTextBuilder.AppendLine($"[IMAGE: {imageIdentifier}]");
        fullTextBuilder.AppendLine(result);
    }

    /// <summary>
    /// Processes a base64-encoded image through the vision model for OCR text extraction.
    /// Returns the extracted text without appending to any collections.
    /// </summary>
    /// <param name="base64Image">The base64-encoded image data.</param>
    /// <param name="modelToUse">The vision model to use for OCR.</param>
    /// <param name="aiProviderService">The AI provider service for generating responses.</param>
    /// <returns>The extracted text from the image.</returns>
    public static string ProcessImageForOcr(
        string base64Image, 
        string modelToUse, 
        AIProviderService aiProviderService)
    {
        return ProcessImageForOcr(base64Image, modelToUse, aiProviderService, OcrSystemPrompt);
    }

    /// <summary>
    /// Processes a base64-encoded image through the vision model for OCR text extraction with custom system prompt.
    /// Returns the extracted text without appending to any collections.
    /// </summary>
    /// <param name="base64Image">The base64-encoded image data.</param>
    /// <param name="modelToUse">The vision model to use for OCR.</param>
    /// <param name="aiProviderService">The AI provider service for generating responses.</param>
    /// <param name="systemPrompt">Custom system prompt to use for the OCR request.</param>
    /// <returns>The extracted text from the image.</returns>
    public static string ProcessImageForOcr(
        string base64Image, 
        string modelToUse, 
        AIProviderService aiProviderService,
        string systemPrompt)
    {
        return aiProviderService.GenerateResponse(
            modelToUse,
            OcrPrompt,
            [base64Image],
            think: false,
            system: systemPrompt
        );
    }
}
