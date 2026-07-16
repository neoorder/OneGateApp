# Upload checklist

1. Merge the final `OneGate.Codex` files into the public `master` branch so the privacy policy URL resolves.
2. Confirm that these public URLs load without authentication:
   - `https://onegate.space`
   - `https://github.com/neoorder/OneGateApp/issues`
   - `https://github.com/neoorder/OneGateApp/blob/master/OneGate.Codex/PRIVACY.md`
   - `https://onegate.space/terms.html`
3. In `https://platform.openai.com/plugins`, create a **Skills only** submission.
4. Copy listing fields and starter prompts from `listing.md`.
5. Upload `dist/onegate-dapp-debug-skill-1.0.0.zip` as the final skill bundle. `dist/onegate-plugin-1.0.0.zip` is the complete local plugin bundle for installation and archival, not the skill-only portal upload.
6. Copy the five positive and three negative cases from `reviewer-tests.md` without adding or removing cases.
7. Copy `release-notes.md` into the release-notes field.
8. Select the verified NEO GLOBAL RESOURCES business identity in the submission form.
9. Select all countries and regions offered by the submission portal.
10. Submit and keep the public support issue tracker monitored for reviewer questions.

This release has no MCP server, remote authentication, demo credentials, domain verification challenge, or MCP tool annotations.
