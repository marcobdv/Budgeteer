# Budgeteer - Quick Start

## Current Status

I've created PowerShell scripts to set up the project. Here's what to do:

## Step 1: Create the .NET Projects

Open PowerShell in `C:\repos\private\Budgeteer` and run:

```powershell
.\create-projects.ps1
```

This creates:
- ✅ All 6 .NET projects
- ✅ Solution file with references
- ✅ NuGet packages installed
- ✅ Project dependencies configured

## Step 2: Tell Me When Done

Once you've run the script, let me know and I'll create all the source code files for you.

## What This Will Build

```
Budgeteer/
├── Budgeteer.sln
├── Budgeteer.AppHost/          → Aspire orchestrator
├── Budgeteer.ServiceDefaults/  → Observability setup  
├── Budgeteer.Shared/           → Domain events (cross-domain)
├── Budgeteer.Accounts/         → Account domain + aggregates
├── Budgeteer.Budget/           → Budget domain + aggregates
└── Budgeteer.Web/              → Blazor UI
```

## Architecture Highlights

**Two Event Stores:**
- Account Domain → PostgreSQL #1 (`accounts-eventstore`)
- Budget Domain → PostgreSQL #2 (`budget-eventstore`)

**Event Flow:**
```
User creates transaction
    ↓
Account.TransactionRecorded (saved to accounts DB)
    ↓
[In-process event handler]
    ↓
Budget.ExpenseRecorded (saved to budget DB)
```

## After Setup

Once I create all source files:

```powershell
cd Budgeteer.AppHost
dotnet run
```

- **App**: http://localhost:5000
- **Aspire Dashboard**: http://localhost:15000

---

**Next**: Run `.\create-projects.ps1` then tell me you're ready for source code!
