# Which email to put in git commits

> **Última actualización:** 2026-10-21

## Answer

- No standard or standards body mandates a commit email. GitHub allows "your GitHub-provided `noreply` email address or any email address".[^1]
- Common practice, from vendor docs: noreply address for personal and public repos; employer address only for employer repos, switched per directory with `includeIf`.[^1][^4] The ranking is inference, not a stated standard.
- Set a neutral global `user.email` (noreply) so a forgotten override leaks nothing.

## Sourced facts

| Claim | Tier | Date | Status |
|-------|------|------|--------|
| Commits made before an email change keep the old email.[^1] | authoritative | undated | verified (page opened 2026-10-21) |
| Noreply format is `ID+USERNAME@users.noreply.github.com` for accounts created after 2017-07-18; the ID form survives a username change.[^2] | authoritative | undated | verified (page opened 2026-10-21) |
| "Block command line pushes that expose my email" checks only the most recent commit of a push.[^3] | authoritative | undated | verified (page opened 2026-10-21) |
| `includeIf` (`gitdir:`, `onbranch:`, `hasconfig:remote.*.url`) sets identity per directory or remote.[^4] | authoritative | page lists git 2.56.0, 2026-09-28 | ⚠️ verify: page opened, but the `includeIf` section was cut off |
| GitLab offers an auto-generated private commit email; a private profile alone does not hide the commit email.[^5] | authoritative | undated | ⚠️ verify: reported by a research pass, not opened |
| Bitbucket Cloud shows the raw commit email; a noreply request (BCLOUD-16952) is open.[^6] | authoritative | 2025-03-03 | ⚠️ verify: open status not confirmed |
| Rewriting history changes hashes and strips signatures; clones, forks and cached views can keep the old data.[^7] | authoritative | undated | ⚠️ verify: reported by two research passes, not opened |
| DCO `Signed-off-by` needs a known identity; the record is kept indefinitely. QEMU wants the sign-off email to match the Author field (QEMU's own policy).[^8] | authoritative | undated | ⚠️ verify: not opened; noreply may be rejected by DCO-enforcing projects |

## Weaker evidence

- Harvesting commit emails from public repos for phishing and marketing: one unreviewed 2019 paper (analyst) and 2022 forum posts (low).[^9] Direction supported, size unquantified.
- IP ownership of side projects follows the employment contract, state law and employer resources, not the commit email (Wharton working paper, 2024, analyst; law-firm blogs, low; mostly US).[^10] No source says a work email establishes ownership. ⚠️ verify: confidence Medium to Low.

## Open points

- Whether GitHub's "Verified" badge needs the signing key to match a verified account email: the two research passes disagreed. ⚠️ verify.
- Not found: a neutral survey of noreply vs real-address use; Bitbucket's own noreply guidance; any authoritative source on commit email as IP evidence.

---

[^1]: GitHub Docs, Setting your commit email address. https://docs.github.com/en/account-and-profile/setting-up-and-managing-your-personal-account-on-github/managing-email-preferences/setting-your-commit-email-address (opened 2026-10-21)
[^2]: GitHub Docs, Email addresses reference. https://docs.github.com/en/account-and-profile/reference/email-addresses-reference (opened 2026-10-21)
[^3]: GitHub Docs, Blocking command line pushes that expose your personal email address. https://docs.github.com/en/account-and-profile/setting-up-and-managing-your-personal-account-on-github/managing-email-preferences/blocking-command-line-pushes-that-expose-your-personal-email-address (opened 2026-10-21)
[^4]: git-config manual. https://git-scm.com/docs/git-config (page lists 2.56.0, 2026-09-28)
[^5]: GitLab Docs, User profile. https://docs.gitlab.com/user/profile/
[^6]: Atlassian, Git commits from unknown users in Bitbucket Cloud (2025-03-03). https://support.atlassian.com/bitbucket-cloud/kb/git-commits-from-unknown-users-in-bitbucket-cloud/ ; https://jira.atlassian.com/browse/BCLOUD-16952
[^7]: GitHub Docs, Removing sensitive data from a repository. https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository
[^8]: Developer Certificate of Origin. https://developercertificate.org/ ; Linux kernel, submitting patches. https://www.kernel.org/doc/html/next/process/submitting-patches.html ; QEMU code provenance. https://www.qemu.org/docs/master/devel/code-provenance.html/
[^9]: arXiv 1908.05354 (2019-08). https://arxiv.org/html/1908.05354v2 ; Hacker News threads 30787677 and 30977883 (2022).
[^10]: Wharton Mack Institute, The Legal Environment of Side Project Ownership (2024). https://mackinstitute.wharton.upenn.edu/wp-content/uploads/2024/03/Ren-Charlotte_The-Legal-Environment-of-Side-Project-Ownership-and-It-Innovation.pdf

---

## Historial de versiones

| Versión | Fecha | Cambios |
|---------|-------|---------|
| 1.0 | 2026-10-21 | Creación inicial. Four claims verified against opened pages; the rest flagged. |

## Generación

**Prompts usados:**
- `"industry standard recommendation for including actual work or personal email in git commits"`

**Herramientas:**
- `research-ops` with two parallel isolated agents, `web_search_exa`, `web_fetch_exa`
