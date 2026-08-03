# Security Policy

## Supported Versions

Security patches and fixes are provided for the active branches:

| Branch | Status |
| :--- | :--- |
| `master` | ✅ Latest stable — actively maintained. |

> Older versions will **not** receive security updates. Users are strongly encouraged to upgrade to the latest stable release.

---

## Reporting a Vulnerability

If you discover a potential security vulnerability, **do not open a public issue**.

Instead, report it privately through **GitHub Security Advisories** — use the **Security** tab in the [repository header](https://github.com/ppn-systems/Zalo.Net/security) to create a private advisory.

For more information about private vulnerability reporting, see [GitHub's documentation](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability).

---

## Disclosure Process

| Step | Action | Timeline |
| :---: | :--- | :--- |
| 1 | Report acknowledged. | Within **48 hours**. |
| 2 | Issue reviewed and validated by maintainers. | — |
| 3 | Patch prepared on a private branch. | — |
| 4 | Fix released publicly; **CVE ID** assigned if applicable. | — |
| 5 | Reporter credited (unless anonymity requested). | — |

---

## Security Best Practices

When using Zalo.Net packages in production:

- Always use the **latest version** of `Zalo.Net` NuGet packages.
- Keep Zalo App credentials, Secret Keys, Access Tokens, and Refresh Tokens secure (never hardcode in source code or check into source control).
- Store API credentials in secure environment variables or secret vaults.
- Validate all inputs, verify webhook signatures (PKCE, OA/Zalo signature verification), and enforce HTTPS/TLS when communicating with Zalo VNG endpoints.

---

## Contact

For questions about this policy or secure usage of Zalo.Net libraries, open a [private security advisory](https://github.com/ppn-systems/Zalo.Net/security) or start a [GitHub Discussion](https://github.com/ppn-systems/Zalo.Net/discussions).
