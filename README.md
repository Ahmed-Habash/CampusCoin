# Campus Coin — first implementation phase

A student budgeting application built with C#, ASP.NET Core MVC, ASP.NET Core Identity, Entity Framework Core, SQLite, Bootstrap, custom CSS, JavaScript, and Chart.js. Requirements source: **CampusCoin End-to-End Web Solutions_SRS.pdf**, version 1.0.

## Run

Install the .NET 10 SDK, open this directory, and run:

```powershell
dotnet restore
dotnet run
```

Open **http://localhost:5080**. The development launch profile applies the initial migration and seeds default categories plus a sample student. The SQLite file is created in this directory. Stop the app before copying the database for a backup.

Local demo credentials:

- Email: `student@campuscoin.local`
- Password: `CampusCoin!2026`

Register a new account to try a clean, empty dashboard. Demo data is only seeded in Development when `SeedDemo` is true. There is no administrator account yet. Default production settings do not seed the demo account. Do not publish the development database or its known demo credentials.

For this Codex workspace, `Start-CampusCoin.ps1` also locates the workspace-local SDK automatically. On another computer it uses the installed SDK.

## Working features

- Registration, login, logout, hashed passwords, cookie sessions, and login lockout using Identity.
- Original responsive layout, public landing page with sitemap, mobile navigation, dark mode, larger text, keyboard focus, reduced-motion support, and chart data table.
- Personalized dashboard with monthly income, expenses, net balance, six-month charts, category breakdown, recent transactions, budget progress, and one rule-based saving suggestion.
- Add, edit, search, filter, and soft-delete transactions. Edits and deletes retain the previous record in an audit table.
- Edit the student profile with academic year, allowance baseline, and savings goal fields. The email remains the sign-in identity.
- Create weekly or monthly recurring transactions for allowances, subscriptions, and regular costs. Due entries are materialized automatically when opening the dashboard or transaction list; routines can be paused, resumed, edited, or removed without deleting existing entries.
- Ask Coin assistant with account-scoped answers about spending, categories, budgets, balances, savings goals, exact dates, and named months. It has an explainable local fallback and can use Gemini or OpenAI when a provider key is configured.
- Settings hub for theme, text size, reduced motion, data export, profile access, AI provider selection, and an optional personal API key encrypted with ASP.NET Core Data Protection.
- What-if Lens for previewing how a planned purchase changes the forecast balance without saving or changing account data.
- Money Lab (`/Insights`) with end-of-month spending forecasts, recent-average comparisons, savings-rate tracking, smart budget alerts, a spending pulse chart, check-in streaks, and earned badges.
- Savings goals with targets, optional deadlines, progress tracking, and quick deposits from the Money Lab screen.
- CSV import/export for transaction history. Imports validate dates and amounts, match existing categories, and create personal categories when needed.
- Installable PWA shell with a local manifest and service worker for faster repeat visits and mobile home-screen installation.
- Refreshed modern UI with a bento-style dashboard hero, responsive glass panels, gradient summary cards, purposeful entrance motion, floating assistant motion, and reduced-motion support.
- Default categories plus private personal category creation, renaming, and removal. Used categories cannot change type; active records must be reassigned before removing a category.
- Add, edit, and delete monthly category budgets, with unique category/month enforcement and near-limit (80%) and over-limit states.
- SQLite migration, SQL schema, default categories, and six months of sample data.

Amounts use USD as a phase-one display assumption, following the SRS example. All amounts are stored in integer cents to avoid binary rounding errors. Dates represent the student's chosen calendar date; month boundaries are inclusive at the start and exclusive at the next month. The displayed balance is the selected month's income minus expenses, not a bank balance or carried-forward account balance.

## How it fits together

- `Models/Entities.cs`: database entities and relationships. Identity owns password hashing and user credentials.
- `Data/ApplicationDbContext.cs`: Identity tables plus application tables, indexes, foreign keys, and positive-amount constraints.
- `Data/Migrations/`: generated initial migration and model snapshot; `schema.sql` is the corresponding SQL script. The app applies migrations automatically for this local student project.
- `Controllers/`: authorization, input validation, record ownership, and form handling. Every user-owned read/write checks the signed-in user's ID; forms never accept an owner ID from the browser.
- `ViewModels/Forms.cs`: explicit form fields, validation, and dashboard output. Database models are not bound directly to posted forms.
- `Services/DashboardService.cs`: monthly totals, six-month trends, spending groups, and budget calculations.
- `Views/`: server-rendered MVC Razor screens. Razor escapes user-entered text; the chart payload uses the default safe JSON encoder.
- `wwwroot/`: original styling and JavaScript. Bootstrap 5.3.8 and Chart.js 4.5.1 are vendored locally; Google Fonts is optional and has system font fallbacks.

