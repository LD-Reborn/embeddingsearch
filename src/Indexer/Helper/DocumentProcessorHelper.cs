using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using Quartz.Util;

namespace Indexer.Helper;

public static class DocumentProcessorHelper
{
    /// <summary>
    /// Extracts all text from parts (e.g., headers, footers) in a Word document.
    /// </summary>
    /// <typeparam name="TPart">Type of the part (e.g., HeaderPart, FooterPart)</typeparam>
    /// <param name="package">The WordprocessingDocumentPackage (e.g., MainDocumentPart).</param>
    /// <param name="getPartsFunc">Function to retrieve parts collection (e.g., part => part.HeaderParts).</param>
    /// <param name="processText">Callback for processing each Text run (e.g., append to StringBuilder, collect in list).</param>
    public static void ExtractTextFromParts<TPart>(
        this MainDocumentPart mainDocPart,
        Func<MainDocumentPart, IEnumerable<TPart>> getPartsFunc,
        Action<string> processText)
        where TPart : OpenXmlPart
    {
        if (mainDocPart is null) return;

        var parts = getPartsFunc(mainDocPart);
        if (parts is null) return;

        foreach (var part in parts)
        {
            var rootElement = part.RootElement;
            if (rootElement is not null)
            {
                var paragraphs = rootElement.Descendants<Paragraph>();
                foreach (var paragraph in paragraphs)
                {
                    var elements = paragraph.Descendants<OpenXmlElement>()
                        .Where(e => e is Text || e is Break);
                    foreach (var element in elements)
                    {
                        if (element is Text run && !string.IsNullOrEmpty(run.Text))
                        {
                            processText(run.Text);
                        } else if (element is Break br)
                        {
                            processText("\n");
                        }
                    }
                    processText("\n");
                }
                if (!paragraphs.Any() && !rootElement.InnerText.IsNullOrWhiteSpace())
                {
                    processText(rootElement.InnerText);
                    processText("\n");
                }
            }
        }
    }

    // Optional: overload with StringBuilder convenience
    public static string ExtractTextFromParts<TPart>(
        this MainDocumentPart mainDocPart,
        Func<MainDocumentPart, IEnumerable<TPart>> getPartsFunc)
        where TPart : OpenXmlPart
    {
        var sb = new StringBuilder();
        mainDocPart.ExtractTextFromParts(getPartsFunc, text => sb.Append(text));
        sb.AppendLine(); // trailing newline
        return sb.ToString();
    }
}

