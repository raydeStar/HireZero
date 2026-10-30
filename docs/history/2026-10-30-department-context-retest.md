# Department context counts as approved: fix and retest

The [internal Home weekly plan](2026-10-30-native-home-walkthrough.md) listed only the two-sentence business brief as
"Owner-approved facts used". It also left out what the employee's approved department context provided: the free
early-access offer, the product's website and the point that it cites sources or says it does not know. The goal was to
learn whether someone uses the product for a real task, yet no post said how to try it, two posts never said what
HireZero is, and the learning plan counted intended tasks together with actual use.

Core's instructions described the business brief (approved), website pages (reference only), documents and memory, but
never `departmentKnowledge`. That field reaches a request only after the owner approves it, but the model was not told so.

## Change

- `CompanyStore.DepartmentKnowledgeInstructions` tells the model that department context is owner-approved company
  context. Unknowns stay unknown. A claim the context leaves for the owner to confirm may be used as stated, flagged for
  confirmation. An owner correction in the business brief wins over a conflicting statement.
- Only deliverable packets carry the sentence, just before the business brief's provenance. Their packet allowance is
  24 KiB. General work has 4 KiB and would lose about a tenth of that (381 bytes); two revision tests near that limit
  failed when the sentence was added everywhere. Conversations and context drafts are unchanged.

## Retest

The owner approved the fix and retest ("Yes, fix it"). The private iteration runner rebuilt two frozen requests with the
current starter text and the new sentence: the HireZero Home request and the fresh bookshop request, whose department
context is the generic setup text. Two samples per case went to Luna and to the DeepSeek competition stand-in on
OpenRouter. All eight completed on the first attempt with no retries. The four Luna requests reported 42,719 tokens; the
four DeepSeek requests cost $0.021.

A fresh blind grader scored them with five earlier drafts: the untouched Home plan, two Luna bookshop drafts from cycles
17 and 18, and two DeepSeek bookshop drafts from cycle 17. Grades come from a single grader, with A = 4.0 and A- = 3.7.

| Drafts | Offer connection | Learning signal | Average of 8 criteria |
| --- | ---: | ---: | ---: |
| Luna, HireZero, untouched Home plan | 2.30 | 2.00 | 3.18 |
| Luna, HireZero, new | 2.50 | 2.80 | 3.22 |
| DeepSeek, HireZero, new | 3.00 | 3.85 | 3.59 |
| Luna, bookshop, cycles 17–18 | 2.50 | 3.30 | 3.27 |
| Luna, bookshop, new | 2.15 | 3.00 | 3.14 |
| DeepSeek, bookshop, cycle 17 | 2.70 | 3.65 | 3.61 |
| DeepSeek, bookshop, new | 3.50 | 4.00 | 3.73 |

- Both DeepSeek HireZero drafts used the approved context. Each gave hirezero.app with the free, no-card offer, counted
  people who said they tried it on a real task, and set numeric next-week rules. Weak spots: one draft says "our" in two
  posts without naming the product; the other's comparison checklist favours its own features, and its third post
  works through the first post's brief.
- Both Luna HireZero drafts used the approved facts about sources, receipts and approval, and counted reported trials
  separately from task ideas. Neither gave the website or the free offer. One said the contact route was unknown and that
  the department context leaves its claims for the owner to confirm. "For my review" remains, and one draft speaks as
  HireZero itself.
- The bookshop drafts showed no change attributable to the sentence. The graded weaknesses were the earlier ones: thin
  tips, a repeated offer line, and first and third posts sharing a method. One DeepSeek bookshop draft promised a
  recommendation service the facts do not support; the same request used 19,960 output tokens and 336 seconds.

## Still open

- Luna still withholds the trial route. HireZero's department context marks its own claims "owner to confirm" and names
  the website only in passing. Confirming those claims, and naming the website as the place to start, would address
  this in company data without another instruction change; it has not been tested.
- Two samples per model and case, graded once, show direction rather than consistent quality. The owner's judgment of
  the Home plan remains the acceptance measure.

## Verification

- 203 tests across every class that reads model packets or instructions pass. New facts cover the sentence on a goal
  deliverable and its absence from a 4 KiB general work packet that carries department context.
- CSharpier is clean.
- Requests, raw drafts, receipts and grades stay in the ignored private pilot directory.
- The local installs do not yet include this change.