All modifying requests use antiforgery validation. Category ownership is validated separately from transaction ownership. Deletion preserves financial audit snapshots; a history viewer is future work. Ask Coin is the first account-scoped AI feature; persistent monthly insight history remains future work.

## Ask Coin configuration

The app works without a provider key by using explainable local answer rules, including exact dates such as `2026-09-12` and named months such as `September 2026`. A signed-in user can choose Gemini or OpenAI and save a personal key from **Settings**; the key is encrypted server-side and is never displayed again. A server-wide provider can instead be configured before starting the app:

```powershell
$env:GEMINI_API_KEY = 'your-key-here'
$env:Gemini__Model = 'gemini-3.8-flash'
# Or use OpenAI instead:
# $env:OPENAI_API_KEY = 'your-key-here'
# $env:OpenAI__Model = 'gpt-4.1-mini'
dotnet run
```

Gemini is tried first when both keys exist, then OpenAI, then the local fallback. Keys are read only on the server and are never sent to the browser. The assistant sends only a compact summary of the signed-in user's own records and keeps answers advisory. See Google's [Gemini generateContent reference](https://ai.google.dev/api/generate-content) and OpenAI's [text generation guide](https://developers.openai.com/api/docs/guides/text) for the provider patterns.

## Next SRS phases

1. Password recovery with an actual email sender and a separate administrator login/control panel.
2. Dedicated monthly reports, daily/weekly summaries, date/source filters, PDF/image export, and persistent in-app notifications.
3. Saving tips based on historical comparisons, tip ranking, pin/dismiss/bookmark functions, notes, and optional sharing.
4. Optional AI categorization/monthly insights, user override, correction history, duplicate detection, and richer forecast models.
5. Student-authored final report, diagrams, full acceptance checklist, deployment, and the required MP4 walkthrough.

This is a first-phase foundation, not full SRS completion. The UI contains working destinations rather than placeholder links for unimplemented features. Production deployment still needs HTTPS/domain configuration, database backups, email delivery, protected configuration and data-protection keys, and a deliberate migration/release procedure.

## Validation

```powershell
dotnet build
```

The standard-library Python integration test exercises a running **disposable** development instance and inspects its SQLite database. Run that server on another port with its own database (use an absolute database path):

```powershell
$env:ConnectionStrings__DefaultConnection = 'Data Source=C:/temp/campuscoin-test.db;Foreign Keys=True'
dotnet run --urls http://localhost:5081
python tests/smoke.py http://localhost:5081 C:/temp/campuscoin-test.db
```

Use a fresh database for a reproducible test. See `VALIDATION.md` for the checks actually performed during this build.

## Learning and attribution

This is an AI-assisted starting implementation created with OpenAI Codex. Review, understand, adapt, and explain it before submission. The SRS requires students to acknowledge AI assistance and demonstrate their own understanding; this README is a developer handoff, not the final academic report.

Useful official references: [ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity), [MVC with EF Core](https://learn.microsoft.com/aspnet/core/data/ef-mvc/), [Chart.js](https://www.chartjs.org/docs/latest/). Bootstrap and Chart.js are MIT licensed; license notices are included in their vendored files.

### Latest interface update
The Orbit reskin applies one SharpLink-inspired editorial system across the public landing page, authentication screens, and signed-in pages: a seamless white navigation surface, oversized headlines, clean rounded modules, rich light accents, and a midnight/white/mint palette. Abstract books and light forms move gently with scrolling, while the Settings motion preference can disable animation. Buttons, fields, tables, charts, empty states, the AI assistant, dark mode, and mobile navigation use the same spacing and interaction rules. Password fields include Show/Hide controls, and new passwords need at least 8 characters with uppercase, lowercase, a number, and a symbol. Transaction rows adapt to the available card width instead of requiring horizontal scrolling.
