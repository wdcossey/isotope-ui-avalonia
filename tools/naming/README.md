# The rename table

`renames.json` maps Reactor's names to Isotope's Avalonia names (#31). The library's tests check the code against it
(`tests/Isotope.UI.Avalonia.Tests/RenameTableTests.cs`), and the Reactor migration analyzer (#33) reads it to rewrite
Reactor code. Change it in the same commit as the code it describes.

## The naming rule

A member of an element record has Avalonia's name when it is one of these, on the record's Avalonia control:

- a property or an event (`Text`, `Minimum`, `IsChecked`, `Content`);
- `On` plus an event, for a callback (`OnClick` for `Click`, `OnTextChanged` for `TextChanged`);
- `On` plus a property plus `Changed`, for a value's callback (`OnSelectedIndexChanged`, `OnIsExpandedChanged`).

The generator maps members by these names, so a record that follows the rule needs no `[WrapAlias]`. A member with no
Avalonia counterpart keeps Reactor's name and gets a `kept` entry that says why. Factory parameters are the members'
names in camel case (`text`, `onTextChanged`).

## Format

`version` is 1. The sections:

| Section | Entry | Meaning |
|---|---|---|
| `records` | `"<record>": "<control>"` | Each element or data record (by Reactor's type name, also when an applied `types` entry gives it an Avalonia name: `TreeViewNodeData` is `TreeViewItemData` in the code) and the full name of the Avalonia type its members are named after. Every record with `[GenerateReactorDescriptor]` is listed, with the control in that attribute. |
| `members` | `{ "record", "reactor", "avalonia", "kind", "note"? }` | A record member whose Reactor name or shape differs, or that keeps Reactor's name. |
| `parameters` | `{ "factory", "reactor", "avalonia" }` | A factory parameter renamed, in every overload of that factory. |
| `modifiers` | `{ "reactor", "avalonia", "target"?, "status", "note"? }` | An extension method on elements (`target`: the record it extends, when only one). |
| `types` | `{ "reactor", "avalonia", "status", "note"? }` | A record or data type. |
| `factories` | `{ "reactor", "avalonia", "status", "note"?, "with"? }` | A factory method. `with` is an object initializer the migration writes onto each call it renames, `(ScrollViewer(...) with { ... })`, so the call keeps the Reactor factory's behaviour; its names are written in full, because a using added for them can give other Reactor names a meaning. |

In `members`:

- `record` is Reactor's record name, a key of `records`; `reactor` is Reactor's member name.
- `avalonia` is the member's name here: a string, or an array when one member became several (`GridElement.Definition`
  is `ColumnDefinitions` and `RowDefinitions`).
- `kind` is one of:
  - `rename`: the same member under another name. A migration renames it.
  - `shape`: the member changed shape (merged with another, split, or retyped); `note` says how. A migration flags it
    for a hand fix, or fixes the simple cases. Several entries can share one `avalonia` member (`Label` and
    `ContentElement` are one `Content`); an entry whose `avalonia` equals `reactor` is a retyped member.
  - `kept`: no Avalonia counterpart, so the name stays (`avalonia` equals `reactor`); `note` says why.

`status` is `applied` (the code has the Avalonia name only) or `proposed` (the code still has Reactor's name, and
applying it is a rename in the code plus a status change here). An `avalonia` of `null` in `modifiers`, `types` or
`factories` means Reactor's name is gone with no counterpart (`GridDefinition`); it is applied, and `note` says what
replaces it. Entries in `members` and `parameters` are applied.
Type and factory renames (`TabView` → `TabControl` and the like) are proposed until the question on #27 is settled.

## Porting a tier 3 change from upstream

Upstream's element records, factories and `Customize` bodies use Reactor's names. When a sync report lists a tier 3 PR
that touches them:

1. Port the change with Isotope's names: look each Reactor member, parameter and modifier up here.
2. A new Reactor member gets the Avalonia name the rule gives it, and an entry here (`rename`, `shape` or `kept`).
3. A new record gets a `records` entry. `RenameTableTests` fails until every member and parameter follows the rule
   or the table.
