namespace XamlTolerantParser;

public enum TokenKind
{
    None,

    StartTagOpen,       // <
    EndTagOpen,         // </
    TagClose,           // >
    SelfTagClose,       // />
    Equals,             // =

    ElementName,        // <Name or </Name (may include ':' and '.')
    AttributeName,      // attr name (may include ':')
    AttributeValue,     // "..." or '...' — quotes are part of the span

    Content,            // raw text between tags
    Comment,            // <!-- ... -->
    CData,              // <![CDATA[ ... ]]>

    Unknown,
    EndOfFile,
}
