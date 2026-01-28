<!--
Sync Impact Report
==================
Version change: 1.0.0 → 1.1.0
Modified principles:
  - I. Security-First: Added credential-less authentication requirement
Added sections: None
Removed sections: None
Templates requiring updates:
  - .specify/templates/plan-template.md ✅ (Constitution Check section compatible)
  - .specify/templates/spec-template.md ✅ (Requirements section compatible)
  - .specify/templates/tasks-template.md ✅ (Phase structure compatible)
Follow-up TODOs: None
-->

# No-Show Demo Constitution

## Core Principles

### I. Security-First (NON-NEGOTIABLE)

All code and cloud infrastructure MUST be secure by design. Secrets, credentials, API keys, and sensitive data MUST NEVER appear in:
- Source code (including comments and documentation)
- Version control history
- Log outputs or error messages
- Client-side bundles or exposed endpoints

**Requirements:**
- **Credential-less authentication is REQUIRED** when supported (managed identities, workload identity federation, service principals with federated credentials, DefaultAzureCredential, etc.)
- Use environment variables, secret managers, or secure vaults ONLY when credential-less auth is not available
- Implement least-privilege access for all services and users
- Validate and sanitize all inputs at system boundaries
- Enable audit logging for security-relevant operations
- Conduct threat modeling for new features involving data or authentication

**Rationale:** Security breaches destroy user trust and can have legal/financial consequences. Prevention is non-negotiable.

### II. Expert Coding Standards

All code MUST adhere to established software engineering principles:

**SOLID Principles:**
- **S**ingle Responsibility: Each module/class/function has one reason to change
- **O**pen/Closed: Open for extension, closed for modification
- **L**iskov Substitution: Subtypes must be substitutable for their base types
- **I**nterface Segregation: Many specific interfaces over one general-purpose
- **D**ependency Inversion: Depend on abstractions, not concretions

