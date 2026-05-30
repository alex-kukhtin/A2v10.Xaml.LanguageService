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

// Typing an element name at the document root: `<Pag|`, `<|` (empty doc).
// Root scope — the completion universe is the root-element types. The child
// counterpart is ChildTagNameContext.
//   Element — the partial tag being typed; null when no node yet exists
//             (the `<|` trigger-character moment on an empty document).
public sealed record TagNameContext(
    XamlElement? Element,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing an element name inside another element: `<a><Butt|`, `<a><|`.
// Child scope — the completion universe is the container's content model.
// The `<`-not-yet-typed counterpart is ElementContentContext; the root
// counterpart is TagNameContext.
//   Element   — the partial tag being typed; null at the `<a><|` moment
//               where the '<' has no name node yet.
//   Container — the element this tag lives inside; always present (a child
//               by definition has a container).
public sealed record ChildTagNameContext(
    XamlElement? Element,
    XamlElement Container,
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

// --- Markup extension contexts ----------------------------------------
//
// All four carry the owning XamlAttribute / XamlElement so handlers don't
// have to walk Parent. Extension is the *immediately enclosing* one — for
// nested cases (`{Binding Source={StaticResource F|oo}}`) Extension is the
// inner extension, and the outer is reachable via Extension.Parent walk.

// Typing the markup extension type name: `{Bind|`, `{|`, `{x:Stat|ic`.
//   TypeNameSpan may have Length == 0 (the `{|` moment) — that is the
//   reason we route here even when the cursor sits in empty span.
public sealed record ExtensionTypeNameContext(
    XamlElement Element,
    XamlAttribute Attribute,
    XamlExtension Extension,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Cursor inside an extension but not on any specific node — whitespace
// after the type name, between arguments, before `}`. Completion here
// typically offers argument names of the extension type.
public sealed record ExtensionInteriorContext(
    XamlElement Element,
    XamlAttribute Attribute,
    XamlExtension Extension,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing the *name* of a named argument: `{Binding Pa|th=Foo}`. Completion
// offers property names of the extension type.
public sealed record ExtensionArgNameContext(
    XamlElement Element,
    XamlAttribute Attribute,
    XamlExtension Extension,
    XamlNamedArgument Argument,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);

// Typing an argument value. Covers both named (`{Binding Path=Na|me}`,
// `{Binding Path=|}`) and positional (`{Binding Na|me}`).
//   Value is null when the user just typed `=` and nothing follows.
//   Argument is XamlNamedArgument or XamlPositionalArgument — consumers
//   pattern-match on it when they need to distinguish.
public sealed record ExtensionArgValueContext(
    XamlElement Element,
    XamlAttribute Attribute,
    XamlExtension Extension,
    XamlExtensionArgument Argument,
    XamlExtensionValue? Value,
    IReadOnlyList<XamlDiagnostic> Diagnostics)
    : XamlPositionContext(Diagnostics);
