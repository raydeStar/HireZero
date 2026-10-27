# HireZero: Context-First AI Employees
### Product Vision & Architecture Proposal
**Status:** Concept proposal | October 2026

## 1. Executive Summary

HireZero is an AI workforce platform designed to give independent professionals, entrepreneurs, and small businesses access to specialized digital employees.

Instead of providing a collection of generic AI chatbots, HireZero creates a coordinated team of agents that understand a company's business, retain relevant institutional knowledge, and execute meaningful work.

The platform is built around three principles:

1. **Context before conversation.** HireZero proactively learns about the company rather than requiring lengthy manual configuration.
2. **Persistent intelligence, temporary compute.** Each customer's agents maintain durable knowledge and identities without needing permanently running virtual machines.
3. **Outcomes over interactions.** Customers pay for useful work completed, not merely the ability to chat with AI models.

The long-term vision is to make capabilities normally reserved for businesses with dedicated teams accessible to individuals and small organizations.

---

## 2. The Core Problem: Context Is the Bottleneck

AI models are increasingly capable of performing sophisticated work, but meaningful business assistance depends on accurate context.

A newly created AI marketing employee doesn't know:

- What the company sells.
- Who its ideal customers are.
- Its brand personality and positioning.
- Its competitors and market.
- What projects are underway.
- Which marketing strategies have already been attempted.
- What constitutes a successful outcome.

Most AI products expect customers to supply this information through lengthy prompts, uploaded documents, or configuration screens.

This creates onboarding friction and forces users to repeatedly explain their business.

**HireZero's central opportunity is to make context acquisition an automatic, ongoing capability of the platform.**

Rather than asking the user to write a company briefing, HireZero researches the company and proposes one for their approval.

This doesn't eliminate configuration, but it changes the customer's job from *writing everything* to *correcting and approving what HireZero discovers*.

## 3. The Ideal Customer Experience

### Step 1: Identify the business

The user creates an account and enters:

- Business name.
- Website URL, if available.
- A brief statement about what they want help accomplishing.

If the website isn't known, HireZero can search for it using the company's name and general location or industry.

The user must confirm the correct business identity before information is imported.

### Step 2: Automatic company discovery

HireZero initiates a limited research workflow.

It examines available public sources, potentially including:

- Official company website.
- About, services, products, and pricing pages.
- Public social media profiles.
- Publicly available customer reviews and testimonials.
- Relevant competitors and industry information.
- Blog articles and other published company materials.

This discovery process produces a structured company profile rather than simply storing large amounts of scraped website text.

**Example extracted company context**

| Category | Information |
|---|---|
| Business | Independent digital marketing agency |
| Services | SEO, content strategy, local advertising |
| Primary customers | Regional service businesses |
| Brand voice | Professional, approachable |
| Geographic market | Pacific Northwest |
| Main competitors | Three locally relevant agencies |
| Potential opportunities | Inconsistent content publication, weak service landing pages |

These are illustrative results. Actual findings must be grounded in retrieved sources, not assumed.

Each important fact should include its source, discovery date, and confidence or verification status.

### Step 3: Review the company's knowledge

HireZero presents a concise, editable company brief.

The user can confirm, correct, or remove findings.

For example:

> **HireZero:** I found your website and identified your three main services. Your published content suggests you primarily serve regional businesses. Is that correct?

The customer can confirm the analysis or clarify their actual target market.

This is important because a website may contain outdated information, and publicly available material may not represent current business priorities.

Corrections become authoritative company context.

### Step 4: Assemble an AI team

HireZero recommends employees based on the company's needs.

For the initial marketing-focused version, those might include:

**CEO / Coordinator**
- Understands the company's goals and priorities.
- Coordinates specialized employees.
- Tracks assignments and outstanding decisions.
- Presents recommendations to the owner.

**Marketing Specialist**
- Researches markets and competitors.
- Identifies marketing opportunities.
- Prepares campaigns and content.
- Analyzes performance when authorized data is available.

