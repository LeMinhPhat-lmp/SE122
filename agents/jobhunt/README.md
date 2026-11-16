# JobHunt reference agents

These four registration-ready cards describe deterministic fixture agents. They
use no LLM and do not implement agent logic; weeks 10–11 can build the runtime
behind these frozen contracts.

## Catalogue

| Agent | Capability |
|---|---|
| Job Search Agent | `search_jobs` |
| CV Analysis Agent | `analyze_cv` |
| Company Research Agent | `research_company` |
| Recommendation Agent | `recommend_jobs` |

All schemas are inline, Draft 2020-12, and reject unknown properties.
Examples in `examples/` contain input objects only, except `*.output.json`,
which contains a representative output.

## Call graph

The graph is documentation only; AERS does not enforce it:

```text
caller
  -> Recommendation Agent
       -> Job Search Agent
            -> Company Research Agent
       -> CV Analysis Agent
```

The longest chain is client → Recommendation → Job Search → Company Research
(depth 2). The cycle demonstration is Company Research calling Recommendation;
the depth demonstration uses a configured low maximum.

## Registration

Set `REGISTRATION_KEY` and send [register-all.http](register-all.http), or use
curl:

```bash
curl -X POST "$AERS_URL/agents/register" \
  -H "X-Registration-Key: $REGISTRATION_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @agents/jobhunt/job-search.agent-card.json
```

Repeat with the other three card files. A successful server returns HTTP 201.
Then `GET /agents?domain=jobhunt` lists the cards and their capabilities.

For a manual task-input check, use a registered target API key with the
HubProbe harness:

```bash
dotnet run --project src/AERS.HubProbe -- call \
  --url "$AERS_URL" --key "$CALLER_KEY" --target "$JOB_SEARCH_AGENT_ID" \
  --capability search_jobs \
  --input "$(cat agents/jobhunt/examples/search_jobs.valid.json)"
```

Repeat with each capability's valid example. Sending an invalid example must
return `VALIDATION_ERROR` at `/payload/input`.

## Examples

`search_jobs` accepts `keywords` and optional `location`/`maxResults` and
returns `jobs` plus `total`. `analyze_cv` accepts `cvText` and an optional
`jobDescription` and returns a bounded score with matched and missing skills.
`research_company` returns a deterministic company profile. `recommend_jobs`
combines CV text and keywords into ranked recommendations. The fixture data is
deterministic so examples are stable in demos and contract tests.

## Output validation

The current AERS baseline does not validate `outputSchema`; the task still
completes and there is no `OUTPUT_INVALID` failure contract. This is the
suggested **validate and warn** option from ADR-W9-02, marked PROVISIONAL until
the ADR is accepted and the output-validation hook is delivered.

## Stable identity

Long-running agents persist `{agentId, apiKey}` through the B-03
`ICredentialStore` and register again only after a 401. Suggested credential
keys are `jobhunt-job-search`, `jobhunt-cv-analysis`,
`jobhunt-company-research`, and `jobhunt-recommendation` (ADR-W9-03,
PROVISIONAL).
