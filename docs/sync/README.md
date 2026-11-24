# Sync reports

`tools/sync` writes one report per upstream sync here, named `<old>-<new>.md` after the two upstream commits. Each
lists the paths to classify, the tier 3 PRs to port by hand, and the tier 1 and 2 PRs it regenerated.

**Porting a tier 3 PR:** read it and port what applies to our rewritten files. Element records, factories, modifiers
and `Customize` bodies take Avalonia's member names, not upstream's: map each Reactor name through
[`tools/naming/renames.json`](../../tools/naming/renames.json), and add an entry for a new one
([format and rule](../../tools/naming/README.md)). `RenameTableTests` checks the result.