**Developer / Technical Specialist**
- Reviews the company's website.
- Investigates technical issues.
- Creates or updates approved software assets.
- Proposes automation and integration opportunities.

Each employee receives relevant company knowledge, its own instructions, assigned tools, permissions, and task history.

Employees share an approved company-level knowledge base while maintaining distinct working contexts.

### Step 5: Demonstrate immediate value

The first interaction should produce something useful, not merely announce that setup is complete.

For example:

> **Marketing Specialist:** I've reviewed your website and identified three potential improvements to your service pages. I've also prepared a draft campaign targeting the customer segment you confirmed during onboarding. Would you like to review it?

The customer sees evidence that HireZero understands the business before investing significant time configuring employees.

This first result should remain appropriately modest. It must distinguish verified observations from hypotheses and avoid promising outcomes unsupported by evidence.

---

## 4. The Context Engine

The Context Engine is a proposed core component of HireZero.

Its purpose is to acquire, structure, maintain, retrieve, and distribute business knowledge.

### Layer A: Public Business Intelligence

Automatically gather permitted public information about the company.

Possible inputs:
- Company websites and public documentation.
- Search results.
- Public product catalogs and service descriptions.
- Public reviews.
- Relevant public competitor information.

The system should respect website access restrictions, applicable terms, rate limits, and privacy requirements.

Public website content is untrusted input. Instructions embedded in retrieved pages must never override system instructions or agent permissions.

### Layer B: Private Business Knowledge

As customers become comfortable with HireZero, they can connect additional information.

Examples:
- Google Drive documents.
- Project management tools.
- CRM records.
- Customer-provided brand guidelines.
- Business analytics.
- Approved email and calendar integrations.

These connections require explicit authorization.

Each integration receives carefully scoped access, and private records remain isolated to the customer's organization.

### Layer C: Organizational Memory

Store durable information such as:
- Company profile and approved facts.
- Goals and preferences.
- Brand guidelines.
- Ongoing projects.
- Completed work.
- Important decisions.
- Employee responsibilities.
- Customer corrections and feedback.

Use structured records for stable business facts, document storage for longer source materials, and retrieval indexes for relevant reference information.

Avoid endlessly appending entire conversations to every model prompt.

### Layer D: Task-Specific Context

Before an employee begins a task, HireZero assembles only the context it needs.

For example, a marketing employee preparing a campaign might receive:

1. The company's approved positioning.
2. Its current marketing goals.
3. Relevant customer segments.
4. Previous campaign decisions.
5. Connected performance data, if authorized.
6. The specific task instructions.

A developer employee working on the same company's website would receive a different context package.

This approach reduces unnecessary tokens, improves relevance, and limits accidental exposure of unrelated information.

### Layer E: Context Maintenance

Company knowledge should not remain frozen after onboarding.

HireZero can periodically check approved public sources for meaningful changes and refresh connected information when appropriate.

Changes should be classified as:
- Verified updates.
- Possible changes needing confirmation.
- Conflicting information.
- Outdated records.

Important customer-approved facts should not be silently overwritten by scraped content.

**Long-term differentiation:** HireZero becomes more useful as it accumulates verified knowledge of a company's goals, systems, workflows, and decisions.

---

## 5. Infrastructure: Persistent Intelligence Without Permanent VMs

A dedicated always-running virtual machine for every customer creates unnecessary fixed costs, particularly for an affordable subscription product.

HireZero should separate persistent customer state from temporary execution environments.

### Shared Platform Services

A shared backend manages:

- Authentication and organizations.
- Agent identities and configurations.
- Persistent context and memory.
- Task scheduling.
- Job queues.
- Usage tracking and billing.
- Notifications.
- Approval workflows.
- Integration management.

### Persistent Data Layer

Each organization receives logically isolated storage for business knowledge, documents, conversations, employee configurations, and task history.

