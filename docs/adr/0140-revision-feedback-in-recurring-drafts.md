# 0140 - Retain revision feedback when repeating accepted work

Status: accepted, 2026-10-27.

The recurring shortcut copied the original task input and approved output, but omitted the owner feedback that
produced an accepted revision. The next plan could therefore repeat an earlier mistake despite preserving the
finished example.

New recurring drafts now include the original request and the complete ordered revision feedback in editable
instructions. Later notes can explicitly replace earlier requirements; the shortcut does not summarize or infer
which requirements should disappear. The chain must be complete, acyclic, within sixteen tasks and confined to
the source employee and conversation. An invalid chain cannot seed the shortcut.

The existing 2,000-character schedule instruction limit remains. Oversized combined text stays intact in the
editor, with saving disabled until the owner consolidates it. A separate expandable copy of the source request
and feedback supports that review. Previously saved edits and schedule definitions are never rewritten.

Saving and approving the exact resulting schedule use the existing authority, context and finite-run workflow.
Accepted outputs remain separately selected references; feedback becomes an instruction only through the
owner's schedule review. No schedule is activated and no model call is made by following the shortcut.
