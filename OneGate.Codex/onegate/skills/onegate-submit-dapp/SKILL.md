---
name: onegate-submit-dapp
description: Prepare, validate, and submit a public DApp, game, tool, or project listing request to OneGate through the canonical neoorder/OneGateApp GitHub issue workflow. Use when a user asks to list, publish, add, register, or submit a project to the OneGate catalog, wants a listing draft reviewed, or asks for a DApp 上架 or 上架申请.
---

# Submit a DApp to OneGate

Use one public GitHub issue per listing. Read [references/submission-fields.md](references/submission-fields.md) before collecting data or drafting the issue. If the live form is reachable, compare it with the bundled field reference and follow the live form when they differ.

## Prepare the request

1. Inspect the user's repository, manifest, documentation, and public site when available. Reuse verifiable public facts and URLs instead of asking for them again.
2. Track which values are observed, supplied by the user, or still unknown. Never infer contact information, URL ownership, audits, licenses, legal rights, or the submitter confirmations.
3. Ask only for missing required values. For a Game, also require the game-specific review fields described in the reference, even if the GitHub form currently renders them as optional.
4. Keep the form headings in English. Write values in the user's requested language; otherwise match the language of the supplied listing content.

## Validate before drafting

- Accept only an exact documented option for each choice field. Require at least one supported network and one wallet-integration choice.
- Require public HTTPS launch, website, and icon URLs. Check that required URLs resolve without authentication when network tools are available. Explain redirects or unreachable assets instead of silently replacing them.
- Prefer a square 512x512 PNG or WebP icon. Treat a different usable square size as a review note, not an automatic rejection.
- If no wallet connection is required, use that integration option and write `None` under wallet permissions and methods.
- Search existing OneGateApp issues by project name and launch URL before submission. Show likely duplicates and ask whether the user wants to update an existing request or proceed with a new one.
- Exclude private keys, seed phrases, credentials, tokens, unpublished API keys, private endpoints, and other secrets. If supplied material contains a secret, do not quote it into the draft or submit the issue; tell the user what category of data must be removed and recommend rotating exposed credentials when appropriate.

## Review and confirm

Build the exact title and Markdown body using the reference. Leave the three confirmation boxes unchecked until the user confirms them. Show the title and body to the user before any external write.

Do not create the issue until the user has authorized the public submission and explicitly confirmed all three statements from the form: the URLs are official or authorized, OneGate may reject or remove the listing, and the issue contains no secrets. A clear confirmation already given in the current conversation counts; do not ask twice. Check the three boxes in the final body only after that confirmation. If the user requested only a draft or review, stop after returning the draft.

## Submit

Create the issue in `neoorder/OneGateApp` with an available authenticated GitHub connector. If no connector can create issues, use authenticated GitHub CLI with the reviewed title and a body file. If neither route is authenticated, open or return the canonical issue-form URL and preserve the completed draft for the user; do not claim the request was submitted.

Do not retry after an ambiguous network failure until checking whether the issue was created. On success, return the issue URL and identify any optional fields that were omitted. Treat the request as submitted only when GitHub returns a stable issue URL.