Tenant-level permissions must be enforced at the database, storage, retrieval, and tool-execution boundaries.

### On-Demand Execution

Agents execute work using the least expensive environment compatible with the task.

**Execution type A: Lightweight API tasks**

Examples:
- Analyzing company information.
- Drafting marketing materials.
- Reading approved documents.
- Preparing recommendations.

These jobs can execute through shared application workers without provisioning a customer VM.

**Execution type B: Isolated sandbox tasks**

Examples:
- Running generated scripts.
- Executing arbitrary code.
- Using browser automation.
- Processing potentially untrusted files.
- Performing developer-oriented operations.

HireZero provisions a temporary isolated sandbox, provides scoped task data, executes the work, persists approved outputs, and releases the compute resources.

**Execution type C: Premium long-running environments**

Some customers may require extended browser sessions, large development projects, or unusually intensive workflows.

These capabilities can be offered separately through usage-based charges or higher-priced plans.

### The Execution Lifecycle

1. A user or scheduler creates a task.
2. HireZero identifies the required agent and relevant company context.
3. The platform checks the task's permissions and budget.
4. A suitable execution environment is selected.
5. The agent performs its work within tool, time, and spending limits.
6. Outputs and durable state are persisted.
7. Temporary computing resources are released.
8. The customer receives results or an approval request.

**The customer's AI employees remain persistent as software identities, even when no dedicated computer is running.**

Scheduled execution and durable task queues provide the experience of ongoing employment without requiring permanently active customer machines.

---

## 6. AI Cost Management

Affordable pricing requires deliberate resource control from the beginning.

HireZero should implement:

**Model routing:** Use economical models for routine extraction, classification, and drafting. Reserve higher-cost reasoning for tasks that benefit from it.

**Context optimization:** Retrieve only necessary information instead of sending entire company histories into every request.

**Structured processing:** Use deterministic code for straightforward operations such as calculations, formatting, scheduling, and data transformation.

**Usage allowances:** Establish monthly quotas for AI tasks, compute time, or platform credits.

**Task-level budgets:** Prevent agents from exceeding approved token, tool-call, runtime, or spending limits.

**Optional customer API keys:** Allow suitable customers to provide their own model API credentials under a clearly defined billing and security model.

**Tiered execution:** Reserve expensive autonomous workflows and long-running sandboxes for plans that can support their costs.

For a bootstrapped launch, avoid unlimited free agent execution. A low-cost interactive preview, limited public-context analysis, paid founding-member pilot, or BYOK configuration may be more financially sustainable.

Pricing and quotas should be based on actual measured job costs, including retries, retrieval, tools, storage, and operational overhead.

---

## 7. Initial Market Positioning

HireZero should not initially position itself as a universal AI company that can do anything.

That promise is difficult to communicate, validate, and fulfill.

Instead, the first target market should be narrow enough to provide a recognizable set of outcomes.

**Proposed initial market:** Independent marketing professionals and agencies with approximately one to five employees.

**Proposed positioning:**

> HireZero gives your business an AI-powered support team that already understands your company, helps manage recurring work, and produces useful deliverables without the complexity of hiring or configuring an entire team.

The initial offering might focus on:

- Company and competitor research.
- Campaign preparation.
- Website reviews.
- Client report preparation.
- Content planning.
- Opportunity identification.
- Recurring marketing operations.

The broader multi-agent platform remains the long-term vision, but early customer acquisition focuses on one repeatable set of problems.

---

## 8. MVP Scope

### Phase 1: Context-First Onboarding

**Goal:** Demonstrate useful business understanding within minutes.

Build:
- Business identification and website confirmation.
- Bounded website discovery.
- Structured company-profile extraction.
- Source attribution.
- Human review and corrections.
- Persistent company knowledge.
- One marketing employee.
- One immediately useful research or marketing deliverable.

Avoid building:
- Dedicated customer VMs.
- A large employee marketplace.
- Open-ended autonomous execution.
- Complicated multi-agent meetings.
- Broad integrations.

