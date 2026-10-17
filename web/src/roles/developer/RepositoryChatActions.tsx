import { useEffect, useState } from "react";
import { api, ApiError } from "../../shell/api";
import type { ConnectionProposal } from "./github";
import { PullRequestChatActions } from "./PullRequestChatActions";
interface RepositoryChatActionsProps {
  requestId: string;
  messageCount: number;
  onForbidden: () => void;
}
export function RepositoryChatActions({ requestId, messageCount, onForbidden }: RepositoryChatActionsProps) {
  const [proposals, setProposals] = useState<ConnectionProposal[]>([]),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  function fail(cause: unknown) {
    setError((cause as Error).message);
    if (cause instanceof ApiError && cause.status === 403) onForbidden();
  }
  useEffect(() => {
    let active = true;
    api<ConnectionProposal[]>("/github/chat/" + requestId)
      .then((items) => {
        if (active) setProposals(items);
      })
      .catch(fail);
    return () => {
      active = false;
    };
  }, [requestId, messageCount]);
  async function confirm(proposal: ConnectionProposal, repositoryId: number) {
    setBusy(true);
    setError("");
    try {
      await api("/github/chat/" + requestId + "/confirm", "POST", { proposalId: proposal.id, repositoryId });
      setProposals(await api<ConnectionProposal[]>("/github/chat/" + requestId));
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <PullRequestChatActions requestId={requestId} messageCount={messageCount} onForbidden={onForbidden} />
      {(proposals.length > 0 || error) && (
        <section className="fe-section dev-chat-connection" aria-label="Repository settings">
          {error && (
            <p role="alert" className="fe-alert">
              {error}
            </p>
          )}
          {proposals.map((proposal) => {
            const automatic = proposal.action === "enableAutomatic",
              manual = proposal.action === "disableAutomatic";
            const label = automatic
              ? "Enable review observation:"
              : manual
                ? "Require confirmation for each local review fix:"
                : "Connect the verified GitHub repository:";
            const button = automatic
              ? "Confirm review observation"
              : manual
                ? "Confirm manual fixes"
                : "Confirm connection";
            const completed = automatic
              ? "Review observation enabled; diagnosis and repairs still need scoped approval."
              : manual
                ? "Local review fixes now require confirmation."
                : "Repository connected. Review fixes wait for confirmation.";
            return (
              <div key={proposal.id}>
                {proposal.confirmed ? (
                  <p role="status">{completed}</p>
                ) : (
                  <>
                    <p>{label}</p>
                    {(automatic || manual) && (
                      <p className="fe-muted">
                        Applies to your PRs in this checkout. Git publication and deployment remain disabled.
                      </p>
                    )}
                    {proposal.choices.map((choice) => (
                      <div className="fe-list-row" key={choice.id}>
                        <span className="fe-list-main">
                          <strong>{choice.name}</strong>
                          {(automatic || manual) && <small>{choice.rootPath || "No local checkout selected"}</small>}
                        </span>
                        <button disabled={busy} onClick={() => void confirm(proposal, choice.id)}>
                          {button}
                        </button>
                      </div>
                    ))}
                  </>
                )}
              </div>
            );
          })}
          <a className="fe-link" href="/?pane=work&section=reviews">
            Open code reviews →
          </a>
        </section>
      )}
    </>
  );
}
