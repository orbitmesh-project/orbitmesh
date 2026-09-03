# Variables

![Variables page - a name/value list, secrets hidden behind Reveal](/screenshots/variables.jpg)

Named values (Console → Variables) that any package's settings can reference with a `{Name}` token, anywhere in a value - including inside a JSON setting. Works whether a package exposes a flat `Latitude` setting or buries it inside a larger config blob.

Change a Variable once. Every package referencing it picks up the change - no per-package edits.

## Secrets

A Variable can be marked secret. Its value is encrypted at rest with the same cipher used for Machine credentials. The Console never shows a secret's value in the list - only behind an explicit "Reveal".

## When substitution happens

Only at delivery, when settings reach a connected package. Never when the Console reads or writes them. A `{Name}` token stays as literal text in storage. Editing a package's settings can't accidentally detach a value from the Variable it tracks.

See [Settings](/guide/sdk/settings) for the package-side view.