**Validation target:** Prospective customers can onboard with minimal assistance and recognize the resulting business brief and first deliverable as useful.

### Phase 2: Persistent Employee Workflows

Add:
- Scheduled jobs.
- Durable task history.
- Multiple specialized agents.
- Shared organizational context.
- A small set of approved integrations.
- Usage metering and billing.
- Human approval for consequential actions.

**Validation target:** Customers return because HireZero completes recurring work, rather than because the initial demonstration was entertaining.

### Phase 3: Advanced Autonomous Capabilities

Add:
- Browser automation.
- On-demand isolated compute.
- Developer workflows.
- Cross-agent task delegation.
- Larger integration ecosystem.
- Advanced memory maintenance.
- Premium execution options.

**Validation target:** Customers willingly pay more for measurable additional work completed.

---

## 9. Primary Risks

### Inaccurate automatic context

Public information may be outdated, incomplete, or associated with the wrong business.

**Mitigation:** Source attribution, confidence labeling, explicit business confirmation, customer corrections, and a distinction between verified facts and AI-generated suggestions.

### AI cost overruns

Unbounded agents can consume far more resources than expected.

**Mitigation:** Job budgets, quotas, conservative defaults, cost instrumentation, and expensive-feature gating.

### Security and privacy

Customer files, credentials, and connected accounts may contain sensitive information.

**Mitigation:** Tenant isolation, encryption, scoped credentials, audit logs, retention controls, deletion support, sandboxing, and explicit approval for sensitive operations.

### Weak differentiation

Users may see HireZero as another interface around existing AI models.

**Mitigation:** Focus on durable business context, relevant integrations, repeatable workflows, and dependable completed work rather than generic conversational abilities.

### Onboarding novelty without retention

A personalized business analysis may be impressive once but insufficient to justify ongoing subscription payments.

**Mitigation:** Validate a recurring job customers value before expanding the agent architecture.

### Excessive product scope

Trying to provide a fully autonomous digital corporation from day one creates unnecessary engineering, support, and reliability demands.

**Mitigation:** Launch one excellent role and workflow, then expand only when customers demonstrate demand.

---

## 10. Early Success Metrics

Initial experiments should measure:

| Metric | What it tells us |
|---|---|
| Correct business identification rate | Whether automatic discovery is trustworthy |
| Time to approved company profile | Whether onboarding actually removes friction |
| Percentage completing first useful task | Whether customers reach meaningful value |
| Corrections per generated company brief | Whether research quality is sufficient |
| AI and compute cost per completed task | Whether the business can support its pricing |
| Repeat task usage | Whether customers value recurring assistance |
| Paid conversion and early retention | Whether users will pay beyond initial curiosity |

A practical first experiment would involve approximately ten independent professionals or small agencies.

Observe their onboarding sessions, measure how much correction is required, and identify the first recurring task they would pay HireZero to perform.

Avoid extensive infrastructure investment until that experiment demonstrates meaningful demand.

---

## 11. Long-Term Vision

HireZero can evolve into a platform where customers assemble small digital teams that understand their businesses and coordinate useful work.

Over time, individual employees could specialize in marketing, development, operations, research, customer support, and administration.

A coordinating agent would allocate tasks, surface important decisions, and maintain alignment with organizational priorities.

The company's shared knowledge would become more complete through approved integrations, completed work, and direct customer feedback.

Compute would remain elastic, allowing HireZero to support many small customers without assigning dedicated always-running infrastructure to every account.

The defining experience should be simple:

**A new customer joins HireZero and discovers that their AI employees have already done the homework necessary to begin helping.**

The customer remains in control of decisions, permissions, and spending.

HireZero provides the research, memory, coordination, and execution needed to help a small organization operate with greater capability.

### Guiding Product Principle

**Don't make customers configure an AI workforce before it can help them. Give them a team that learns their business, proves its usefulness quickly, and earns greater responsibility over time.**