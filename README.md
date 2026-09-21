<p align="center">
  <img src="docs/images/fluxo-logo.png" alt="Fluxo logo placeholder" width="160">
</p>

# Fluxo

Fluxo is a local-first Windows personal finance app. Track accounts, record transactions, plan budgets, monitor goals, and review upcoming money events from one desktop workspace.

> **Screenshot placeholder:** Dashboard overview.

Current release: **1.0.6**

## Navigation

- [Building a release](#building-a-release)
- [Features](#features)
- [Getting started](#getting-started)
- [Major features](#major-features)
- [Privacy and storage](#privacy-and-storage)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Build and test](#build-and-test)
- [Troubleshooting](#troubleshooting)
- [Support](#support)
- [Release history](#release-history)

## Building a release

Use one version input for the bundle, MSI, application, and first-party DLL file versions:

```powershell
dotnet build Fluxo.Installer.Bundle/Fluxo.Installer.Bundle.wixproj -c Release -p:FluxoInstallerVersion=1.0.6
```

Release numbers are immutable, strictly increasing `major.minor.patch` values (MSI limits: 255, 255, 65535). Do not rebuild different public binaries under an existing version. CI rejects reused or older release tags. `Directory.Build.props` provides the local default; use `FluxoInstallerVersion` for overrides, including direct application publishing. Conflicting `Version`, `FileVersion`, or `FluxoMsiVersion` inputs fail the build.

Before distributing a build, verify both the published files and actual MSI contents:

```powershell
./Fluxo.Installer.Msi/Build/Test-PackageGuards.ps1 `
  -PayloadDirectory ./Fluxo.Installer.Msi/obj/Release/app-payload/win-x64 `
  -MsiPath ./Fluxo.Installer.Msi/bin/Release/en-US/Fluxo.Installer.msi `
  -ExpectedVersion 1.0.6
```

Successful verification reports matching release versions and packaged SHA256 hashes, followed by passing rejection checks for deliberately broken payloads. CI requires these checks and non-displaying assembly-load tests before uploading an installer. Manual binary replacement or bypassing this pipeline can defeat these safeguards.

## Features

### Setup and navigation

- Guided Quick Setup for identity, theme, preferences, accounts, budget allocation, recurring transactions, goals, and notifications.
- Customizable Quick Access for common creation, planning, account, settings, data, and update actions.
- Global Search for transactions, accounts, tags, goals, features, and settings.
- Dashboard, Analytics, Calendar, and Ledger pages.
- Keyboard shortcuts with an in-app Hotkeys overview.

### Accounts and money movement

- Cash, checking, credit, and saving accounts.
- Account balances, credit limits, spent credit, minimum payments, due dates, interest rates, default selection, pinning, and enable/disable state.
- Expenses, incomes, goal contributions, transfers, repayments, and budget reconciliation.
- Account reconciliation with optional reconciliation transaction logging.

### Transactions and organization

- Tags, notes, Needs/Wants/Invest categories, budget exclusion, and pinned transactions.
- Split transactions with nested child transactions.
- Bulk transaction entry with an editable validation queue.
- Live balance previews before a transaction is saved.
- Recurring transactions and installment transactions with end dates.
- Linked transactions, reversal, transaction history, debt/IOU flags, posted and unposted IOUs, and repayment modes.

### Views and analysis

- Dashboard period views: daily, weekly, monthly, allocation period, and all-time.
- Calendar date navigation with expenses, incomes, goal deadlines, recurring transactions, and payment totals.
- Ledger search, date ranges, filters, grouping, amount sorting, nested rows, transaction details, CSV export, bulk editing, and deletion.
- Analytics for income, expenses, net value, trends, category ratios, top tags, and goals created in a selected period.

### Budgeting and planning

- Needs, Wants, and Invest allocation percentages.
- Weekly, biweekly, monthly, quarterly, and yearly allocation periods.
- Spending limits, period starts, rollover policies, and overspend policies.
- Planning Report for testing income, expenses, recurring items, and allocation changes.
- Budget Forecast for projecting balances and budget usage with recurring items and planned purchases.

### Goals and recurring activity

- Saving goals with target amounts, current amounts, deadlines, progress, visibility, and reminder state.
- Goal contributions and recurring goal updates.
- Upcoming Events panel for future payments, recurring activity, and goal deadlines.

### Notifications and history

- Startup cards for overdue payments, overdue recurring transactions, overdue goals, budget thresholds, low balances, high credit usage, and daily allowance warnings.
- Process actions for overdue items, session-only clearing, floating notifications, and 24-hour snoozing.
- Session history with undo, redo, revert-to-history, and transaction/account history details.

### Data, privacy, and Windows integration

- Local SQLite storage with no cloud sync or bank-connection service.
- JSON data backup and restore with entity selection, append, overwrite, and conflict decisions.
- Automatic startup database copies with three-day retention.
- Windows startup registration, system-tray operation, close-to-tray behavior, UI password lock, and auto-lock presets.
- GitHub release checks and installer downloads.

## Getting started

1. Install Fluxo from the [latest GitHub release](https://github.com/Hokichi/Fluxo/releases/latest).
2. Open Fluxo. The installer checks for the .NET 10 Windows Desktop Runtime and can install it when missing.
3. Complete Quick Setup.
4. Open Quick Access or New Transaction to start recording activity.

> **Screenshot placeholder:** Quick Setup wizard.

Quick Setup can be run again from Settings. Add at least one account before entering account-backed activity or adjusting budget allocation.

## Major features

### Quick Setup and Quick Access

Quick Setup configures the initial display name, light or dark theme, preferences, accounts, allocation percentages, recurring transactions, saving goals, and notification settings. Theme changes are previewed immediately and saved when setup finishes.

Quick Access opens common actions:

- New Transaction.
- View Accounts.
- New Account.
- New Recurring Transaction.
- New Saving Goal.
- New Tag.
- Planning Report.
- Budget Forecast.
- Search Everything.
- Data Management.
- Lock Application.
- Run Quick Setup.
- Hotkeys.
- Check for Updates.

Choose **Edit** to show the complete catalog and select which tiles appear in the panel. Hidden tiles are dimmed while editing, and the selection is saved for future sessions. Actions that are temporarily unavailable remain visible but dimmed in normal mode and become fully visible while editing.

> **Screenshot placeholder:** Quick Access panel.

### Global Search

Press `Ctrl+F` or choose **Search Everything** from Quick Access to search across transactions, accounts, tags, saving goals, app features, and settings.

Results are grouped by type. Opening a result can show a financial record, navigate to a main page, start a common action, or open the matching settings section.

### Dashboard

Dashboard combines period totals, daily allowance, budget allocation, account cards, transaction activity, saving goals, notifications, and Upcoming Events.

Use the period controls to move through daily, weekly, monthly, allocation-period, and all-time views. Account cards can show balances, credit usage, and account-level differences. Collapsible cards keep the main workspace compact.

### Accounts and reconciliation

Create Cash, Checking, Credit, or Saving accounts. Credit accounts support account limits, spent credit, minimum payments, due dates, interest rates, and a source account for deductions.

Account detail supports balance or spent-credit editing, transfers, pinning, deletion, history, and report generation. Reconciliation can set a current balance or spent-credit amount and optionally log the difference as a Budget Reconciliation transaction.

### Transactions, transfers, and repayments

New Transaction records expenses or incomes. Depending on the mode, it can also create goal contributions, recurring items, installments, repayments, debt/IOU entries, or split transactions.

Transaction details support accounts, amounts, dates, categories, tags, notes, goals, budget exclusion, pinning, linked transactions, reversal, and child transactions. The balance update card previews the resulting account, category, and tag amounts before saving. Transfer Funds moves money between accounts without requiring a manual pair of entries.

Enable **Bulk Insert** while creating transactions to build a queue, review or edit each item, remove unwanted items, and save the valid queue together. Duplicate and incomplete entries are identified before the queue is persisted.

### Recurring transactions and installments

Recurring transactions support expenses, income, and goal updates on weekly, biweekly, or monthly schedules. Each item can have an account, category, tag, goal, budget-exclusion state, enabled state, and end date.

Installments use recurring timing and an end date to stop the series. Related recurring entries allow overdue items to be processed from Notifications.

### Saving goals

Goals track a name, target amount, current amount, deadline, visibility, and reminder state. A goal contribution updates both the goal and its source account. Goal updates can be created as one-time or recurring activity.

### Budget management

Budget Management divides activity into **Needs**, **Wants**, and **Invest**. Configure:

- Allocation percentages.
- Spending limit.
- Weekly, biweekly, monthly, quarterly, or yearly allocation period.
- Period start.
- Rollover: Ignore, Matching, or Pooled.
- Overspend: Ignore, Soft Debt, or Hard Stop.

Transactions can be excluded from budget calculations. Split transactions are represented as nested activity while budget totals avoid double-counting parent and child rows.

### Planning Report and Budget Forecast

Planning Report models income and expenses, including selected recurring items, then shows balance, allocations, usage, and overflow for Needs, Wants, and Invest.

Budget Forecast models account balances and budget usage over a selected period. Include or exclude recurring items and test planned purchases before recording them.

### Calendar and Upcoming Events

Calendar provides a month grid with previous-month, next-month, and Today navigation. Select any date to review expenses, incomes, goal deadlines, recurring transactions, spent totals, earned totals, goals due, and payments due.

Upcoming Events shows future payments, recurring activity, and goal deadlines from the Dashboard.

### Ledger

Ledger supports:

- Search and date-range selection.
- Type, account, category, and tag filters.
- Grouping by date, tag, account, type, or category.
- Ascending or descending amount sorting.
- Parent/child transaction expansion.
- Transaction detail and delete actions.
- Selection mode, bulk account/tag edits, and bulk deletion.
- CSV export.

### Analytics

Analytics accepts a date range of up to 31 days and can show expenses, incomes, or both. It reports total income, total expense, net value, period trends, Needs/Wants/Invest ratios, top spending tags, and goals created in the selected period.

### Notifications

Notifications are evaluated from current local data and enabled settings. Cards can identify overdue credit payments, overdue recurring transactions, overdue saving goals, budget thresholds, low account balances, high credit usage, and daily allowance warnings.

Process actions open the relevant transaction workflow. Clear All removes current cards from the session. Snooze suppresses notification evaluation for 24 hours. Notification types and warning thresholds are configurable.

### History and reversible actions

Fluxo records session actions for transactions, accounts, and related changes. Use the History drawer to inspect actions, undo or redo the latest eligible action, or revert to an earlier point in the session.

Transaction and account details also expose their own history where available. Reversal and linked-transaction actions preserve the relationship between related money movements.

### Data management and backups

Open **Settings > Data Backup/Restore** or press `Ctrl+Shift+B`.

JSON backup entities can include accounts, expenses, incomes, tags, goals, recurring transactions, and user settings.

- **Backup:** create a JSON file from selected entities.
- **Append:** add selected backup data and choose Replace, Append, or Ignore for conflicts.
- **Overwrite:** replace selected data.

Default user-backup folder:

```text
%LocalAppData%\fluxo\user_backups
```

After first-run setup, Fluxo also copies the SQLite database at startup to `%LocalAppData%\fluxo\backup`. Automatic startup copies older than three days are pruned.

### Settings, lock, tray, and updates

Settings covers personalization, notifications, security, accounts, recurring transactions, goals, tags, budget management, debt/IOUs, data management, setup, and configuration.

Configuration can run Fluxo with Windows, choose Exit or Minimize to tray when closing, reset settings, delete all data, show the installed version, and check for updates.

UI locking supports a password and auto-lock after 30 seconds, 1 minute, 3 minutes, 5 minutes, 10 minutes, or a custom interval. Locking protects the app window; it does not encrypt the database file.

The system tray can reopen, restart, or exit Fluxo.

> **Screenshot placeholder:** System-tray menu.

## Privacy and storage

Fluxo stores financial data locally in SQLite. It has no cloud sync or bank-connection service.

Database path:

```text
%LocalAppData%\fluxo\fluxo.db
```

Keep a JSON backup before moving to another Windows installation or deleting the Fluxo data folder.

## Keyboard shortcuts

Open **Hotkeys** or press `Ctrl+/` for the complete list.

| Shortcut | Action |
| --- | --- |
| `Ctrl+1` / `Ctrl+2` / `Ctrl+3` / `Ctrl+4` | Dashboard / Analytics / Calendar / Ledger |
| `Ctrl+F` | Search |
| `Ctrl+Q` | Quick Access |
| `Ctrl+N` | New transaction |
| `Ctrl+Shift+N` | New recurring transaction |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+H` | Toggle History |
| `Ctrl+Shift+L` | Lock Fluxo |
| `Ctrl+E` | Export Ledger |
| `Ctrl+Shift+B` | Open Data Management |
| `Ctrl+P` / `Ctrl+Shift+P` | Planning Report / Budget Forecast |

## Build and test

Requirements: Windows x64 and the .NET 10 SDK.

```powershell
dotnet restore Fluxo.slnx
dotnet build Fluxo\Fluxo.csproj -c Release
dotnet test Fluxo.Tests\Fluxo.Tests.csproj -c Release
```

Installer projects use WiX Toolset SDK 7 and publish the Windows x64 application payload through the solution.

## Troubleshooting

### Fluxo starts but no window appears

Fluxo allows one running instance. Check the Windows notification area and open the existing Fluxo window from the tray. If no window or tray icon appears, close any stale Fluxo process and start Fluxo again.

### Fluxo opens in the tray

Open the tray menu and choose Open Fluxo. To change close behavior, select **Settings > Configuration > When closing Fluxo > Exit**.

### The tray icon is not visible

Check the Windows hidden-icons menu and Windows taskbar notification-area settings. Fluxo may still be running even when the icon is hidden.

### Setup wizard does not finish

Complete the required name, account, and budget fields. If an optional recurring transaction or saving goal is invalid, remove it from the wizard and add it later from Settings.

### Dashboard actions are locked

Add or enable an account with enough available balance. Fluxo can hide or disable spending actions when no usable account is available or when the UI is locked.

### Account balance or credit usage looks wrong

Check the account type. Cash, Checking, and Saving use Balance; Credit uses Spent Amount and Account Limit. Review transfers, repayments, reconciliation entries, and deleted or budget-excluded transactions.

### Reconciliation changed the budget

If reconciliation logging was enabled, Fluxo creates a Budget Reconciliation transaction for the difference. Inspect that transaction in Ledger or transaction history.

### A recurring transaction is missing

Check that it is enabled, its schedule date is valid, and its end date has not passed. Confirm the assigned account and goal still exist. Recurring items can be disabled or edited from **Settings > Recurring Transactions**.

### An installment continues longer than expected

Check the installment recurring period and end date. Installments stop when their configured end date is reached.

### A goal contribution cannot be saved

Select a valid goal and source account. Goal updates can use Cash or Checking accounts; credit and saving accounts are not eligible goal-update sources.

### Budget totals do not match expectations

Check the selected allocation period, category, rollover policy, overspend policy, budget-excluded state, and split parent/child structure. Excluded transactions do not count toward budget totals, and split activity is not counted twice.

### Ledger shows no transactions

Clear Ledger filters with `Ctrl+Shift+R`, widen the date range, check the selected type/account/category/tag filters, or use **View all transactions**.

### Calendar shows no item for a date

Select the date again and confirm the item type. Calendar separates expenses, incomes, goal deadlines, and recurring transactions into different lists.

### Imported backup is rejected

Use a Fluxo JSON backup created by the current backup flow. Confirm the file is valid JSON and that it was not edited into an unsupported schema version. Start restore again from **Settings > Data Backup/Restore**.

### Imported data is incomplete

Enable the required entity types. Accounts are a dependency for account-backed expenses, incomes, and recurring transactions. In Append mode, review each conflict and choose Replace, Append, or Ignore.

### Notifications do not appear

Check notification settings, notification snooze state, and the matching account, due date, recurring item, goal, balance, credit usage, or budget threshold. Some notifications only appear when their condition is met.

### Update check fails

Allow Fluxo to reach GitHub release services and try again. The update check needs network access and a release containing a Fluxo installer asset.

### Installer cannot start Fluxo

Run the installer again with network access so it can install or verify the .NET 10 Windows Desktop Runtime. Close any running Fluxo instance before retrying.

### Data appears missing after reinstall

Check `%LocalAppData%\fluxo`, including `fluxo.db`, `backup`, and `user_backups`. Reinstalling Fluxo does not replace the database unless the data folder is removed. Restore a JSON backup if needed.

### Build or test reports a file-in-use error

Close Fluxo and any test process, then rerun the command. A running executable can prevent the build or test output from being replaced.

## Support

Report issues at the [Fluxo GitHub repository](https://github.com/Hokichi/Fluxo/issues). Include Fluxo version, Windows version, reproduction steps, and relevant screenshots or logs.

## Release history

- **1.0.6** - Current release. Adds Global Search, persistent light and dark themes, a revamped Quick Setup flow, customizable Quick Access tiles, bulk transaction entry, live balance previews, improved split-transaction editing, faster cached data access, and safer installer reinstall, version, and package verification behavior.
- **1.0.5** - Adds Calendar, Ledger tools, data management, transaction splitting, budget planning and forecasting, history actions, locking, installments, debt/IOUs, notifications, tray behavior, shortcuts, linked transactions, and related UI improvements and fixes.
- **1.0.4** - Runtime-aware installer and Quick Setup, account, and dashboard improvements.
- **1.0.3** - Installer fix, income search, and insufficient-funds protection.
- **1.0.2** - Recurring transactions and expanded spending/saving records.
- **1.0.1** - Update checks.
- **1.0.0** - Initial release.
