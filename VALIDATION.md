# Build validation — 24 September 2026

## Completed

- .NET SDK 10.0.401; ASP.NET Core Identity and EF Core 10.0.12.
- Build: **0 errors, 0 warnings**.
- Initial EF migration successfully applied to new SQLite databases; schema SQL generated from that migration.
- Restoring patched dependencies produced no NuGet vulnerability warnings. SQLitePCLRaw.bundle_e_sqlite3 is explicitly pinned to 3.0.5.
- **39 HTTP integration assertions passed** against a fresh disposable SQLite database. The test creates two independent accounts and checks authentication, CSRF, cross-user access, exact cent storage, validation, monthly budget uniqueness, progress recalculation, soft deletion, and audit snapshots.
- **49 HTTP integration assertions passed** after the profile and recurring transaction phase. New checks cover profile persistence, weekly recurrence materialization, schedule advancement, pause/resume, deletion, and recurring record ownership.
- **51 HTTP integration assertions passed** after the Ask Coin phase. New checks cover account-scoped assistant answers for two separate users, local fallback behavior, and assistant request validation.
- Browser inspection: landing page, login, populated dashboard, budget screen, and an empty month.
- Responsive review: desktop and 390 × 844 phone viewport; no page-level horizontal overflow in the checked dashboard.
- Mobile navigation opens and follows links; closed navigation is hidden from the accessibility tree.
- Dark-mode and larger-text controls exercised. Month selector verified to submit a full date and load the selected month.
- Chart.js charts rendered with real seeded data; no browser console errors in the final dashboard check.

## Limits

This is a first-phase functional build, not a completed SRS or production certification. Automated tests do not yet cover concurrency stress, every browser, email recovery, administrator features, imports, exports, or persistent insight history. Ask Coin is implemented with account-scoped local fallback answers for exact dates/months and optional Gemini/OpenAI provider integration. Accessibility was checked through semantic markup and visual interaction, not a formal audit.

The demo database is local and disposable. The source archive excludes databases, compiled output, the downloaded SDK, and package caches; a fresh database is created on first run. The test runner is `tests/smoke.py` and the requirements roadmap is in `README.md`.

## UI and password update

- Registration now accepts passwords of at least 8 characters; uppercase, lowercase, number, and symbol rules remain.
- 41 integration assertions passed, including server rejection of seven-character passwords and acceptance of eight-character passwords.
- Login and registration password fields have accessible Show/Hide toggles, including the confirmation field. Browser checks confirmed type changes between password and text.
- Transaction tables adapt to their containing card using container queries. At desktop width, the table measured 515px client/515px scroll; at a 390px phone viewport, 298px client/298px scroll. No horizontal table overflow in either check.
- Reviewed refreshed categories, dashboard rows, dark theme, and mobile layout. Category creation links now preselect income or expense appropriately.
- Build completed with zero warnings and zero errors.
- Ask Coin has a floating responsive panel, privacy copy, prompt suggestions, CSRF protection, and account-scoped answers; the refreshed indigo/navy UI was smoke-tested with the application build.
- Exact-date assistant checks returned the matching transaction for both ISO and natural-language date formats; the dashboard composition and motion pass were checked through a live authenticated page response.
- Final visual pass verified the public navigation, signed-in top navigation, Bootstrap navbar classes, private-category creation card removal, and three-colour theme variables in live page responses.
