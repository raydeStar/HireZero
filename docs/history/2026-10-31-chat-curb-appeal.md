# Chat curb appeal (UX round 11)

The owner asked what could be done to make the workspace look better, especially the chat. Their real conversation
with Marketing showed the problems. At 1280×820:
- the header, a toolbar that wrapped to two rows and the composer left the messages less than half the window;
- the header said "Marketing · Marketing · private conversation";
- each answer was separated from its question by a "Reply status and usage · Done" row;
- one older reply showed its raw `[connect: google-ads]` tag;
- a company facts draft showed as a line of JSON;
- a cancelled reply read "Owner canceled; any late output is withheld.. Accounting or runtime reconciliation may be
  required; this turn cannot be replayed.";
- a work card's "Read result" button butted against its title.

## What changed

Display only: nothing saved, sent or authorized changes.
- **Header.** It shows the role, and the department only when it differs: "Chip / Marketing". The employee's
  responsibility is its tooltip.
- **Toolbar.** One row: the conversation picker on the left, then New conversation, Context & settings and an info
  button for How replies work. The cut-off responsibility line is gone from employee chats. When the toolbar itself is
  under 600px wide, the buttons show only their icons; their names stay for screen readers and tests. That covers a
  phone, and also a desktop chat squeezed by a wide cockpit. The first install keyed this to the screen width, and the
  owner's real layout still wrapped.
- **Messages.**
  - A reply carries the employee's avatar, name and a quiet "AI reply" tag.
  - Your messages are plain right-hand bubbles; "You" is kept for screen readers.
  - The composer is as wide as the messages above it.
- **Receipts.** A finished reply's details sit as one quiet "Usage and receipt" line under the reply it wrote. Before,
  they sat between the question and its answer. A reply that did not finish keeps its status line where it was.
- **Plain words.** A stopped reply says why in the owner's words ("You cancelled it…", "It expired before you sent
  it…") and always ends "This turn won't be sent again." Where usage may be unsettled it still says so.
- **Legacy text.** A saved `[connect: id]` tag is hidden; the server already strips it from new replies. A reply that is
  one flat JSON object of text, such as a company facts draft, reads as labelled fields.
- **Work cards.** A card's title in the chat has its own line.

The reply-text rules are in `web/src/company/replyText.ts`.

## Checks

- A focused unit test (`tests/conversation-reply-text.test.mjs`) covers:
  - hidden connect tags, and a reply that is only a tag staying as it was;
  - which bodies read as fields;
  - every stop reason ending "won't be sent again", with no doubled full stop and no internal codes.
- All 55 frontend unit tests pass.
- These headless Chrome journeys pass against the fake provider, with zero live model calls:
  - conversation replies, including pending, Stop and "Reply stopped." after reload;
  - company chat on desktop and mobile;
  - employee reviews.
- **Real data.** A copy of the real company, with every key and connection removed, ran on this checkout's build with no
  model connection. Its Marketing chat was checked before and after, in dark and light, at 1280×820, 1680×900 and on a
  375-wide phone.

## Not done

At 1280 wide the docked cockpit still takes about a third of the window beside the chat. Folding it away by default in
chat is a layout decision for the owner. It can already be closed by hand.

## One message box

After the first install the owner marked two things in their real chat. The Tools line sat alone at the far left,
away from the message box. Under the box, Attach, Options and "Review before spending" took a row of their own. They
asked for a box like other chat apps', with a + that drops the options down:
- The box holds the message, then a + on the left and the send button on the right.
- The + opens a menu upward over the conversation. The box never grows to show it. The menu holds:
  - Attach a file;
  - Tools;
  - the reply options: work type, message action, model, allowance and evidence;
  - a line saying that Prepare reply makes no model call and that the reply is reviewed before anything is sent or
    spent.

  It closes on a click outside it or on Escape. The + turns into a × while the menu is open.
- What a message will do is never hidden: a changed work type, or "Save only", shows as a chip beside the +. A file
  being read shows there too.
- **Tools.** The panel keeps its own recovery behaviour: it opens itself when an action needs a status check. Closed,
  it takes no room and is reached from the + menu. Open, it is a card in the message column just above the box. Its
  own header still closes it, and the conversation gives it room.
- Attached text is shown above the box.
- The Options and Tools panels, and the file picker, keep the names and behaviour the tests and screen readers rely on.

## Installed

At the owner's "Yes, install it", both local installs moved to 15bb2e9 and then to aa267c3 through the usual backup
routine. At their second "Yes, install it" they moved to 594e0a1, the message box, the same way:
- Only the web app was replaced. The host and every other file kept its installed bytes, and each install verified 165
  files.
- All 21 companies kept their work, connections, model connections, readings and schedules, and every audit chain is
  intact. Marketing's morning shift and the pilot's schedule still match their approvals.
- No model calls were made.

The first install showed the toolbar still wrapping beside the owner's wide cockpit. That led to the second.
