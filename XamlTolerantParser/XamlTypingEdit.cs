namespace XamlTolerantParser;

// Neutral, LSP-free description of a single on-type edit produced by
// XamlDocument.EditAt. The server maps it 1:1 to an LSP TextEdit
// (Replace -> Range, NewText -> newText).
//
// The caret is deliberately NOT carried as a separate field: VS 2026 derives it
// from the edit shape (verified live — see project memory), so the shape IS the
// caret instruction:
//   * an empty Replace at the caret leaves the caret to the LEFT of NewText;
//   * a Replace whose range END coincides with the caret moves the caret to the
//     END of NewText.
// EditAt picks the shape per trigger:
//   '>'  ->  empty Replace at the caret, NewText "</name>"  => caret between tags
//   '/'  ->  Replace of the just-typed '/', NewText "/>"     => caret after '>'
public sealed record XamlTypingEdit(TextSpan Replace, string NewText);
