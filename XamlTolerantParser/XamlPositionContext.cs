using System.Collections.Generic;

namespace XamlTolerantParser;

// Structural context of a cursor position in a vxaml document — what is
// being edited, with only the slots that make sense for each variant.
//
// Discriminated union pattern: XamlPositionContext is abstract; the concrete
// variant carries exactly the fields that are valid for its shape. Consumers
// dispatch with a switch expression on the runtime type, which the compiler
// can check for exhaustiveness when every arm is listed.
//
// Nullability is meaningful here:
//   * a non-nullable field means the parser guarantees the value
//   * a nullable field marks a real edge case the consumer must handle
//     (e.g. TagNameContext.Element is null when the user just typed `<`)
public abstract record XamlPositionContext(IReadOnlyList<XamlDiagnostic> Diagnostics);

// Outside any root element (inter-root whitespace, empty document).
public sealed record OutsideContext(
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing an element name in an open tag: `<Butt|`, `<|`.
//   Element          — the partial tag being typed; null when no node yet
//                      exists (the `<|` trigger-character moment, or when
//                      the cursor sits in a parent like `<a><|`).
//   ContainerElement — the element this tag lives inside; null for root.
public sealed record TagNameContext(
    XamlElement? Element,
    XamlElement? ContainerElement,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing the name in a close tag: `</Bu|`, `</|`.
//   Element — the matching open element; null when the close tag is
//             unmatched (`<a></Butt|`).
public sealed record EndTagNameContext(
    XamlElement? Element,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Inside an open tag, between attribute slots, on whitespace: `<Button | />`.
public sealed record TagInteriorContext(
    XamlElement Element,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing an attribute name: `<Button Wi|`, `<Button Width|`.
public sealed record AttributeNameContext(
    XamlElement Element,
    XamlAttribute Attribute,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Inside an attribute value: `<Button Width="10|"`.
//   Value — the value node; null when the value is missing after `=`
//           (the `<a b=|` case — the parser has the attribute and equals
//           but no value to attach).
public sealed record AttributeValueContext(
    XamlElement Element,
    XamlAttribute Attribute,
    XamlValue? Value,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Between children of an element, not inside any text / comment / cdata.
public sealed record ElementContentContext(
    XamlElement Element,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Inside a text content node: `<a>he|llo</a>`.
//   Element — the containing element; nullable because text can exist at
//             root scope when the document is malformed.
public sealed record TextContext(
    XamlElement? Element,
    XamlText Node,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Inside a comment: `<!-- | -->`. Element nullable for root-level comments.
public sealed record CommentContext(
    XamlElement? Element,
    XamlComment Node,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Inside CDATA: `<![CDATA[ | ]]>`. Element nullable for root-level cdata.
public sealed record CDataContext(
    XamlElement? Element,
    XamlCData Node,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);
