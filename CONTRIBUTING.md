# Contributing to Zalo.Net

Thank you for considering contributing to Zalo.Net! It's people like you that make Zalo.Net a great library for interacting with Zalo API (VNG).

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Development Environment](#development-environment)
- [How to Contribute](#how-to-contribute)
- [Commit Convention](#commit-convention)
- [Coding Standards](#coding-standards)
- [Pull Request Process](#pull-request-process)
- [CI Quality Gates](#ci-quality-gates)
- [Release Automation](#release-automation)
- [Issue Reporting](#issue-reporting)
- [Architecture](#architecture)
- [Community](#community)

---

## Code of Conduct

This project and everyone participating in it is governed by the [Zalo.Net Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior via a [private security advisory](https://github.com/ppn-systems/Zalo.Net/security/advisories/new).

---

## Development Environment

### Prerequisites

| Tool | Version |
| :--- | :--- |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ |
| IDE | [Visual Studio 2026](https://visualstudio.microsoft.com/vs/) / [VS Code](https://code.visualstudio.com/) / [Rider](https://www.jetbrains.com/rider/) |
| [Git](https://git-scm.com/) | Latest |

### Getting Started

1. **Fork** the repository on GitHub.

2. **Clone** your fork locally:

   ```bash
   git clone https://github.com/<your-username>/Zalo.Net.git
   cd Zalo.Net
   ```

3. **Add upstream** remote:

   ```bash
   git remote add upstream https://github.com/ppn-systems/Zalo.Net.git
   ```

4. **Create a branch** for your work:

   ```bash
   git checkout -b feature/your-feature-name
   ```

5. **Build** the solution to verify everything compiles:

   ```bash
   dotnet build src/Zalo.Net.sln --configuration Release
   ```

---

## How to Contribute

### Workflow

1. Sync with upstream: `git pull upstream master`
2. Create a feature branch.
3. Make your changes.
4. Write or update tests as needed.
5. Run the test suite: `dotnet test tests/Zalo.Net.Tests.sln --configuration Release`
6. Commit with a [conventional message](#commit-convention).
7. Push to your fork and open a Pull Request.

### Types of Contributions

| Type | Description |
| :--- | :--- |
| Features | New functionality or capabilities for Zalo API interaction |
| Bug Fixes | Corrections to existing behavior or payload handling |
| Documentation | Improvements to docs, comments, or examples |
| Tests | New or improved test coverage |
| Code Quality | Refactoring, formatting, or cleanup |

---

## Commit Convention

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification for consistent commit history and automated versioning.

### Format

```text
<type>(optional-scope): <short description>

[optional body]

[optional footer]
```

### Commit Types

| Type | Purpose |
| :--- | :--- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `refactor` | Code change (no feature/fix) |
| `perf` | Performance improvement |
| `docs` | Documentation only |
| `test` | Adding or updating tests |
| `build` | Build system or dependencies |
| `ci` | CI/CD pipeline changes |
| `chore` | Maintenance (no production code) |
| `style` | Formatting or non-functional cleanup |
| `revert` | Revert a previous commit |

### Best Practices

- Keep the subject line short, imperative, and lowercase.
- Do not end the subject with a period.
- Use an optional scope to identify the affected package or area.
- Prefer one logical change per commit.

### Version Bump Rules

When release automation is enabled:

| Commit Type | Release |
| :--- | :--- |
| `fix` | Patch (`x.x.+1`) |
| `feat` | Minor (`x.+1.0`) |
| `!` or `BREAKING CHANGE:` footer | Major (`+1.0.0`) |

### Examples

```text
feat(auth): add PKCE support for Zalo OAuth 2.0 flow
fix(webhook): handle missing signature header in webhook payload
docs(readme): update OAuth documentation for Zalo OA
test(client): add unit tests for message sending endpoint
```

---

## Coding Standards

### C# Code Style

We follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) with additional guidelines:

- Use **4 spaces** for indentation (no tabs).
- Use `var` when the type is obvious; explicit type declarations otherwise.
- Use expression-bodied members when appropriate.
- Prefer pattern matching (`is`) over type checking and casting.
- Keep methods short and focused on a single responsibility.
- Write self-documenting code; avoid excessive comments.
- Full formatting rules are codified in [`.editorconfig`](.editorconfig).

### SOLID Principles

| Principle | Guideline |
| :--- | :--- |
| **S**ingle Responsibility | Each class should have only one reason to change. |
| **O**pen/Closed | Open for extension, closed for modification. |
| **L**iskov Substitution | Derived classes must be substitutable for their base classes. |
| **I**nterface Segregation | Prefer many client-specific interfaces over one general-purpose interface. |
| **D**ependency Inversion | Depend on abstractions, not concretions. |

---

## Pull Request Process

1. Ensure your code adheres to the [coding standards](#coding-standards).
2. Update documentation if your changes affect public APIs or behavior.
3. Include relevant tests for your changes.
4. Verify the PR passes all [CI quality gates](#ci-quality-gates).
5. A maintainer will review your PR and may request changes.
6. Once approved, your PR will be merged into the `master` branch.

---

## CI Quality Gates

Pull requests and pushes to `master` must pass the shared `_build.yml` workflow:

| Check | Description |
| :--- | :--- |
| `dotnet format --verify-no-changes` | Code formatting compliance |
| Release build | Full solution compilation |
| Test execution | TRX results with XPlat Code Coverage |
| Coverage artifacts | Cobertura format generation |

---

## Release Automation

Releases follow Conventional Commits and are driven from the `master` branch:

- `fix` commits → next **patch** release.
- `feat` commits → next **minor** release.
- `BREAKING CHANGE:` footers or `!` markers → next **major** release.

> **Tip:** Prefer one commit (or squash-merge result) that clearly communicates the highest-severity change so the version bump is unambiguous.

---

## Issue Reporting

When reporting issues, please use the provided templates and include:

1. A clear, descriptive title.
2. Steps to reproduce the issue.
3. Expected behavior.
4. Actual behavior.
5. Environment details (OS, .NET version, etc.).
6. Any relevant logs or screenshots.

---

## Architecture

Zalo.Net is designed as a **layered .NET SDK** for communicating with Zalo VNG APIs:

- **Nullable** enabled everywhere — never disable.
- Prefer `sealed` classes and `readonly struct`.
- Hot paths are high-performance and low-allocation.
- Secure token handling and cryptographically safe signature verification.

See [`CLAUDE.md`](CLAUDE.md) for full project rules and guidelines.

---

## Community

Join the conversation:

- [GitHub Discussions](https://github.com/ppn-systems/Zalo.Net/discussions)
- [GitHub Issues](https://github.com/ppn-systems/Zalo.Net/issues)

---

<p align="center">
  ❤️ Thank you for contributing to Zalo.Net!
</p>