**Additional Standards:**
- **DRY** (Don't Repeat Yourself): Extract common logic; no copy-paste code
- **KISS** (Keep It Simple, Stupid): Prefer straightforward solutions
- **YAGNI** (You Ain't Gonna Need It): Build only what is currently required
- **Composition over Inheritance**: Favor object composition for flexibility

**Rationale:** These principles reduce technical debt, improve maintainability, and enable safe refactoring.

### III. Clarity Over Cleverness

Code MUST prioritize human readability and understandability over clever or terse solutions:

- Favor explicit over implicit behavior
- Use descriptive, meaningful names for variables, functions, and classes
- Prefer verbose clarity to cryptic brevity
- Break complex logic into well-named helper functions
- Avoid premature optimization that obscures intent
- Write code as if the next maintainer is a sleep-deprived version of yourself

**Rationale:** Code is read far more often than it is written. Maintainability and debuggability depend on clarity.

### IV. Self-Documenting Code First

Documentation strategy follows this priority order:

1. **Self-documenting code** (PRIMARY): Clear names, obvious structure, and readable flow
2. **Inline comments** (SECONDARY): Explain *why*, not *what*, when intent is non-obvious
3. **External documentation** (TERTIARY): Architecture decisions, API contracts, and complex algorithms

**Comment Guidelines:**
- MUST explain non-obvious business logic or domain rules
- MUST document public API contracts (parameters, return values, exceptions)
- MUST NOT state the obvious (no `// increment counter` for `counter++`)
- MUST be kept in sync with code changes

**Rationale:** Good code explains itself; comments that duplicate code become stale and misleading.

### V. Scientific Rigor for Data & ML

All data science and machine learning work MUST uphold scientific validity:

**Reproducibility:**
- Pin all dependency versions (exact versions, not ranges)
- Set and document random seeds for all stochastic processes
- Version datasets alongside code
- Use deterministic data splits (train/validation/test)

**Validity:**
- Document hypotheses before experimentation
- Use appropriate statistical methods for the data type and distribution
- Report confidence intervals and significance levels
- Guard against data leakage between train and evaluation sets
- Validate assumptions (normality, independence, etc.) before applying techniques

**Transparency:**
- Log all experiments with parameters and results
- Document negative results and failed approaches
- Maintain clear lineage from raw data to final outputs
- Explain model limitations and failure modes

**Rationale:** ML systems can silently fail or encode bias. Scientific discipline prevents costly errors and maintains trust.

## Security Requirements

All implementations MUST satisfy these security constraints:

| Category | Requirement |
|----------|-------------|
| Secrets Management | No hardcoded secrets; use environment variables or secret stores |
| Authentication | Implement proper auth flows; no security through obscurity |
| Authorization | Enforce least-privilege; validate permissions at every boundary |
| Data Protection | Encrypt sensitive data at rest and in transit |
| Input Validation | Sanitize and validate all external inputs |
| Dependency Security | Regularly audit and update dependencies; monitor CVEs |
| Logging | Log security events; never log sensitive data |
| Error Handling | Fail securely; do not expose internal details in errors |

## Infrastructure Standards

All Azure infrastructure MUST follow these standards:

### Resource Naming Convention

Azure resources MUST use abbreviation prefixes from the [Cloud Adoption Framework](https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations).

**Format**: `{prefix}-{project}-{environment}-{region}-{instance}`

**Required Abbreviations** (relevant to this project):

| Resource Type | Prefix | Example |
|---------------|--------|---------|
| Resource Group | `rg` | `rg-noshow-dev-ncus-001` |
| AI Foundry Account | `aif` | `aif-noshow-dev-ncus-001` |
| AI Foundry Project | `proj` | `proj-noshow-dev-ncus-001` |
| Azure ML Workspace | `mlw` | `mlw-noshow-dev-ncus-001` |
| Azure SQL Server | `sql` | `sql-noshow-dev-ncus-001` |
| Azure SQL Database | `sqldb` | `sqldb-noshow-dev-ncus-001` |
| Static Web App | `stapp` | `stapp-noshow-dev-ncus-001` |
| Container Registry | `cr` | `crnoshowtestncus001` (no hyphens) |
| Storage Account | `st` | `stnoshowtestncus001` (no hyphens, max 24 chars) |
| Key Vault | `kv` | `kv-noshow-dev-ncus-001` |
| Log Analytics | `log` | `log-noshow-dev-ncus-001` |
| Application Insights | `appi` | `appi-noshow-dev-ncus-001` |
| Managed Identity | `id` | `id-noshow-agent-dev-ncus-001` |

**Rationale**: Consistent naming enables quick identification of resource type, ownership, environment, and location. CAF abbreviations are industry standard.

## Development Workflow

### Code Review Requirements

Every change MUST be reviewed for:
- [ ] **Security**: No secrets, proper input validation, secure defaults
- [ ] **Clarity**: Readable code, meaningful names, appropriate comments
- [ ] **Standards**: SOLID, DRY, KISS compliance; no unnecessary complexity
- [ ] **Testing**: Adequate test coverage for the change
- [ ] **Documentation**: Self-documenting code; comments where needed

### Quality Gates

- All tests MUST pass before merge
- Security scanning MUST report no high/critical vulnerabilities
- Code coverage MUST not decrease without justification
- Static analysis MUST pass with no new warnings

### ML/Data Science Workflow

- Document hypothesis and expected outcomes before starting
- Use experiment tracking (MLflow, Weights & Biases, or similar)
- Maintain separation between exploration and production code
- Require peer review of statistical methods and evaluation metrics

## Governance

This constitution supersedes all other development practices in this repository. Violations MUST be addressed before code is merged.

**Amendment Process:**
1. Propose change with rationale in a dedicated PR
2. Obtain approval from project maintainers
3. Update constitution with proper versioning
4. Communicate changes to all contributors

**Versioning Policy:**
- MAJOR: Principle removal or incompatible redefinition
- MINOR: New principles, sections, or material expansions
- PATCH: Clarifications, wording improvements, typo fixes

**Compliance Review:**
- All PRs MUST include a constitution compliance checklist
- Periodic audits SHOULD verify adherence to principles
- Deviations MUST be documented with justification in the PR

**Version**: 1.1.0 | **Ratified**: 2026-01-28 | **Last Amended**: 2026-01-28
