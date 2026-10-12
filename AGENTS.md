# Global Coding Style Rules

The following rules apply to all programming projects. You must follow these guidelines strictly unless explicitly told otherwise.

1. **Strictly NO COMMENTS in the code**: Do not add comments or docstrings to the code, and preserve existing ones only if they are unrelated to the changes.
2. **Space after casts**: Always format type casts with a space (e.g., `(float) delta`, `(int) myVar`).
3. **Guard clauses**: Use early returns to prevent deep nesting and keep the code clean.
4. **Preserve external APIs**: When refactoring, always use backward-compatible properties or wrapper functions so that external scripts and systems do not break.
5. **Clickable links in chat**: Always provide clickable markdown links using the `file:///` format for symbols and files when discussing code (e.g., `[Player.cs](file:///path/to/Player.cs)`).
6. **Explicit Static Typing**: Always prefer strict static typing over `var` (e.g., use `float someVar = calculateSomething();` instead of `var someVar = calculateSomething();`).
7. **Target-Typed `new()` Expressions**: Always prefer using `new()` when the type is explicitly declared on the left side (e.g., use `ClassName obj = new();` instead of `ClassName obj = new ClassName();` or `var obj = new ClassName();`). Do not use `new()` where the type is unclear.
8. **Semantic Line Spacing**: Group logical chunks of code tightly together and separate different steps with a single empty line:
   - Bunch all early returns / guard clauses at the top of a method without empty lines between them, and place an empty line after the last one.
   - Keep lines that achieve a single specific task together as one contiguous block. Add an empty line after the block before starting the next task.
9. **No fully-qualified namespaces or redundant type qualifiers**:
   - Never include namespaces before classnames in the code body (e.g., `System.IO.Directory`). Always add the `using` statement at the top of the file instead.
   - Avoid redundant class/type qualifiers for static members, events, and enum values where `using static` or class inheritance can be used (e.g., use `AddHandler(PointerReleasedEvent, handler, Tunnel)` instead of `InputElement.PointerReleasedEvent` and `RoutingStrategies.Tunnel`).
10. **Strict Braces**: Never omit braces `{}` for block statements (e.g., `if`, `while`, `for`) unless it is a single-line statement that ends in an early return (`return`, `continue`, or `break`). Do not put single-line statements without early returns on the same line (e.g., `while (cond) doSomething();` is forbidden, use braces instead).
11. **No Braceless Else**: Never use braceless `if`/`else` blocks. Always enclose the bodies in braces `{ }`.
12. **NO GIT COMMANDS**: Strictly NEVER run git commands (`git`) in the terminal under any circumstances.
13. **AXAML / XAML Layouting & Formatting Style**:
   - Every attribute / property of an element with multiple properties must be on its own line, indented under the element tag.
   - Self-closing tags should close on the same line as the final attribute (e.g., `Attribute="Value"/>`).
   - Use empty lines between sibling elements and child controls inside layout containers (`Grid`, `StackPanel`, `Border`, `Menu`, `Popup`, etc.) to keep UI elements visually separated and readable.
   - Use empty lines between `<Style>` blocks in `<Styles>`.
   - Separate major layout containers and structural sections with empty lines.
