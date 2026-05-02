from __future__ import annotations
from dataclasses import dataclass
import array
from typing import Optional
from enum import Enum

@dataclass
class JSONDatapoint:
    Name:str
    Text:str
    Probmethod_embedding:str
    SimilarityMethod:str
    Model:list[str]

@dataclass
class JSONEntity:
    Name:str
    Probmethod:str
    Searchdomain:str
    Attributes:dict
    Datapoints:array.array[JSONDatapoint]

#Model - Searchdomain
@dataclass
class SearchdomainListResults:
    Searchdomains:list[str]

@dataclass
class SearchdomainCreateResults:
    Success:bool
    id:int|None

@dataclass
class SearchdomainUpdateResults:
    Success:bool

@dataclass
class SearchdomainDeleteResults:
    Success:bool
    DeletedEntities:int

#Model - Entity
@dataclass
class EntityQueryResult:
    name:str
    ValueError:float

@dataclass
class EntityQueryResults:
    Results:list[EntityQueryResult]

@dataclass
class EntityIndexResult:
    Success:bool

@dataclass
class AttributeResult:
    Name:str
    Value:str

@dataclass
class EmbeddingResult:
    Model:str
    Embeddings:array.array[float]

@dataclass
class DatapointResult:
    Name:str
    ProbMethod:str
    Embeddings:list[EmbeddingResult]|None

@dataclass
class EntityListResults:
    Name:str
    Attributes:list[AttributeResult]
    Datapoints:list[DatapointResult]

@dataclass
class EntityDeleteResults:
    Success:bool

# Model - Client
@dataclass
class Client:
    baseUri:str
    apiKey:str
    searchdomain:str
    async def SearchdomainListAsync() -> SearchdomainListResults:
        pass
    async def SearchdomainDeleteAsync() -> SearchdomainDeleteResults:
        pass
    async def SearchdomainCreateAsync() -> SearchdomainCreateResults:
        pass
    async def SearchdomainCreateAsync(searchdomain:str) -> SearchdomainCreateResults:
        pass
    async def SearchdomainUpdateAsync(newName:str, settings:str) -> SearchdomainUpdateResults:
        pass
    async def SearchdomainUpdateAsync(searchdomain:str, newName:str, settings:str) -> SearchdomainUpdateResults:
        pass
    async def EntityQueryAsync(query:str) -> EntityQueryResults:
        pass
    async def EntityQueryAsync(searchdomain:str, query:str) -> EntityQueryResults:
        pass
    #async def EntityIndexAsync(jsonEntity): # -> EntityIndexResult:#:NetList[JSONEntity]) -> EntityIndexResult: #TODO fix clr issues, i.e. make this work
    #    pass
    #async def EntityIndexAsync(searchdomain:str, jsonEntity:list[JSONEntity]) -> EntityIndexResult:
    #    pass
    async def EntityIndexAsync(jsonEntity:str) -> EntityIndexResult:
        pass
    async def EntityIndexAsync(jsonEntity:str, sessionId:str, sessionComplete:bool) -> EntityIndexResult:
        pass
    async def EntityIndexAsync(searchdomain:str, jsonEntity:str) -> EntityIndexResult:
        pass
    async def EntityListAsync(returnEmbeddings:bool = False) -> EntityListResults:
        pass
    async def EntityListAsync(searchdomain:str, returnEmbeddings:bool = False) -> EntityListResults:
        pass
    async def EntityDeleteAsync(searchdomain:str, entityName:str) -> EntityDeleteResults:
        pass

@dataclass
class ICallbackInfos:
    pass

@dataclass
class IntervalCallbackInfos(ICallbackInfos):
    sender: Optional[object]
    e: object

@dataclass
class LoggerWrapper:
    def LogTrace(message:str, args:list[object]) -> None:
        pass
    def LogDebug(message:str, args:list[object]) -> None:
        pass
    def LogInformation(message:str) -> None:
        pass
    def LogInformation(message:str, args:list[object]) -> None:
        pass
    def LogWarning(message:str, args:list[object]) -> None:
        pass
    def LogError(message:str, args:list[object]) -> None:
        pass
    def LogCritical(message:str, args:list[object]) -> None:
        pass

@dataclass
class CancellationTokenRegistration:
    Token: CancellationToken
    def Dispose() -> None:
        pass
    def Unregister() -> None:
        pass

@dataclass
class WaitHandle:
    SafeWaitHandle: object
    def Close() -> None:
        pass
    def Dispose() -> None:
        pass
    def WaitOne() -> bool:
        pass
    def WaitOne(timeout:int) -> bool:
        pass


@dataclass
class CancellationToken:
    CanBeCanceled: bool
    IsCancellationRequested: bool
    def ThrowIfCancellationRequested() -> None:
        pass
    def Register(callback: callable[[], any]) -> CancellationTokenRegistration:
        pass
    WaitHandle: WaitHandle
    

@dataclass
class Toolset:
    Name:str
    FilePath:str
    Client:Client
    Logger:LoggerWrapper
    Configuration: object
    CancellationToken: CancellationToken
    Name:str
    CallbackInfos: Optional[ICallbackInfos] = None
    DocumentProcessor: Optional['DocumentProcessor'] = None

@dataclass
class DocumentProcessor:
    """
    Processes documents and images to extract text content.
    
    Supports:
    - Images (PNG, JPG, GIF, BMP, WebP, TIFF) - uses OCR if vision model is configured
    - Text files (TXT, CSV, JSON, XML)
    - Documents (PDF, DOCX, XLSX, PPTX) - best handled via Python libraries
    
    Usage examples:
        # Extract text from any file (with vision model for images if configured)
        text = await toolset.DocumentProcessor.GetTextContentAsync("./path/to/file.pdf")
        
        # Extract text from image using specific vision model for OCR
        text = await toolset.DocumentProcessor.GetTextContentAsync(
            "./image.png", 
            "ollama:qwen3-vl:latest"
        )
        
        # Check if file type is supported
        if toolset.DocumentProcessor.IsImageFile("./photo.jpg"):
            # Handle as image
            pass
        
        # Get raw file bytes
        raw_bytes = await toolset.DocumentProcessor.GetFileContentAsync("./file.bin")
    """
    
    async def GetTextContentAsync(file_path: str, vision_model: Optional[str] = None) -> str:
        """
        Extracts text content from a file.
        
        Args:
            file_path: Path to the file to process
            vision_model: Optional vision model for OCR (e.g., "ollama:qwen3-vl:latest")
                         Overrides the default vision model from configuration
        
        Returns:
            Extracted text content
        
        For document files (PDF, DOCX, XLSX):
            - Returns NotImplementedError with guidance to use Python libraries
            - Use Python's PyPDF, python-docx, openpyxl, pandas libraries instead
            - This allows better control and performance for complex documents
        
        For text files (TXT, CSV, JSON, XML):
            - Returns file contents as-is
        
        For image files (PNG, JPG, GIF, BMP, WebP, TIFF):
            - Uses OCR if vision_model is provided or default is configured
            - Returns extracted text from image
            - Raises error if no vision model available
        """
        pass
    
    async def GetTextContentAsync(file_path: str) -> str:
        """Gets text content using the default vision model from configuration."""
        pass
    
    async def GetFileContentAsync(file_path: str) -> bytearray:
        """Gets raw file bytes."""
        pass
    
    def IsImageFile(file_path: str) -> bool:
        """Checks if a file is an image that might require OCR."""
        pass
    
    def IsDocumentFile(file_path: str) -> bool:
        """Checks if a document file format is supported."""
        pass

