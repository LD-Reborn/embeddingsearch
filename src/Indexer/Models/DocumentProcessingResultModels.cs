namespace Indexer.Models;

public interface IDocumentProcessingResultModel
{
    public string FullText { get; set; }
    public Dictionary<string, dynamic> AsDictionary { get; set; }
}

public class DocumentProcessingTextResultModel(string fullText) : IDocumentProcessingResultModel
{
    public string FullText { get; set; } = fullText;
    public Dictionary<string, dynamic> AsDictionary { get; set; } = new Dictionary<string, dynamic> { { "FullText", fullText} };
}

public class DocumentProcessingImageResultModel(string fullText) : IDocumentProcessingResultModel
{
    public string FullText { get; set; } = fullText;
    public Dictionary<string, dynamic> AsDictionary { get; set; } = new Dictionary<string, dynamic> { { "FullText", fullText} };
}

public class DocumentProcessingWordDocumentResultModel(string fullText, List<string> paragraphs, List<string> headerParts, List<string> footerParts, List<string> textBoxes, List<string> tables, List<string> comments, List<string> images) : IDocumentProcessingResultModel
{
    public string FullText { get; set; } = fullText;
    public Dictionary<string, dynamic> AsDictionary { get; set; } = new Dictionary<string, dynamic> {
        { "FullText", fullText},
        { "Paragraphs", paragraphs},
        { "HeaderParts", headerParts},
        { "FooterParts", footerParts},
        { "TextBoxes", textBoxes},
        { "Tables", tables},
        { "Comments", comments},
        { "Images", images}
    };
    public List<string> Paragraphs { get; set; } = paragraphs;
    public List<string> HeaderParts { get; set; } = headerParts;
    public List<string> FooterParts { get; set; } = footerParts;
    public List<string> TextBoxes { get; set; } = textBoxes;
    public List<string> Tables { get; set; } = tables;
    public List<string> Comments { get; set; } = comments;
    public List<string> Images { get; set; } = images;
}

public class DocumentProcessingPdfResultModel(string fullText, List<string> pages, List<string>? images = null) : IDocumentProcessingResultModel
{
    public string FullText { get; set; } = fullText;
    public Dictionary<string, dynamic> AsDictionary { get; set; } = new Dictionary<string, dynamic> {
        { "FullText", fullText},
        { "Pages", pages},
        { "Images", images ?? []}
    };
    public List<string> Pages { get; set; } = pages;
    public List<string> Images { get; set; } = images ?? new List<string>();
}
